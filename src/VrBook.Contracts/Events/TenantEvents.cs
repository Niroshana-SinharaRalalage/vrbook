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

// OPS.M.5 §3.9 (D9) — Stripe Connect lifecycle events. Raised by
// Tenant.UpdateStripeAccountReadiness when the connected account transitions
// from PendingOnboarding to Active (Onboarded) or from Active to Suspended
// (Suspended). Slice OPS.M.7's wizard subscribes to the first; Slice OPS.M.8's
// Super Admin console + ops dashboards subscribe to the second.

public sealed record TenantStripeOnboarded(Guid TenantId, string StripeAccountId) : DomainEvent;

public sealed record TenantStripeSuspended(
    Guid TenantId, string StripeAccountId, string Reason) : DomainEvent;
