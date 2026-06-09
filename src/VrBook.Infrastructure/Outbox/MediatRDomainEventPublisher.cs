using MediatR;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Outbox;

/// <summary>
/// Adapts <see cref="IDomainEvent"/> to MediatR's <see cref="INotification"/> dispatcher.
/// Every <c>IDomainEvent</c> already extends <c>INotification</c>, so <see cref="IPublisher.Publish"/>
/// fires any <see cref="INotificationHandler{TEvent}"/> registered in DI.
///
/// In-process only. Cross-process delivery (Service Bus) is wired in A11 via the
/// outbox relay; this publisher is the synchronous, same-process path.
/// </summary>
public sealed class MediatRDomainEventPublisher(IPublisher mediator) : IDomainEventPublisher
{
    public Task PublishAsync(IDomainEvent domainEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return mediator.Publish(domainEvent, ct);
    }

    public async Task PublishAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);
        foreach (var ev in domainEvents)
        {
            await mediator.Publish(ev, ct);
        }
    }
}
