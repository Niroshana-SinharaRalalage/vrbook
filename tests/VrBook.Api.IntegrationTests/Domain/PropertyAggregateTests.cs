using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Modules.Catalog.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A2 Property aggregate + Address + Capacity value objects.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PropertyAggregateTests
{
    private static Address AnyAddress() =>
        new("1 Main St", "Townsville", "ST", "12345", "US", 42.0m, -71.0m);

    private static Capacity AnyCapacity() => new(maxGuests: 4, bedrooms: 2, bathrooms: 1, beds: 3);

    private static CheckInWindow AnyCheckInWindow() =>
        new(new TimeOnly(15, 0), new TimeOnly(22, 0), new TimeOnly(11, 0));

    private static Property Create(
        IEnumerable<string>? houseRules = null,
        IEnumerable<Guid>? amenityIds = null,
        string title = "Cozy Cabin",
        string description = "A great place.") =>
        Property.Create(
            ownerUserId: Guid.NewGuid(),
            title: title,
            description: description,
            type: PropertyType.Cabin,
            address: AnyAddress(),
            capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            houseRules: houseRules ?? [],
            amenityIds: amenityIds ?? [],
            slug: "cozy-cabin");

    // ---- Property.Create ----

    [Fact]
    public void Create_initializes_with_inactive_state_and_raises_PropertyCreated()
    {
        var p = Create();

        p.IsActive.Should().BeFalse("owner activates explicitly after adding photos");
        p.ReviewsEnabled.Should().BeTrue();
        p.DynamicPricingEnabled.Should().BeFalse();
        p.MessagingEnabled.Should().BeTrue();
        p.Title.Should().Be("Cozy Cabin");
        p.Slug.Should().Be("cozy-cabin");
        p.RatingAvg.Should().BeNull();
        p.RatingCount.Should().Be(0);
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyCreated>();
    }

    [Fact]
    public void Create_trims_title_and_description()
    {
        var p = Create(title: "   Title   ", description: "   Desc   ");
        p.Title.Should().Be("Title");
        p.Description.Should().Be("Desc");
    }

    [Fact]
    public void Create_with_blank_title_throws()
    {
        Action act = () => Create(title: " ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_skips_blank_house_rules_and_preserves_order()
    {
        var p = Create(houseRules: ["No smoking", "", "  ", "Quiet 22:00-07:00"]);
        p.HouseRules.Should().HaveCount(2);
        p.HouseRules.Select(r => r.RuleText).Should().Equal("No smoking", "Quiet 22:00-07:00");
    }

    [Fact]
    public void Create_deduplicates_amenity_ids()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var p = Create(amenityIds: [a, b, a, b, a]);
        p.AmenityIds.Should().BeEquivalentTo([a, b]);
    }

    // ---- UpdateBasics ----

    [Fact]
    public void UpdateBasics_overwrites_fields_and_raises_PropertyUpdated()
    {
        var p = Create();
        p.DequeueEvents();

        p.UpdateBasics(
            title: "New Title",
            description: "New Desc",
            address: AnyAddress(),
            capacity: new Capacity(maxGuests: 6, bedrooms: 3, bathrooms: 2, beds: 4),
            checkIn: AnyCheckInWindow(),
            reviewsEnabled: false,
            dynamicPricingEnabled: true,
            messagingEnabled: false);

        p.Title.Should().Be("New Title");
        p.Description.Should().Be("New Desc");
        p.Capacity.MaxGuests.Should().Be(6);
        p.ReviewsEnabled.Should().BeFalse();
        p.DynamicPricingEnabled.Should().BeTrue();
        p.MessagingEnabled.Should().BeFalse();
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyUpdated>();
    }

    // ---- House rules + amenity replace ----

    [Fact]
    public void ReplaceHouseRules_replaces_collection()
    {
        var p = Create(houseRules: ["old1", "old2"]);
        p.ReplaceHouseRules(["new1", "new2", "new3"]);
        p.HouseRules.Select(r => r.RuleText).Should().Equal("new1", "new2", "new3");
    }

    [Fact]
    public void ReplaceAmenities_deduplicates()
    {
        var p = Create();
        var a = Guid.NewGuid();
        p.ReplaceAmenities([a, a, a]);
        p.AmenityIds.Should().ContainSingle().Which.Should().Be(a);
    }

    // ---- Activate / Deactivate ----

    [Fact]
    public void Activate_makes_property_active()
    {
        var p = Create();
        p.Activate();
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_clears_active_and_raises_event()
    {
        var p = Create();
        p.Activate();
        p.DequeueEvents();

        p.Deactivate("low quality");

        p.IsActive.Should().BeFalse();
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyDeactivated>();
    }

    // ---- Images ----

    [Fact]
    public void AddImage_first_image_is_primary_and_sort_zero()
    {
        var p = Create();
        p.DequeueEvents();

        var img = p.AddImage("blob/path/1.jpg", "front");

        img.IsPrimary.Should().BeTrue();
        img.SortOrder.Should().Be(0);
        p.Images.Should().HaveCount(1);
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyImageAdded>();
    }

    [Fact]
    public void AddImage_subsequent_images_not_primary_and_increment_sort()
    {
        var p = Create();
        p.AddImage("p1", null);
        var second = p.AddImage("p2", null);
        var third = p.AddImage("p3", "caption");

        second.IsPrimary.Should().BeFalse();
        second.SortOrder.Should().Be(1);
        third.SortOrder.Should().Be(2);
    }

    // ---- Address value object ----

    [Fact]
    public void Address_with_invalid_latitude_throws()
    {
        Action act = () => _ = new Address("s", "c", "st", "z", "us", latitude: 91m, longitude: 0m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Address_with_invalid_longitude_throws()
    {
        Action act = () => _ = new Address("s", "c", "st", "z", "us", latitude: 0m, longitude: 181m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Address_equality_uses_value_components()
    {
        var a1 = new Address("1 Main", "City", "ST", "12345", "US", 42m, -71m);
        var a2 = new Address("1 Main", "City", "ST", "12345", "US", 42m, -71m);
        a1.Equals(a2).Should().BeTrue();
        a1.GetHashCode().Should().Be(a2.GetHashCode());
    }

    // ---- Capacity value object ----

    [Fact]
    public void Capacity_requires_max_guests_at_least_one()
    {
        Action act = () => _ = new Capacity(maxGuests: 0, bedrooms: 1, bathrooms: 1, beds: 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Capacity_requires_at_least_one_bed()
    {
        Action act = () => _ = new Capacity(maxGuests: 2, bedrooms: 1, bathrooms: 1, beds: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- SetRating (called from Reviews on aggregate update) ----

    [Fact]
    public void SetRating_overwrites_both_fields()
    {
        var p = Create();

        p.SetRating(4.5m, 12);

        p.RatingAvg.Should().Be(4.5m);
        p.RatingCount.Should().Be(12);
    }
}
