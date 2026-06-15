namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Cross-module read used by Notifications (and any future module that needs
/// to email a user) to resolve a user id to the address + display name we
/// should mail. Mirrors <see cref="IPropertyOwnerLookup"/>: the implementation
/// lives in the Identity module; the interface keeps Notifications free of an
/// Identity dependency.
///
/// <para>
/// <b>Locale</b> is a forward-compat stub. Phase 1 stores no per-user locale,
/// so the adapter returns <c>null</c>; the Mustache template renderer falls
/// back to "en-US". Phase 2 will populate it from a future
/// <c>identity.users.locale</c> column without changing this interface.
/// </para>
/// </summary>
public interface IUserEmailLookup
{
    Task<UserEmailSnapshot?> GetAsync(Guid userId, CancellationToken ct = default);
}

public sealed record UserEmailSnapshot(
    Guid UserId,
    string Email,
    string DisplayName,
    string? Locale);
