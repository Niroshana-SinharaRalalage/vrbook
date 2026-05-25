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
