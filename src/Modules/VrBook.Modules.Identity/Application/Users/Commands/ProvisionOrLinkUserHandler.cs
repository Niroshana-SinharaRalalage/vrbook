using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Auth;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Slice OPS.M.13 — email-first provisioning handler that supersedes
/// <c>ProvisionUserHandler</c> per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.5.
///
/// <para>Three branches:</para>
/// <list type="number">
///   <item>Identity hit — <c>(provider, external_id)</c> already mapped;
///     bump <c>LastSeenAt</c> + refresh login stamps; return the linked
///     <c>users.Id</c>. Cache-fast happy path for every returning
///     sign-in.</item>
///   <item>Identity miss + email hit — link the new identity to the
///     existing user profile. Guarded by <c>email_verified=true</c>.
///     Refusal path throws <c>BusinessRuleViolationException</c> with
///     rule <c>email_unverified_cannot_bind_profile</c>.</item>
///   <item>Identity miss + email miss — provision a fresh user +
///     first identity row atomically. Race with a parallel sign-in on
///     the same fresh email is caught by the partial-UNIQUE on
///     <c>lower(email)</c>; the second attempt re-queries and links.</item>
/// </list>
///
/// <para>Race handling relies on DB constraints, not advisory locks:
/// <c>user_identities_provider_extid_uq</c> for two-tab-same-identity
/// races, <c>users_email_active_lower_uq</c> for two-tab-fresh-email
/// races. Both throw Postgres 23505; the handler catches, clears the
/// change tracker, and re-queries to return the winner's id.</para>
/// </summary>
internal sealed class ProvisionOrLinkUserHandler(
    IdentityDbContext db,
    IUnitOfWork uow,
    IDateTimeProvider clock,
    ILogger<ProvisionOrLinkUserHandler> logger) : IRequestHandler<ProvisionOrLinkUserCommand, Guid>
{
    private const string UsersEmailUniqueConstraint = "users_email_active_lower_uq";
    private const string UserIdentitiesUniqueConstraint = "user_identities_provider_extid_uq";

    public async Task<Guid> Handle(ProvisionOrLinkUserCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.ExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Email);

        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;

        // ---- Branch 1: identity hit ----
        var identity = await db.UserIdentities
            .AsTracking()
            .FirstOrDefaultAsync(
                i => i.Provider == cmd.Provider && i.ExternalId == cmd.ExternalId,
                cancellationToken);
        if (identity is not null)
        {
            identity.UpdateLastSeen(now);
            var user = await db.Users
                .AsTracking()
                .FirstAsync(u => u.Id == identity.UserId, cancellationToken);
            user.RefreshFromLogin(cmd.EmailVerified);
            await uow.SaveChangesAsync(cancellationToken);
            return user.Id;
        }

        // ---- Branch 2: identity miss + email hit ----
        var existingUser = await FindUserByEmailAsync(normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            if (!cmd.EmailVerified)
            {
                throw new BusinessRuleViolationException(
                    "email_unverified_cannot_bind_profile",
                    $"Cannot bind '{cmd.Provider}' identity to existing profile: email '{cmd.Email}' is not verified. Verify your email with your identity provider and sign in again.");
            }

            await RefuseIfAdminSocialLinkAsync(existingUser, cmd, cancellationToken);

            var linkedId = await LinkIdentityToUserAsync(
                existingUser, cmd.Provider, cmd.ExternalId, cmd.EmailVerified, now, cancellationToken);
            return linkedId;
        }

        // ---- Branch 3: identity miss + email miss — fresh provision ----
        var freshUser = User.Provision(new Email(normalizedEmail), cmd.DisplayName, cmd.EmailVerified);
        var firstIdentity = UserIdentity.Create(freshUser.Id, cmd.Provider, cmd.ExternalId, now);
        await db.Users.AddAsync(freshUser, cancellationToken);
        await db.UserIdentities.AddAsync(firstIdentity, cancellationToken);
        try
        {
            await uow.SaveChangesAsync(cancellationToken);
            return freshUser.Id;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, UsersEmailUniqueConstraint))
        {
            // Race: another request just created the users row for this email.
            db.ChangeTracker.Clear();
            var raced = await FindUserByEmailAsync(normalizedEmail, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Users email uniqueness violated but no active row found post-clear; DB in an unexpected state.");
            // Re-check the verified guard against the winner row.
            if (!cmd.EmailVerified)
            {
                throw new BusinessRuleViolationException(
                    "email_unverified_cannot_bind_profile",
                    $"Cannot bind '{cmd.Provider}' identity to existing profile: email '{cmd.Email}' is not verified.");
            }
            // Slice OPS.M.12.4 REFUSE-AT-PROVISIONING — same rule applies
            // on the race-recovery path.
            await RefuseIfAdminSocialLinkAsync(raced, cmd, cancellationToken);
            return await LinkIdentityToUserAsync(
                raced, cmd.Provider, cmd.ExternalId, cmd.EmailVerified, now, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, UserIdentitiesUniqueConstraint))
        {
            // Race: another request just inserted the same (provider, external_id) pair.
            db.ChangeTracker.Clear();
            var raced = await db.UserIdentities
                .FirstAsync(i => i.Provider == cmd.Provider && i.ExternalId == cmd.ExternalId, cancellationToken);
            return raced.UserId;
        }
    }

    /// <summary>
    /// Slice OPS.M.12.4 REFUSE-AT-PROVISIONING.
    ///
    /// <para>Owner policy 2026-07-05: admin users MUST NEVER carry a social
    /// identity linked. If <paramref name="cmd"/> targets a social provider
    /// AND <paramref name="matched"/> has any admin authority
    /// (<c>is_platform_admin</c> OR any active <c>tenant_memberships</c>
    /// row), throws <c>BusinessRuleViolationException</c> with rule
    /// <c>admin_social_signin_refused</c>. No <c>user_identities</c> row is
    /// created; the admin's row stays entra-only.</para>
    ///
    /// <para>Layer 1 defence for admin-role integrity. Layer 2
    /// (<c>AdminSocialIdpRejectionMiddleware</c>) catches the case where
    /// this branch is bypassed by data-heal race, direct SQL, or config
    /// drift.</para>
    /// </summary>
    private async Task RefuseIfAdminSocialLinkAsync(
        User matched,
        ProvisionOrLinkUserCommand cmd,
        CancellationToken cancellationToken)
    {
        if (!HttpCurrentUser.SocialProviderKeys.Contains(cmd.Provider))
        {
            return;
        }

        var hasAdminAuthority = matched.IsPlatformAdmin
            || await db.Set<TenantMembership>()
                .AnyAsync(m => m.UserId == matched.Id && m.DeletedAt == null, cancellationToken);

        if (!hasAdminAuthority)
        {
            return;
        }

        logger.LogWarning(
            "REFUSE-AT-PROVISIONING fired. UserId={UserId} Email={Email} IsPlatformAdmin={PA} AttemptedProvider={Provider} AttemptedExternalId={ExtId}",
            matched.Id, cmd.Email, matched.IsPlatformAdmin, cmd.Provider, cmd.ExternalId);

        throw new BusinessRuleViolationException(
            "admin_social_signin_refused",
            "This email belongs to a tenant admin account. Admin roles must use Entra local sign-in " +
            "(email + password OR email + OTP). Sign in with your Entra credentials instead. If this is " +
            "unexpected, contact your admin. No new identity was created.");
    }

    private Task<User?> FindUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users
            .AsTracking()
            .FirstOrDefaultAsync(
                // The ((string)(object)u.Email) cast is a Npgsql EF Core translator
                // workaround: the Email HasConversion prevents u.Email.Value from
                // being translated inside EF.Functions.ILike (fails at runtime with
                // "could not be translated" — see reference_email_ilike_translator
                // memory + M.13.4 fixup 2 for UserRepository.BuildQ). The soft-delete
                // clause is redundant with the global query filter and dropped so the
                // predicate stays translatable.
                u => EF.Functions.ILike(((string)(object)u.Email), normalizedEmail),
                cancellationToken);

    private async Task<Guid> LinkIdentityToUserAsync(
        User existingUser,
        string provider,
        string externalId,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var newIdentity = UserIdentity.Create(existingUser.Id, provider, externalId, now);
        await db.UserIdentities.AddAsync(newIdentity, cancellationToken);
        existingUser.RefreshFromLogin(emailVerified);
        try
        {
            await uow.SaveChangesAsync(cancellationToken);
            return existingUser.Id;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, UserIdentitiesUniqueConstraint))
        {
            // Race: another request just inserted the same (provider, external_id).
            db.ChangeTracker.Clear();
            var raced = await db.UserIdentities
                .FirstAsync(i => i.Provider == provider && i.ExternalId == externalId, cancellationToken);
            return raced.UserId;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is Npgsql.PostgresException pex
        && pex.SqlState == "23505"
        && string.Equals(pex.ConstraintName, constraintName, StringComparison.Ordinal);
}
