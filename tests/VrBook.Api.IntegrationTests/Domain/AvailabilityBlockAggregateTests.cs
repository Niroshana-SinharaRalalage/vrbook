using FluentAssertions;
using VrBook.Modules.Booking.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Slice 3 + OPS.M.3 — unit tests for the AvailabilityBlock aggregate invariants.
/// Run in the Category=Unit step of CI; no Docker required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AvailabilityBlockAggregateTests
{
    private static readonly Guid DefaultTenant = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnyPropertyId = Guid.NewGuid();
    private static readonly DateOnly Day1 = new(2026, 7, 10);
    private static readonly DateOnly Day3 = new(2026, 7, 12);

    [Fact]
    public void Create_succeeds_with_valid_range_and_no_reason()
    {
        var block = AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day3, reason: null);

        block.PropertyId.Should().Be(AnyPropertyId);
        block.StartDate.Should().Be(Day1);
        block.EndDate.Should().Be(Day3);
        block.Reason.Should().BeNull();
        block.TenantId.Should().Be(DefaultTenant,
            "OPS.M.3c flipped TenantId to non-nullable; factory must supply it.");
    }

    [Fact]
    public void Create_trims_reason_and_treats_whitespace_as_null()
    {
        AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day3, "  maintenance ").Reason.Should().Be("maintenance");
        AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day3, "   ").Reason.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_empty_property_id()
    {
        var act = () => AvailabilityBlock.Create(DefaultTenant, Guid.Empty, Day1, Day3, null);
        act.Should().Throw<ArgumentException>().WithParameterName("propertyId");
    }

    [Fact]
    public void Create_rejects_empty_tenant_id()
    {
        var act = () => AvailabilityBlock.Create(Guid.Empty, AnyPropertyId, Day1, Day3, null);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Create_rejects_end_equal_to_start()
    {
        var act = () => AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day1, null);
        act.Should().Throw<ArgumentException>().WithParameterName("endDate");
    }

    [Fact]
    public void Create_rejects_end_before_start()
    {
        var act = () => AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day3, Day1, null);
        act.Should().Throw<ArgumentException>().WithParameterName("endDate");
    }

    [Fact]
    public void Create_rejects_reason_over_200_chars()
    {
        var act = () => AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day3, new string('x', 201));
        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Overlaps_half_open_semantics()
    {
        var b = AvailabilityBlock.Create(DefaultTenant, AnyPropertyId, Day1, Day3, null);

        b.Overlaps(Day1, Day3).Should().BeTrue("identical range overlaps");
        b.Overlaps(new(2026, 7, 9), new(2026, 7, 11)).Should().BeTrue("left straddle overlaps");
        b.Overlaps(new(2026, 7, 11), new(2026, 7, 14)).Should().BeTrue("right straddle overlaps");
        b.Overlaps(new(2026, 7, 5), Day1).Should().BeFalse("touch on start does not overlap (half-open)");
        b.Overlaps(Day3, new(2026, 7, 20)).Should().BeFalse("touch on end does not overlap (half-open)");
    }
}
