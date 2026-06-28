namespace VrBook.Contracts.Events;

public sealed record UserRegistered(Guid UserId, string Email, string DisplayName) : DomainEvent;

public sealed record UserEmailVerified(Guid UserId, string Email) : DomainEvent;

public sealed record UserDeactivated(Guid UserId, string Reason) : DomainEvent;

/// <summary>OPS.M.8 §3.1 (D1) — raised when a user is promoted to platform-admin.</summary>
public sealed record UserPlatformAdminGranted(Guid UserId, Guid ActorId) : DomainEvent;

/// <summary>OPS.M.8 §3.1 (D1) — raised when platform-admin is revoked.</summary>
public sealed record UserPlatformAdminRevoked(Guid UserId, Guid ActorId) : DomainEvent;
