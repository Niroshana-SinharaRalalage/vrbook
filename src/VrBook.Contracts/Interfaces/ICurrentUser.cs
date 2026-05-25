namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Ambient access to the calling user inside MediatR handlers and pipeline behaviors.
/// Resolves null when called from a background worker or anonymous endpoint.
/// </summary>
public interface ICurrentUser
{
    /// <summary>App-side user id (NOT the B2C object id).</summary>
    Guid? UserId { get; }

    /// <summary>B2C object id from the JWT <c>oid</c> claim.</summary>
    string? B2CObjectId { get; }

    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsOwner { get; }
    bool IsAdmin { get; }

    bool HasRole(string role);
}
