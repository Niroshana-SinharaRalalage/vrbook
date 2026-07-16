using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;
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

    private static readonly Guid AnyTenantId = new("00000000-0000-0000-0000-000000000001");

    private static Property Create(
        IEnumerable<string>? houseRules = null,
        IEnumerable<Guid>? amenityIds = null,
        string title = "Cozy Cabin",
        string description = "A great place.") =>
        Property.Create(
            tenantId: AnyTenantId,
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

    // ---- Slice OPS.M.16 TurnoverHours ----

    [Fact]
    public void Create_default_TurnoverHours_is_24()
    {
        Create().TurnoverHours.Should().Be(24);
    }

    [Fact]
    public void Create_accepts_explicit_TurnoverHours()
    {
        var p = Property.Create(
            tenantId: AnyTenantId,
            ownerUserId: Guid.NewGuid(),
            title: "T",
            description: "D",
            type: PropertyType.Cabin,
            address: AnyAddress(),
            capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            houseRules: [],
            amenityIds: [],
            slug: "s",
            turnoverHours: 48);
        p.TurnoverHours.Should().Be(48);
    }

    [Fact]
    public void Create_rejects_negative_TurnoverHours()
    {
        var act = () => Property.Create(
            tenantId: AnyTenantId,
            ownerUserId: Guid.NewGuid(),
            title: "T", description: "D", type: PropertyType.Cabin,
            address: AnyAddress(), capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            houseRules: [], amenityIds: [], slug: "s",
            turnoverHours: -1);
        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*turnover_hours_out_of_range*");
    }

    [Fact]
    public void Create_rejects_TurnoverHours_over_upper_bound_168()
    {
        var act = () => Property.Create(
            tenantId: AnyTenantId,
            ownerUserId: Guid.NewGuid(),
            title: "T", description: "D", type: PropertyType.Cabin,
            address: AnyAddress(), capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            houseRules: [], amenityIds: [], slug: "s",
            turnoverHours: 169);
        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void UpdateBasics_updates_TurnoverHours()
    {
        var p = Create();
        p.DequeueEvents();

        p.UpdateBasics(
            title: "T2", description: "D2",
            address: AnyAddress(), capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            reviewsEnabled: true, dynamicPricingEnabled: false, messagingEnabled: true,
            turnoverHours: 12);

        p.TurnoverHours.Should().Be(12);
    }

    [Fact]
    public void UpdateBasics_rejects_TurnoverHours_over_upper_bound()
    {
        var p = Create();
        var act = () => p.UpdateBasics(
            title: "T2", description: "D2",
            address: AnyAddress(), capacity: AnyCapacity(),
            checkIn: AnyCheckInWindow(),
            reviewsEnabled: true, dynamicPricingEnabled: false, messagingEnabled: true,
            turnoverHours: 200);
        act.Should().Throw<BusinessRuleViolationException>();
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
#pragma warning disable CS0618 // intentional: exercises the F11.1 obsolete-bridge contract.
        p.Activate();
#pragma warning restore CS0618
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_clears_active_and_raises_event()
    {
        var p = Create();
#pragma warning disable CS0618
        p.Activate();
#pragma warning restore CS0618
        p.DequeueEvents();

        p.Deactivate("low quality");

        p.IsActive.Should().BeFalse();
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyDeactivated>();
    }

    // ---- Slice OPS.M.10.2 F11.1 — gated Activate(tenant payment-readiness) ----

    [Fact]
    public void Activate_gated_succeeds_when_tenant_is_payment_ready()
    {
        var p = Create();
        p.AddImage("blob/1.jpg", null); // VRB-101 — activation now requires ≥1 photo
        p.Activate(tenantStatus: "Active", tenantChargesEnabled: true, tenantPayoutsEnabled: true);
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Activate_gated_throws_when_property_has_no_images()
    {
        var p = Create();
        var act = () => p.Activate(tenantStatus: "Active", tenantChargesEnabled: true, tenantPayoutsEnabled: true);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "property.requires_image");
        p.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData("PendingOnboarding")]
    [InlineData("Suspended")]
    [InlineData("Closed")]
    public void Activate_gated_throws_when_tenant_status_is_not_active(string status)
    {
        var p = Create();
        var act = () => p.Activate(tenantStatus: status, tenantChargesEnabled: true, tenantPayoutsEnabled: true);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "property.tenant_not_payment_ready");
        p.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Activate_gated_throws_when_stripe_capability_not_enabled(bool charges, bool payouts)
    {
        var p = Create();
        var act = () => p.Activate(tenantStatus: "Active", tenantChargesEnabled: charges, tenantPayoutsEnabled: payouts);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "property.tenant_not_payment_ready");
        p.IsActive.Should().BeFalse();
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

    [Fact]
    public void AddImage_trims_caption()
    {
        var p = Create();
        var img = p.AddImage("p", "  Ocean view  ");
        img.Caption.Should().Be("Ocean view");
    }

    [Fact]
    public void AddImage_with_explicit_id_uses_that_id()
    {
        var p = Create();
        var id = Guid.NewGuid();
        var img = p.AddImage(id, "tenant/prop/img.jpg", null);
        img.Id.Should().Be(id);
    }

    [Fact]
    public void RemoveImage_returns_blob_path_and_raises_event()
    {
        var p = Create();
        var first = p.AddImage("blob/a.jpg", null);
        p.AddImage("blob/b.jpg", null);
        p.DequeueEvents();

        var removedPath = p.RemoveImage(first.Id);

        removedPath.Should().Be("blob/a.jpg");
        p.Images.Should().HaveCount(1);
        p.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PropertyImageRemoved>();
    }

    [Fact]
    public void RemoveImage_promotes_next_survivor_when_primary_removed()
    {
        var p = Create();
        var primary = p.AddImage("blob/a.jpg", null); // sort 0, primary
        var second = p.AddImage("blob/b.jpg", null);   // sort 1
        p.AddImage("blob/c.jpg", null);                // sort 2

        p.RemoveImage(primary.Id);

        p.Images.Single(i => i.Id == second.Id).IsPrimary.Should().BeTrue();
        p.Images.Count(i => i.IsPrimary).Should().Be(1);
    }

    [Fact]
    public void RemoveImage_of_non_primary_keeps_the_primary()
    {
        var p = Create();
        var primary = p.AddImage("blob/a.jpg", null);
        var second = p.AddImage("blob/b.jpg", null);

        p.RemoveImage(second.Id);

        p.Images.Single().Id.Should().Be(primary.Id);
        p.Images.Single().IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void RemoveImage_unknown_id_throws_not_found()
    {
        var p = Create();
        p.AddImage("blob/a.jpg", null);
        var act = () => p.RemoveImage(Guid.NewGuid());
        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void ReorderImages_persists_order_and_makes_first_primary()
    {
        var p = Create();
        var a = p.AddImage("blob/a.jpg", null); // primary
        var b = p.AddImage("blob/b.jpg", null);
        var c = p.AddImage("blob/c.jpg", null);

        p.ReorderImages([c.Id, a.Id, b.Id]);

        var byId = p.Images.ToDictionary(i => i.Id);
        byId[c.Id].SortOrder.Should().Be(0);
        byId[c.Id].IsPrimary.Should().BeTrue();
        byId[a.Id].SortOrder.Should().Be(1);
        byId[a.Id].IsPrimary.Should().BeFalse();
        byId[b.Id].SortOrder.Should().Be(2);
        byId[b.Id].IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void ReorderImages_with_mismatched_id_set_throws()
    {
        var p = Create();
        var a = p.AddImage("blob/a.jpg", null);
        p.AddImage("blob/b.jpg", null);

        var act = () => p.ReorderImages([a.Id]); // missing the second id
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "property.image_reorder_mismatch");
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
