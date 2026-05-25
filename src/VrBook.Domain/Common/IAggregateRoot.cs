using VrBook.Contracts.Events;

namespace VrBook.Domain.Common;

/// <summary>
/// Marker for aggregate roots. Repositories accept and return aggregate roots only.
/// </summary>
public interface IAggregateRoot
{
    /// <summary>Stable identity.</summary>
    Guid Id { get; }

    /// <summary>Optimistic concurrency token mapped to <c>row_version</c> in Postgres.</summary>
    long RowVersion { get; }

    /// <summary>Events raised since this aggregate was loaded — flushed by the outbox.</summary>
    IReadOnlyCollection<IDomainEvent> DequeueEvents();
}
