using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Common;

/// <summary>
/// Fallback for background workers and contexts where there is no HTTP caller.
/// The API request pipeline registers an HTTP-aware implementation that supersedes this.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? B2CObjectId => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public bool IsOwner => false;
    public bool IsAdmin => false;
    public bool HasRole(string role) => false;
}
