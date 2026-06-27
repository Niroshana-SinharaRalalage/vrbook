using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A6 Sync aggregates. Pure domain behaviour — no DbContext,
/// no parsing, no HTTP. Covers: ChannelFeed lifecycle + poll scheduling,
/// ExternalReservation import/update/cancel + overlap math, SyncConflict
/// detection + resolution rules, SyncRun start/complete/fail invariants.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChannelFeedAggregateTests
{
    private static ChannelFeed New(string url = "https://www.airbnb.com/calendar/ical/12345.ics?s=abc") =>
        ChannelFeed.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), ChannelKind.AirBnb, url, pollIntervalMinutes: 30);

    [Fact]
    public void Create_initializes_with_enabled_state_and_random_outbound_token()
    {
        var feed = New();

        feed.IsEnabled.Should().BeTrue();
        feed.PollIntervalMinutes.Should().Be(30);
        feed.OutboundToken.Should().NotBeNullOrWhiteSpace();
        feed.OutboundToken.Length.Should().Be(32, "Guid.ToString(N) is 32 hex chars");
        feed.LastSuccessAt.Should().BeNull();
        feed.LastAttemptAt.Should().BeNull();
        feed.ConsecutiveFailures.Should().Be(0);
        feed.ETag.Should().BeNull();
    }

    [Fact]
    public void Create_trims_inbound_url()
    {
        var feed = ChannelFeed.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), ChannelKind.AirBnb,
            "   https://www.airbnb.com/calendar/ical/x.ics   ");
        feed.InboundUrl.Should().Be("https://www.airbnb.com/calendar/ical/x.ics");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://files.example.com/cal.ics")]
    [InlineData("file:///tmp/cal.ics")]
    [InlineData("javascript:alert(1)")]
    public void Create_rejects_non_http_url(string badUrl)
    {
        var act = () => ChannelFeed.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), ChannelKind.AirBnb, badUrl);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.feed.url");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Create_rejects_poll_interval_below_5_minutes(int interval)
    {
        var act = () => ChannelFeed.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), ChannelKind.AirBnb,
            "https://e.com/c.ics", pollIntervalMinutes: interval);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.feed.poll_interval");
    }

    [Fact]
    public void UpdateConfig_overwrites_and_clears_disabled_flag()
    {
        var feed = New();
        feed.UpdateConfig("https://e.com/new.ics", 60, isEnabled: false);

        feed.InboundUrl.Should().Be("https://e.com/new.ics");
        feed.PollIntervalMinutes.Should().Be(60);
        feed.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Pause_and_Resume_toggle_enabled()
    {
        var feed = New();
        feed.Pause();
        feed.IsEnabled.Should().BeFalse();
        feed.Resume();
        feed.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccess_resets_failures_and_stores_etag()
    {
        var feed = New();
        var now = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

        feed.RecordFailure(now, "network");
        feed.RecordFailure(now.AddMinutes(5), "network");
        feed.ConsecutiveFailures.Should().Be(2);

        feed.RecordSuccess(now.AddMinutes(10), etag: "W/\"abc\"", lastModified: now.AddMinutes(-1));

        feed.ConsecutiveFailures.Should().Be(0);
        feed.LastError.Should().BeNull();
        feed.LastSuccessAt.Should().Be(now.AddMinutes(10));
        feed.LastAttemptAt.Should().Be(now.AddMinutes(10));
        feed.ETag.Should().Be("W/\"abc\"");
    }

    [Fact]
    public void RecordFailure_increments_consecutive_count_and_keeps_last_success()
    {
        var feed = New();
        var t0 = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        feed.RecordSuccess(t0, "etag1", null);

        feed.RecordFailure(t0.AddMinutes(30), "boom");
        feed.RecordFailure(t0.AddMinutes(60), "boom");
        feed.RecordFailure(t0.AddMinutes(90), "boom");

        feed.ConsecutiveFailures.Should().Be(3);
        feed.LastError.Should().Be("boom");
        feed.LastSuccessAt.Should().Be(t0, "success timestamp must not be overwritten by failures");
    }

    [Fact]
    public void IsDueForPoll_returns_true_when_never_attempted()
    {
        var feed = New();
        feed.IsDueForPoll(DateTimeOffset.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsDueForPoll_returns_false_when_disabled_even_if_overdue()
    {
        var feed = New();
        feed.Pause();
        feed.IsDueForPoll(DateTimeOffset.UtcNow.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void IsDueForPoll_false_within_interval_window()
    {
        var feed = New();
        var t0 = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);
        feed.RecordAttemptStarted(t0);

        feed.IsDueForPoll(t0.AddMinutes(29)).Should().BeFalse();
        feed.IsDueForPoll(t0.AddMinutes(30)).Should().BeTrue();
        feed.IsDueForPoll(t0.AddMinutes(31)).Should().BeTrue();
    }
}

[Trait("Category", "Unit")]
public sealed class ExternalReservationAggregateTests
{
    private static ExternalReservation Import(
        DateOnly? checkin = null,
        DateOnly? checkout = null,
        string ical = "evt-1@airbnb.com",
        string? summary = "Reserved (Not available)") =>
        ExternalReservation.Import(new Guid("00000000-0000-0000-0000-000000000001"),
            channelFeedId: Guid.NewGuid(),
            propertyId: Guid.NewGuid(),
            channel: ChannelKind.AirBnb,
            iCalUid: ical,
            checkin: checkin ?? new DateOnly(2026, 7, 10),
            checkout: checkout ?? new DateOnly(2026, 7, 14),
            summary: summary,
            rawPayload: "BEGIN:VEVENT\nUID:evt-1\nEND:VEVENT");

    [Fact]
    public void Import_creates_active_reservation_and_raises_event()
    {
        var er = Import();

        er.IsActive.Should().BeTrue();
        er.CancelledAt.Should().BeNull();
        er.ICalUid.Should().Be("evt-1@airbnb.com");
        er.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<ExternalReservationImported>();
    }

    [Fact]
    public void Import_with_checkout_on_same_day_throws()
    {
        var act = () => Import(
            checkin: new DateOnly(2026, 7, 10),
            checkout: new DateOnly(2026, 7, 10));
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.reservation.date_range");
    }

    [Fact]
    public void Import_with_checkout_before_checkin_throws()
    {
        var act = () => Import(
            checkin: new DateOnly(2026, 7, 14),
            checkout: new DateOnly(2026, 7, 10));
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.reservation.date_range");
    }

    [Fact]
    public void Update_changes_dates_and_re_raises_imported_event()
    {
        var er = Import();
        er.DequeueEvents();

        er.Update(new DateOnly(2026, 7, 12), new DateOnly(2026, 7, 16), "Updated", "raw2");

        er.Checkin.Should().Be(new DateOnly(2026, 7, 12));
        er.Checkout.Should().Be(new DateOnly(2026, 7, 16));
        er.Summary.Should().Be("Updated");
        er.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<ExternalReservationImported>(
            because: "downstream consumers must re-evaluate conflicts on date changes");
    }

    [Fact]
    public void MarkCancelled_sets_timestamp_raises_event_and_is_idempotent()
    {
        var er = Import();
        er.DequeueEvents();

        er.MarkCancelled();
        er.IsActive.Should().BeFalse();
        er.CancelledAt.Should().NotBeNull();
        er.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<ExternalReservationCancelled>();

        var firstCancelledAt = er.CancelledAt;
        er.MarkCancelled(); // no-op
        er.CancelledAt.Should().Be(firstCancelledAt);
        er.DequeueEvents().Should().BeEmpty("second cancel must not re-publish");
    }

    [Theory]
    // [overlapping pairs: er=[10,14), other=[?,?)]
    [InlineData("2026-07-12", "2026-07-15", true)]   // other starts during ER
    [InlineData("2026-07-08", "2026-07-12", true)]   // other ends during ER
    [InlineData("2026-07-10", "2026-07-14", true)]   // identical
    [InlineData("2026-07-09", "2026-07-15", true)]   // other contains ER
    [InlineData("2026-07-11", "2026-07-13", true)]   // ER contains other
    [InlineData("2026-07-14", "2026-07-20", false)]  // other starts exactly when ER ends (half-open)
    [InlineData("2026-07-05", "2026-07-10", false)]  // other ends exactly when ER starts (half-open)
    [InlineData("2026-07-01", "2026-07-05", false)]  // entirely before
    [InlineData("2026-07-20", "2026-07-25", false)]  // entirely after
    public void OverlapsWith_uses_half_open_semantics(string otherInStr, string otherOutStr, bool expected)
    {
        var er = Import(
            checkin: new DateOnly(2026, 7, 10),
            checkout: new DateOnly(2026, 7, 14));

        var actual = er.OverlapsWith(DateOnly.Parse(otherInStr, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(otherOutStr, System.Globalization.CultureInfo.InvariantCulture));

        actual.Should().Be(expected,
            $"reservation [2026-07-10, 2026-07-14) vs other [{otherInStr}, {otherOutStr})");
    }

    [Fact]
    public void OverlapsWith_returns_false_for_cancelled_reservation()
    {
        var er = Import(
            checkin: new DateOnly(2026, 7, 10),
            checkout: new DateOnly(2026, 7, 14));
        er.MarkCancelled();

        er.OverlapsWith(new DateOnly(2026, 7, 11), new DateOnly(2026, 7, 13))
            .Should().BeFalse("a cancelled external reservation no longer conflicts");
    }
}

[Trait("Category", "Unit")]
public sealed class SyncConflictAggregateTests
{
    private static SyncConflict Detect() => SyncConflict.Detect(new Guid("00000000-0000-0000-0000-000000000001"),
        propertyId: Guid.NewGuid(),
        bookingId: Guid.NewGuid(),
        externalReservationId: Guid.NewGuid(),
        channel: ChannelKind.AirBnb);

    [Fact]
    public void Detect_creates_pending_conflict_and_raises_SyncConflictDetected()
    {
        var c = Detect();

        c.Resolution.Should().Be(SyncConflictResolution.Pending);
        c.IsResolved.Should().BeFalse();
        c.ResolvedAt.Should().BeNull();
        c.DetectedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        c.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<SyncConflictDetected>();
    }

    [Theory]
    [InlineData(SyncConflictResolution.OwnerKeptDirect)]
    [InlineData(SyncConflictResolution.OwnerCancelledDirect)]
    [InlineData(SyncConflictResolution.AutoCancelled)]
    [InlineData(SyncConflictResolution.ManualOverride)]
    public void Resolve_with_any_non_pending_resolution_marks_resolved(SyncConflictResolution res)
    {
        var c = Detect();
        c.DequeueEvents();

        c.Resolve(res, "owner picked X");

        c.Resolution.Should().Be(res);
        c.ResolutionNotes.Should().Be("owner picked X");
        c.IsResolved.Should().BeTrue();
        c.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void Resolve_with_Pending_throws()
    {
        var c = Detect();
        var act = () => c.Resolve(SyncConflictResolution.Pending, "noop");
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.conflict.resolution");
    }

    [Fact]
    public void Resolve_blank_notes_stored_as_null()
    {
        var c = Detect();
        c.Resolve(SyncConflictResolution.OwnerKeptDirect, "   ");
        c.ResolutionNotes.Should().BeNull();
    }

    [Fact]
    public void Resolve_twice_throws()
    {
        var c = Detect();
        c.Resolve(SyncConflictResolution.OwnerKeptDirect, "first");

        var act = () => c.Resolve(SyncConflictResolution.ManualOverride, "second");

        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.conflict.already_resolved");
    }
}

[Trait("Category", "Unit")]
public sealed class SyncRunAggregateTests
{
    private static SyncRun Start() =>
        SyncRun.Start(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), Guid.NewGuid(), ChannelKind.AirBnb);

    [Fact]
    public void Start_creates_non_terminal_run_with_zero_counts()
    {
        var run = Start();

        run.IsTerminal.Should().BeFalse();
        run.EndedAt.Should().BeNull();
        run.Duration.Should().BeNull();
        run.EventsSeen.Should().Be(0);
        run.EventsNew.Should().Be(0);
        run.EventsCancelled.Should().Be(0);
        run.Error.Should().BeNull();
    }

    [Fact]
    public void Complete_records_counts_and_marks_success()
    {
        var run = Start();

        run.Complete(seen: 10, @new: 3, updated: 2, cancelled: 1);

        run.Status.Should().Be(SyncRunStatus.Success);
        run.EventsSeen.Should().Be(10);
        run.EventsNew.Should().Be(3);
        run.EventsUpdated.Should().Be(2);
        run.EventsCancelled.Should().Be(1);
        run.IsTerminal.Should().BeTrue();
        run.Duration.Should().NotBeNull();
        run.DequeueEvents().Should().BeEmpty("Complete is a quiet operation");
    }

    [Fact]
    public void Fail_records_error_and_raises_SyncRunFailed()
    {
        var run = Start();

        run.Fail("connection refused", consecutiveFailures: 3);

        run.Status.Should().Be(SyncRunStatus.Failed);
        run.Error.Should().Be("connection refused");
        run.IsTerminal.Should().BeTrue();
        var ev = run.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<SyncRunFailed>().Subject;
        ev.ConsecutiveFailures.Should().Be(3);
        ev.Error.Should().Be("connection refused");
    }

    [Fact]
    public void Complete_after_terminal_throws()
    {
        var run = Start();
        run.Complete(0, 0, 0, 0);

        var act = () => run.Complete(0, 0, 0, 0);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.run.already_terminal");
    }

    [Fact]
    public void Fail_after_terminal_throws()
    {
        var run = Start();
        run.Complete(0, 0, 0, 0);

        var act = () => run.Fail("late", 1);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "sync.run.already_terminal");
    }

    [Fact]
    public void Fail_with_blank_error_throws()
    {
        var run = Start();
        var act = () => run.Fail("  ", 1);
        act.Should().Throw<ArgumentException>();
    }
}
