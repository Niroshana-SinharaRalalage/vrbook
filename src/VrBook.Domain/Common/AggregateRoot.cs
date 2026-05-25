using VrBook.Contracts.Events;

namespace VrBook.Domain.Common;

/// <summary>
/// Base class for aggregate roots. Provides:
/// <list type="bullet">
///   <item>Identity (<see cref="Id"/>) — Guid by convention.</item>
///   <item>Soft-delete + audit columns (managed by EF interceptors, see Infrastructure).</item>
///   <item>Optimistic concurrency token (<see cref="RowVersion"/>).</item>
///   <item>Domain event collection (<see cref="Raise"/> → flushed by <see cref="DequeueEvents"/>).</item>
/// </list>
/// </summary>
public abstract class AggregateRoot : IAggregateRoot
{
    private readonly List<IDomainEvent> _events = new();

    public Guid Id { get; protected set; } = Guid.NewGuid();
    public long RowVersion { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; protected set; }
    public DateTimeOffset? DeletedAt { get; protected set; }
    public Guid? DeletedBy { get; protected set; }

    /// <summary>True iff <see cref="DeletedAt"/> is non-null. Repositories filter by this.</summary>
    public bool IsDeleted => DeletedAt.HasValue;

    protected void Raise(IDomainEvent @event) => _events.Add(@event);

    public IReadOnlyCollection<IDomainEvent> DequeueEvents()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }

    public override bool Equals(object? obj) =>
        obj is AggregateRoot other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
