using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// User aggregate root. External identity provider (Entra External ID today; more
/// via OPS.M.12) is the source of truth for credentials. This aggregate holds the
/// app-side profile, role flags, and audit-relevant lifecycle events. The
/// (provider, external_id) mapping to this User lives in
/// <see cref="UserIdentity"/> — one User can have many identity mappings.
/// See proposal §4.3 (Identity context) and §14.1 (claim mapping), and
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2 for the redesigned shape.
/// </summary>
public sealed class User : AggregateRoot
{
    public Email Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public PhoneNumber Phone { get; private set; } = new(string.Empty);
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }
    /// <summary>
    /// OPS.M.8 §3.1 (D1) — DB-authoritative platform-admin flag. The
    /// <c>TenantAuthorizationBehavior</c> reads this via the DB-wins
    /// precedence per ADR-0014 (OPS.M.2). Granted/revoked exclusively by
    /// ops Powershell <c>vrbook-admin promote</c> per §3.8 (D8).
    /// </summary>
    public bool IsPlatformAdmin { get; private set; }
    public bool EmailVerified { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }

    private User() { }   // EF Core

    /// <summary>
    /// Slice OPS.M.13 — email-first provisioning. Called by
    /// <c>ProvisionOrLinkUserHandler</c> Branch 3 (identity miss + email
    /// miss) per <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.5.
    /// The (provider, oid) binding is created separately as a
    /// <see cref="UserIdentity"/> row in the same handler transaction.
    /// </summary>
    /// <remarks>
    /// <c>IsOwner</c> and <c>IsAdmin</c> global flags are omitted
    /// intentionally — role assignments happen post-provisioning through
    /// admin flows, not from token claims. Global roles get formally
    /// deprecated in OPS.M.15.
    /// </remarks>
    public static User Provision(
        Email email,
        string displayName,
        bool emailVerified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName.Trim(),
            EmailVerified = emailVerified,
        };

        user.Raise(new UserRegistered(user.Id, email.Value, user.DisplayName));
        if (emailVerified)
        {
            user.Raise(new UserEmailVerified(user.Id, email.Value));
        }
        return user;
    }

    public void UpdateProfile(string displayName, PhoneNumber phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
        Phone = phone;
    }

    /// <summary>
    /// Slice 4 dev bridge: lets the dev "set persona email" endpoint
    /// repoint a DevAuth persona at a real inbox (e.g. so a queued
    /// notification actually lands in Gmail). NOT exposed via the user-facing
    /// profile editor — that flow goes through Entra in production.
    /// </summary>
    public void SetEmail(Email newEmail)
    {
        Email = newEmail;
        EmailVerified = true;
    }

    public void RefreshFromLogin(string displayName, bool emailVerified)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            DisplayName = displayName.Trim();
        }

        if (emailVerified && !EmailVerified)
        {
            EmailVerified = true;
            Raise(new UserEmailVerified(Id, Email.Value));
        }

        LastLoginAt = DateTimeOffset.UtcNow;
    }

    public void MarkEmailVerified()
    {
        if (EmailVerified)
        {
            return;
        }

        EmailVerified = true;
        Raise(new UserEmailVerified(Id, Email.Value));
    }

    public void GrantOwner() => IsOwner = true;
    public void RevokeOwner() => IsOwner = false;
    public void GrantAdmin() => IsAdmin = true;
    public void RevokeAdmin() => IsAdmin = false;

    /// <summary>
    /// OPS.M.8 §3.1 + §3.8 — promote a user to platform-admin. Idempotent
    /// (re-granting is a no-op). Raises <see cref="UserPlatformAdminGranted"/>
    /// for the audit trail.
    /// </summary>
    public void GrantPlatformAdmin(Guid actorId)
    {
        if (IsPlatformAdmin)
        {
            return;
        }
        IsPlatformAdmin = true;
        Raise(new UserPlatformAdminGranted(Id, actorId));
    }

    /// <summary>
    /// OPS.M.8 §3.1 + §3.8 — revoke platform-admin. Idempotent.
    /// </summary>
    public void RevokePlatformAdmin(Guid actorId)
    {
        if (!IsPlatformAdmin)
        {
            return;
        }
        IsPlatformAdmin = false;
        Raise(new UserPlatformAdminRevoked(Id, actorId));
    }

    public void Deactivate(string reason, Guid actorId)
    {
        if (IsDeleted)
        {
            return;
        }
        DeletedAt = DateTimeOffset.UtcNow;
        DeletedBy = actorId;
        Raise(new UserDeactivated(Id, reason));
    }
}
