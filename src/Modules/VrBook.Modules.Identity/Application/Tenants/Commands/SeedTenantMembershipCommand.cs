using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Commands;

/// <summary>
/// Slice OPS.M.10.2 F11.3 — PlatformAdmin bootstrap command. Seeds a
/// <c>tenant_memberships</c> row for an Entra-signed user against a
/// target tenant so the user's subsequent requests resolve
/// <c>ICurrentUser.TenantId</c> via the M.2 <c>UserProvisioningMiddleware</c>
/// path. Used in staging when an Entra account exists but no membership
/// row was created during onboarding.
///
/// <para><b>Auth posture</b>: the calling controller is
/// <c>[Authorize(Roles="PlatformAdmin")]</c> AND the route's
/// <c>tenantId</c> is the trusted target (the OPS.M.8 §3.4 platform
/// pattern). The command is NOT <c>ITenantScoped</c> — the M.4 gate
/// would refuse a cross-tenant write; PlatformAdmin operations
/// intentionally bypass that.</para>
///
/// <para><b>Idempotent</b>: if an active membership row exists for
/// <c>(UserId, TenantId)</c>, the existing id is returned. A
/// soft-deleted row is revived (un-soft-deleted) rather than creating a
/// duplicate that would violate the M.1 partial unique index.</para>
/// </summary>
public sealed record SeedTenantMembershipCommand(
    Guid TenantId,
    string EntraOid,
    string Role,
    bool IsPrimary) : IRequest<SeedTenantMembershipResult>;

public sealed record SeedTenantMembershipResult(Guid MembershipId, bool Created);

internal sealed class SeedTenantMembershipHandler(IdentityDbContext db)
    : IRequestHandler<SeedTenantMembershipCommand, SeedTenantMembershipResult>
{
    public async Task<SeedTenantMembershipResult> Handle(
        SeedTenantMembershipCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.EntraOid);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Role);
        if (cmd.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(cmd));
        }

        // Verify tenant exists (gives a clean 404 instead of an FK violation).
        var tenantExists = await db.Tenants.AnyAsync(t => t.Id == cmd.TenantId, cancellationToken);
        if (!tenantExists)
        {
            throw new NotFoundException("Tenant", cmd.TenantId);
        }

        // Resolve Entra OID -> User.Id. The Entra path stores the OID in
        // identity.users.b2c_object_id (same column the DevAuth path uses).
        var user = await db.Users.FirstOrDefaultAsync(u => u.B2CObjectId == cmd.EntraOid, cancellationToken)
            ?? throw new NotFoundException("User (by EntraOid)", cmd.EntraOid);

        // Idempotent path: active membership already present.
        var existingActive = await db.TenantMemberships
            .FirstOrDefaultAsync(m => m.UserId == user.Id
                                    && m.TenantId == cmd.TenantId
                                    && m.DeletedAt == null, cancellationToken);
        if (existingActive is not null)
        {
            return new SeedTenantMembershipResult(existingActive.Id, Created: false);
        }

        // Revive a soft-deleted row if present (the partial unique index
        // on (user_id, tenant_id) WHERE deleted_at IS NULL is the
        // reason re-inserting would otherwise 23505).
        var softDeleted = await db.TenantMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.UserId == user.Id
                                    && m.TenantId == cmd.TenantId
                                    && m.DeletedAt != null, cancellationToken);
        if (softDeleted is not null)
        {
            softDeleted.Revive();
            if (softDeleted.Role != cmd.Role)
            {
                softDeleted.ChangeRole(cmd.Role);
            }
            if (cmd.IsPrimary && !softDeleted.IsPrimary)
            {
                softDeleted.MakePrimary();
            }
            await db.SaveChangesAsync(cancellationToken);
            return new SeedTenantMembershipResult(softDeleted.Id, Created: false);
        }

        var membership = TenantMembership.Create(user.Id, cmd.TenantId, cmd.Role, cmd.IsPrimary);
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        return new SeedTenantMembershipResult(membership.Id, Created: true);
    }
}
