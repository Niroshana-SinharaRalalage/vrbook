using VrBook.Contracts.Events;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Outbox + Service Bus publisher. Aggregates raise events; the application persists them
/// in the same transaction as the state change; this publisher flushes them to Service Bus
/// after commit. In-process subscribers (MediatR notification handlers) fire synchronously.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default);
    Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default);
}
