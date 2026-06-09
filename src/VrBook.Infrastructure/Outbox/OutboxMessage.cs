using System.Text.Json;
using VrBook.Contracts.Events;

namespace VrBook.Infrastructure.Outbox;

/// <summary>
/// Durable record of a domain event. Written into the same transaction as the
/// aggregate state change that produced it, so events are never lost on crash.
/// In Phase 1 (A0.3) the only consumer is the in-process MediatR dispatcher;
/// the outbox→Service Bus relay lands in A11.
///
/// Schema is per module (catalog.outbox_messages, booking.outbox_messages, …)
/// — keeps each module's writes inside its own transaction boundary.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Setters are written by EF Core via reflection at materialization time.")]
public sealed class OutboxMessage
{
    public long Id { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = default!;
    public string Payload { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? DispatchedAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { } // EF

    public OutboxMessage(IDomainEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        EventId = ev.EventId;
        EventType = ev.GetType().FullName ?? ev.GetType().Name;
        // Serialize against the concrete type so polymorphic properties are preserved.
        Payload = JsonSerializer.Serialize(ev, ev.GetType());
        OccurredAt = ev.OccurredAt;
    }

    public void MarkDispatched(DateTimeOffset at)
    {
        DispatchedAt = at;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error;
    }
}
