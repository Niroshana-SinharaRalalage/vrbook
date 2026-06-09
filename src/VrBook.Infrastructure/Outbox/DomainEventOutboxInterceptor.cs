using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Infrastructure.Outbox;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that drains <see cref="IDomainEvent"/>s
/// from every changed <see cref="AggregateRoot"/> and:
///   1. Writes one <see cref="OutboxMessage"/> per event into the same transaction as
///      the aggregate state change (so events survive a crash between commit and dispatch).
///   2. After the transaction commits, fires the events through
///      <see cref="IDomainEventPublisher"/> so in-process handlers run synchronously.
///
/// Registered as <b>scoped</b> in DI so the interceptor instance is created per-DbContext
/// (state on <see cref="_pending"/> is per-SaveChanges-call). EF's
/// <c>DbContextOptionsBuilder.AddInterceptors(IServiceProvider, …)</c> resolves it from
/// the scoped container automatically.
/// </summary>
public sealed class DomainEventOutboxInterceptor(
    IDomainEventPublisher publisher,
    ILogger<DomainEventOutboxInterceptor> logger) : SaveChangesInterceptor
{
    // Per-call buffer of events to publish post-commit. Lives only while the SaveChanges
    // pipeline is active because this interceptor is scoped.
    private List<IDomainEvent>? _pending;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null)
        {
            return ValueTask.FromResult(result);
        }

        // Pull events from each tracked aggregate. DequeueEvents() also clears the
        // internal list, so we only see each event once per SaveChanges call.
        var events = ctx.ChangeTracker.Entries<AggregateRoot>()
            .SelectMany(e => e.Entity.DequeueEvents())
            .ToList();

        if (events.Count == 0)
        {
            _pending = null;
            return ValueTask.FromResult(result);
        }

        // Persist outbox rows into the same transaction. EF batches them with the
        // aggregate INSERT/UPDATEs into a single round-trip.
        foreach (var ev in events)
        {
            ctx.Set<OutboxMessage>().Add(new OutboxMessage(ev));
        }

        _pending = events;
        logger.LogInformation(
            "Queued {EventCount} domain event(s) into outbox for {DbContext}; will publish in-process after commit.",
            events.Count,
            ctx.GetType().Name);

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (_pending is { Count: > 0 } pending)
        {
            _pending = null; // clear before publish so handler reentrancy is safe
            try
            {
                await publisher.PublishAsync(pending, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't fail the commit — the rows are durably written to the outbox.
                // The A11 outbox→Service-Bus relay will retry from there.
                logger.LogWarning(ex,
                    "In-process publication of {EventCount} domain event(s) failed; outbox rows persist for relay.",
                    pending.Count);
            }
        }
        return result;
    }
}
