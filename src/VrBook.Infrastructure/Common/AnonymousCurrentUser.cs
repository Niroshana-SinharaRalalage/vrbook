using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Common;

/// <summary>
/// Fallback for background workers and contexts where there is no HTTP caller.
/// The API request pipeline registers an HTTP-aware implementation that supersedes this.
/// </summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<string>> EmptyMembershipRoles =
        new Dictionary<Guid, IReadOnlySet<string>>();

    public Guid? UserId => null;
    public string? ExternalObjectId => null;
    public string? IdentityProvider => null;
    public string? Email => null;
    public bool IsAuthenticated => false;
    public bool IsOwner => false;
    public bool IsAdmin => false;
    public bool IsPlatformAdmin => false;
    public Guid? TenantId => null;
    public IReadOnlyDictionary<Guid, IReadOnlySet<string>> MembershipRoles => EmptyMembershipRoles;
    public bool HasRole(string role) => false;
    public bool HasTenantRole(Guid tenantId, string role) => false;
}
