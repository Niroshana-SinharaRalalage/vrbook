using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Slice OPS.M.22 §3 — PlatformAdmin pre-seeds an admin's
/// <c>identity.users</c> row BEFORE the admin's first sign-in. Uploads the
/// email + display-name + role shape (platform-admin flag + optional list
/// of tenant memberships). The DB row is created with <c>pre_seeded_at =
/// NOW()</c> so <c>UserProvisioningMiddleware</c> can distinguish
/// "operator vouched" from "random signup" on the first sign-in.
///
/// <para><b>Idempotency:</b> the handler is idempotent on normalized email.
/// A repeat request for the same email returns the existing user id with
/// <c>Created=false</c>, and MERGES any newly-requested memberships. A
/// request whose email matches a NON-pre-seeded row (e.g. a guest with the
/// same email) throws <see cref="ConflictException"/> — the operator must
/// resolve the collision manually (owner-lock in plan §4 risk #3).</para>
///
/// <para><b>Auth posture:</b> gated by
/// <c>[Authorize(Roles="PlatformAdmin")]</c> at the controller. NOT
/// <c>ITenantScoped</c> — this is a platform-level write. Route
/// <c>tenantId</c>s inside the payload are validated against
/// <c>identity.tenants</c> before writing (single transaction; validation
/// failure = 404, nothing written).</para>
/// </summary>
public sealed record SeedAdminUserCommand(
    string Email,
    string DisplayName,
    bool IsPlatformAdmin,
    IReadOnlyList<SeedAdminUserTenantMembership> TenantMemberships)
    : IRequest<SeedAdminUserResult>, VrBook.Modules.Identity.Application.Behaviors.IAuditable
{
    public string AuditAction => "admin.pre-seed-user";
    public string? AuditTargetType => "User";
    public string? AuditTargetId => Email.ToLowerInvariant();
}

internal sealed class SeedAdminUserHandler(
    IdentityDbContext db,
    IDateTimeProvider clock,
    ICurrentUser currentUser)
    : IRequestHandler<SeedAdminUserCommand, SeedAdminUserResult>
{
    public async Task<SeedAdminUserResult> Handle(
        SeedAdminUserCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.DisplayName);

        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;

        // Validate every tenant id in the payload BEFORE writing anything.
        // Empty list is fine (platform-only admin with no tenant membership).
        // Duplicate tenant ids in the payload are collapsed silently.
        var requestedTenantIds = cmd.TenantMemberships
            .Select(m => m.TenantId)
            .Distinct()
            .ToArray();
        if (requestedTenantIds.Length > 0)
        {
            var existingTenantIds = await db.Tenants
                .Where(t => requestedTenantIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToArrayAsync(cancellationToken);
            var missing = requestedTenantIds.Except(existingTenantIds).ToArray();
            if (missing.Length > 0)
            {
                throw new NotFoundException("Tenant", string.Join(",", missing));
            }
        }

        // Existing-row branch: idempotent on email match.
        // The email lookup mirrors ProvisionOrLinkUserHandler.FindUserByEmailAsync:
        // the ((string)(object)u.Email) cast survives the Email value-conversion
        // for ILike-based lookups (reference_email_ilike_translator memory).
        var existing = await db.Users
            .AsTracking()
            .FirstOrDefaultAsync(
                u => EF.Functions.ILike(((string)(object)u.Email), normalizedEmail),
                cancellationToken);

        if (existing is not null)
        {
            // Collision guard: an already-linked row (i.e. row exists AND
            // wasn't pre-seeded — probably a guest self-signup with the same
            // email) is a policy conflict the operator must resolve.
            if (existing.PreSeededAt is null)
            {
                throw new ConflictException(
                    $"Email '{normalizedEmail}' is already linked to a non-pre-seeded user " +
                    $"(id={existing.Id:D}). Manual reconciliation required per M.22 plan §4 " +
                    $"risk #3. Do NOT overwrite the existing row from this handler.");
            }

            // Bump platform-admin if the payload elevates.
            if (cmd.IsPlatformAdmin && !existing.IsPlatformAdmin)
            {
                existing.GrantPlatformAdmin(currentUser.UserId ?? Guid.Empty);
            }

            // Merge memberships: create only the ones that don't yet exist.
            var membershipsCreated = await MergeMembershipsAsync(
                existing.Id, cmd.TenantMemberships, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return new SeedAdminUserResult(existing.Id, Created: false, membershipsCreated);
        }

        // Fresh-provision branch.
        var user = User.PreSeedForOperator(new Email(normalizedEmail), cmd.DisplayName, now);
        if (cmd.IsPlatformAdmin)
        {
            user.GrantPlatformAdmin(currentUser.UserId ?? Guid.Empty);
        }
        await db.Users.AddAsync(user, cancellationToken);

        var freshMemberships = new List<Guid>();
        foreach (var m in cmd.TenantMemberships)
        {
            var membership = TenantMembership.Create(user.Id, m.TenantId, m.Role, m.IsPrimary);
            await db.TenantMemberships.AddAsync(membership, cancellationToken);
            freshMemberships.Add(m.TenantId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SeedAdminUserResult(user.Id, Created: true, freshMemberships);
    }

    /// <summary>
    /// Merges the payload's tenant memberships against what's already in the
    /// DB for <paramref name="userId"/>. Returns the tenant ids that got a
    /// fresh membership row on THIS call (already-existing ones are omitted).
    /// A soft-deleted membership on the (user, tenant) pair is revived + role
    /// updated to match the payload (mirrors <c>SeedTenantMembershipHandler</c>'s
    /// M.10.2 F11.3 revival path).
    /// </summary>
    private async Task<IReadOnlyList<Guid>> MergeMembershipsAsync(
        Guid userId,
        IReadOnlyList<SeedAdminUserTenantMembership> requested,
        CancellationToken cancellationToken)
    {
        var created = new List<Guid>();
        foreach (var m in requested)
        {
            var active = await db.TenantMemberships
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.TenantId == m.TenantId && x.DeletedAt == null,
                    cancellationToken);
            if (active is not null)
            {
                // Idempotent: role/primary already match, or update to payload shape.
                if (active.Role != m.Role)
                {
                    active.ChangeRole(m.Role);
                }

                if (m.IsPrimary && !active.IsPrimary)
                {
                    active.MakePrimary();
                }

                continue;
            }

            var softDeleted = await db.TenantMemberships
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    x => x.UserId == userId && x.TenantId == m.TenantId && x.DeletedAt != null,
                    cancellationToken);
            if (softDeleted is not null)
            {
                softDeleted.Revive();
                if (softDeleted.Role != m.Role)
                {
                    softDeleted.ChangeRole(m.Role);
                }

                if (m.IsPrimary && !softDeleted.IsPrimary)
                {
                    softDeleted.MakePrimary();
                }

                created.Add(m.TenantId);
                continue;
            }

            var membership = TenantMembership.Create(userId, m.TenantId, m.Role, m.IsPrimary);
            await db.TenantMemberships.AddAsync(membership, cancellationToken);
            created.Add(m.TenantId);
        }
        return created;
    }
}
