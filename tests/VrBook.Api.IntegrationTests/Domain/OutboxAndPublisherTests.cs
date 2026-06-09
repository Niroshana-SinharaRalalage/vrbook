using FluentAssertions;
using MediatR;
using NSubstitute;
using VrBook.Contracts.Events;
using VrBook.Infrastructure.Outbox;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for A0.3 — the event-bus wiring that closes the gap where domain events
/// were silently discarded. Two sub-units here: (1) the MediatR-backed publisher
/// that adapts <see cref="IDomainEvent"/> to <see cref="INotification"/>, and (2) the
/// <see cref="OutboxMessage"/> entity that records each event for durable replay.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MediatRDomainEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_single_event_invokes_mediator_publish_once()
    {
        var mediator = Substitute.For<IPublisher>();
        var sut = new MediatRDomainEventPublisher(mediator);
        var ev = new TestDomainEvent("a");

        await sut.PublishAsync(ev);

        await mediator.Received(1).Publish(ev, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_batch_invokes_mediator_publish_per_event_in_order()
    {
        var mediator = Substitute.For<IPublisher>();
        var sut = new MediatRDomainEventPublisher(mediator);
        var events = new[]
        {
            new TestDomainEvent("a"),
            new TestDomainEvent("b"),
            new TestDomainEvent("c"),
        };

        await sut.PublishAsync(events);

        // Each event published exactly once.
        foreach (var ev in events)
        {
            await mediator.Received(1).Publish(ev, Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task PublishAsync_empty_batch_does_nothing()
    {
        var mediator = Substitute.For<IPublisher>();
        var sut = new MediatRDomainEventPublisher(mediator);

        await sut.PublishAsync(Array.Empty<IDomainEvent>());

        await mediator.DidNotReceive().Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }
}

[Trait("Category", "Unit")]
public sealed class OutboxMessageTests
{
    [Fact]
    public void Constructor_captures_event_metadata_and_serializes_payload()
    {
        var ev = new TestDomainEvent("hello");
        var msg = new OutboxMessage(ev);

        msg.EventId.Should().Be(ev.EventId);
        msg.EventType.Should().Be(typeof(TestDomainEvent).FullName);
        msg.OccurredAt.Should().Be(ev.OccurredAt);
        msg.Payload.Should().Contain("hello");
        msg.DispatchedAt.Should().BeNull();
        msg.RetryCount.Should().Be(0);
        msg.LastError.Should().BeNull();
    }

    [Fact]
    public void MarkDispatched_sets_timestamp_and_clears_last_error()
    {
        var msg = new OutboxMessage(new TestDomainEvent("x"));
        msg.RecordFailure("transient");

        var at = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
        msg.MarkDispatched(at);

        msg.DispatchedAt.Should().Be(at);
        msg.LastError.Should().BeNull();
    }

    [Fact]
    public void RecordFailure_increments_retry_count_and_stores_latest_error()
    {
        var msg = new OutboxMessage(new TestDomainEvent("x"));

        msg.RecordFailure("first");
        msg.RecordFailure("second");

        msg.RetryCount.Should().Be(2);
        msg.LastError.Should().Be("second");
        msg.DispatchedAt.Should().BeNull("retries do not mark dispatch complete");
    }

    [Fact]
    public void Payload_round_trips_through_System_Text_Json()
    {
        var ev = new TestDomainEvent("round-trip");
        var msg = new OutboxMessage(ev);

        var decoded = System.Text.Json.JsonSerializer.Deserialize<TestDomainEvent>(msg.Payload);

        decoded.Should().NotBeNull();
        decoded!.Greeting.Should().Be("round-trip");
        decoded.EventId.Should().Be(ev.EventId);
    }
}

/// <summary>Test double for A0.3 — minimal IDomainEvent record.</summary>
internal sealed record TestDomainEvent(string Greeting) : DomainEvent;
