using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// User aggregate root. AD B2C is the source of truth for credentials; this aggregate
/// holds the app-side profile, role flags, and audit-relevant lifecycle events.
/// See proposal §4.3 (Identity context) and §14.1 (claim mapping).
/// </summary>
public sealed class User : AggregateRoot
{
    /// <summary>B2C `oid` claim — stable per-identity across name and email changes.</summary>
    public string B2CObjectId { get; private set; } = default!;

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
    /// Provision a new app-side user from a verified AD B2C token. Called once on
    /// first-login per <c>b2c_object_id</c>.
    /// </summary>
    public static User Provision(
        string b2cObjectId,
        Email email,
        string displayName,
        bool emailVerified,
        bool isOwner,
        bool isAdmin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(b2cObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var user = new User
        {
            Id = Guid.NewGuid(),
            B2CObjectId = b2cObjectId,
            Email = email,
            DisplayName = displayName.Trim(),
            EmailVerified = emailVerified,
            IsOwner = isOwner,
            IsAdmin = isAdmin,
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

    /// <summary>
    /// Slice OPS.M.10.2 F11.7.6 — rebind this aggregate's <c>B2CObjectId</c>
    /// to a new oid. Called from <c>ProvisionUserHandler</c> when an
    /// incoming oid is not found but the email matches this row. Raises
    /// <see cref="UserOidRebound"/> for the audit trail. Idempotent —
    /// re-binding to the SAME oid is a no-op (no event).
    ///
    /// <para>Preconditions: not soft-deleted; <paramref name="newOid"/>
    /// non-empty. The handler enforces the guardrail (both-oids-real-Entra
    /// throws before this is called), so this method does not re-verify.</para>
    /// </summary>
    public void ClaimOidForExistingProfile(string newOid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newOid);
        if (IsDeleted)
        {
            throw new InvalidOperationException(
                "Cannot rebind oid on a soft-deleted user row.");
        }
        if (string.Equals(B2CObjectId, newOid, StringComparison.Ordinal))
        {
            return; // idempotent — same oid, no event
        }
        var oldOid = B2CObjectId;
        B2CObjectId = newOid;
        Raise(new UserOidRebound(Id, oldOid, newOid));
    }
}
