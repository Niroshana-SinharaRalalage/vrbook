namespace VrBook.Contracts.Events;

public sealed record TenantCreated(Guid TenantId, string Slug, string DisplayName) : DomainEvent;

public sealed record TenantActivated(Guid TenantId) : DomainEvent;

public sealed record TenantSuspended(Guid TenantId, string Reason, Guid ActorId) : DomainEvent;

public sealed record TenantClosed(Guid TenantId) : DomainEvent;

public sealed record TenantMembershipCreated(
    Guid MembershipId, Guid UserId, Guid TenantId, string Role) : DomainEvent;

public sealed record TenantMembershipRoleChanged(
    Guid MembershipId, string OldRole, string NewRole) : DomainEvent;

public sealed record TenantMembershipRevoked(
    Guid MembershipId, Guid UserId, Guid TenantId) : DomainEvent;
