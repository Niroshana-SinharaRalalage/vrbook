using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Domain;
using Xunit;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.16.7 — locks the shape of the turnover-aware completion
/// slice. Reflection facts guard the aggregate shape + DTO shape;
/// source-substring facts guard the sweep-predicate + overlap-predicate
/// implementation from silently regressing (e.g. a future refactor
/// reintroducing the pre-M.16 hardcoded 24h delay in
/// <c>CompletionSweepHandler</c>).
///
/// <para>The `_pre_m13_snap` schema cleanup + Housekeeping module +
/// calendar UI polish are the successor slices listed in
/// <c>docs/OPS_M_16_CLOSE_OUT.md</c>; these arch tests do NOT cover them.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM16_TurnoverAwareShapeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "the test must run from inside the repo so it can scan source files.");
        return dir!.FullName;
    }

    // ---- Reflection facts ------------------------------------------------

    [Fact]
    public void Property_aggregate_exposes_TurnoverHours_int()
    {
        var prop = typeof(Property).GetProperty(
            "TurnoverHours",
            BindingFlags.Public | BindingFlags.Instance);
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void Booking_aggregate_exposes_TurnoverHoursOverride_nullable_int_and_CompletionDueAt_nullable_DateTimeOffset()
    {
        var overrideProp = typeof(DomainBooking).GetProperty(
            "TurnoverHoursOverride",
            BindingFlags.Public | BindingFlags.Instance);
        overrideProp.Should().NotBeNull();
        overrideProp!.PropertyType.Should().Be(typeof(int?));

        var dueProp = typeof(DomainBooking).GetProperty(
            "CompletionDueAt",
            BindingFlags.Public | BindingFlags.Instance);
        dueProp.Should().NotBeNull();
        dueProp!.PropertyType.Should().Be(typeof(DateTimeOffset?));
    }

    [Fact]
    public void Booking_aggregate_exposes_CompleteManually_and_ScheduleCompletion_methods()
    {
        typeof(DomainBooking).GetMethod("CompleteManually", Type.EmptyTypes)
            .Should().NotBeNull(because: "manual completion is the OPS.M.16 admin path; deleting it removes the Complete-now button surface.");

        var schedule = typeof(DomainBooking).GetMethod(
            "ScheduleCompletion",
            new[] { typeof(int) });
        schedule.Should().NotBeNull(because: "ScheduleCompletion(int) is the domain entry point for POST /schedule-completion.");
    }

    [Fact]
    public void PropertyDto_carries_TurnoverHours()
    {
        var prop = typeof(PropertyDto).GetProperty(
            "TurnoverHours",
            BindingFlags.Public | BindingFlags.Instance);
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(int));
    }

    [Fact]
    public void BookingDto_carries_CompletionDueAt_and_TurnoverHoursOverride_and_CheckedOutAt()
    {
        var t = typeof(BookingDto);
        t.GetProperty("CheckedOutAt")!.PropertyType.Should().Be(typeof(DateTimeOffset?));
        t.GetProperty("CompletionDueAt")!.PropertyType.Should().Be(typeof(DateTimeOffset?));
        t.GetProperty("TurnoverHoursOverride")!.PropertyType.Should().Be(typeof(int?));
    }

    // ---- Source-substring facts (guard against silent regressions) --------

    [Fact]
    public void CompletionSweepHandler_reads_CompletionDueAt_not_hardcoded_24h_delay()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Booking/Application/Commands/CompletionSweepCommand.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain(
            "CompletionDueAt",
            because: "the sweep predicate must read the snapshotted due-at; reverting to CheckedOutAt <= cutoff loses the per-property + per-override semantics.");
        text.Should().NotContain(
            "TimeSpan.FromHours(24)",
            because: "the pre-M.16 hardcoded 24h delay is retired; keep this arch test as the guard.");
    }

    [Fact]
    public void BookingRepository_FindOverlaps_has_CheckedOut_conditional_branch()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/BookingRepository.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain(
            "BookingStatus.CheckedOut",
            because: "the CheckedOut turnover-day-block predicate must live in FindOverlapsAsync.");
        text.Should().Contain(
            "checkin <= b.Stay.CheckoutDate",
            because: "the inclusive-checkout predicate must survive for CheckedOut bookings; removing it reintroduces the walk's same-day overlap bug.");
    }

    [Fact]
    public void PlaceBookingHandler_FOR_UPDATE_sql_has_CheckedOut_conditional_branch()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain(
            "status = 'CheckedOut'",
            because: "the race-safe SQL primary check must mirror BookingRepository.FindOverlapsAsync's conditional-on-status predicate.");
        text.Should().Contain(
            "@p1 <= checkout_date",
            because: "the inclusive-checkout branch must survive for the SQL path.");
    }
}
