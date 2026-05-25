using MediatR;

namespace VrBook.Contracts.Events;

/// <summary>
/// Marker for all domain events. In-process dispatch via MediatR's <see cref="INotification"/>;
/// cross-process dispatch via Service Bus envelope by the outbox worker.
/// </summary>
public interface IDomainEvent : INotification
{
    /// <summary>Stable identifier — used for idempotency on the consumer side.</summary>
    Guid EventId { get; }

    /// <summary>Wall-clock at which the producing aggregate raised the event.</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Convenience base for records — supplies <see cref="EventId"/> and <see cref="OccurredAt"/>.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
