namespace VrBook.Contracts.Events;

public sealed record UserRegistered(Guid UserId, string Email, string DisplayName) : DomainEvent;

public sealed record UserEmailVerified(Guid UserId, string Email) : DomainEvent;

public sealed record UserDeactivated(Guid UserId, string Reason) : DomainEvent;
