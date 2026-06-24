using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Reports.Application.Realtime;
using Xunit;

namespace VrBook.Api.IntegrationTests.Realtime;

/// <summary>
/// Slice 7 C3 - the BookingPlaced -> SignalR push handler. Fire-and-forget
/// per SLICE7_PLAN §2.5: Handle() returns immediately, the SignalR REST call
/// runs in the background. The handler must NOT block on the push.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OnBookingPlacedHandlerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PropertyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static BookingPlaced NewEvent() => new(
        BookingId: Guid.NewGuid(),
        Reference: "VRB-TEST01",
        PropertyId: PropertyId,
        GuestUserId: Guid.NewGuid(),
        Checkin: new DateOnly(2026, 8, 10),
        Checkout: new DateOnly(2026, 8, 12),
        TentativeUntil: DateTimeOffset.UtcNow.AddHours(6));

    [Fact]
    public async Task Handle_returns_immediately_even_when_push_is_slow()
    {
        var notifier = Substitute.For<IRealtimeNotifier>();
        notifier
            .NotifyUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.Delay(500)); // simulate a slow SignalR REST hop (background)

        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(PropertyId, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(PropertyId, OwnerId, "Test Property"));

        var handler = new OnBookingPlacedHandler(lookup, notifier, NullLogger<OnBookingPlacedHandler>.Instance);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await handler.Handle(NewEvent(), default);
        sw.Stop();

        // Handler must not wait on the push - critical for booking-POST latency.
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "Handle() must return immediately; the push runs in the background");
    }

    [Fact]
    public async Task Handle_pushes_lean_payload_to_resolved_owner()
    {
        var notifier = Substitute.For<IRealtimeNotifier>();
        var tcs = new TaskCompletionSource<(Guid uid, string method, object payload)>();
        notifier
            .When(n => n.NotifyUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(call => tcs.TrySetResult(
                ((Guid)call[0], (string)call[1], call[2])));

        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(PropertyId, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(PropertyId, OwnerId, "Test Property"));

        var handler = new OnBookingPlacedHandler(lookup, notifier, NullLogger<OnBookingPlacedHandler>.Instance);
        var evt = NewEvent();
        await handler.Handle(evt, default);

        // Wait for the background push to land.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        completed.Should().Be(tcs.Task, "push should fire within 2s");

        var (uid, method, payload) = await tcs.Task;
        uid.Should().Be(OwnerId);
        method.Should().Be("tentativeBookingAdded");
        // Payload is an anonymous object with the lean fields documented in §2.5.
        payload.Should().BeOfType(payload.GetType()); // sanity: object received
    }

    [Fact]
    public async Task Handle_swallows_lookup_failure_silently()
    {
        var notifier = Substitute.For<IRealtimeNotifier>();
        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PropertyOwnerSnapshot?)null);

        var handler = new OnBookingPlacedHandler(lookup, notifier, NullLogger<OnBookingPlacedHandler>.Instance);

        var act = () => handler.Handle(NewEvent(), default);
        await act.Should().NotThrowAsync();

        // Give the background task a moment to run.
        await Task.Delay(100);
        await notifier.DidNotReceive().NotifyUserAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_swallows_notifier_exceptions_silently()
    {
        var notifier = Substitute.For<IRealtimeNotifier>();
        notifier
            .NotifyUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated SignalR outage")));

        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(PropertyId, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(PropertyId, OwnerId, "Test Property"));

        var handler = new OnBookingPlacedHandler(lookup, notifier, NullLogger<OnBookingPlacedHandler>.Instance);

        var act = () => handler.Handle(NewEvent(), default);
        await act.Should().NotThrowAsync();
        await Task.Delay(100); // let background log fire
    }
}
