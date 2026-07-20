using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Settings;

/// <summary>
/// ADR-0020 Tier-B DONE evidence for VRB-212 — real-Postgres round-trips through the
/// production auth middleware + RLS (<see cref="TwoTenantApiFixture"/>): the property-
/// settings edit is audited (<c>settings.property.update</c>), the activation gate is
/// enforced (a non-payment-ready tenant — the fixture seeds PendingOnboarding — cannot
/// publish), the gate is surfaced (<c>CanActivate</c>/<c>ActivationBlockedReason</c>), and
/// cross-tenant edits are rejected.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class PropertyActivationRoundTripTests(TwoTenantApiFixture fixture)
{
    private static string Detail(Guid id) => $"/api/v1/admin/properties/{id}";

    // Map a fetched PropertyDto back to the UpdatePropertyRequest shape (same Address type
    // round-trips), overriding only title/isActive.
    private static object ToUpdateBody(PropertyDto p, string? title = null, bool? isActive = null) => new
    {
        title = title ?? p.Title,
        description = p.Description,
        address = p.Address,
        maxGuests = p.MaxGuests,
        bedrooms = p.Bedrooms,
        bathrooms = p.Bathrooms,
        beds = p.Beds,
        checkinFrom = p.CheckinFrom,
        checkinTo = p.CheckinTo,
        checkoutBy = p.CheckoutBy,
        houseRules = p.HouseRules,
        amenityIds = p.Amenities.Select(a => a.Id),
        reviewsEnabled = p.ReviewsEnabled,
        dynamicPricingEnabled = p.DynamicPricingEnabled,
        messagingEnabled = p.MessagingEnabled,
        isActive = isActive ?? p.IsActive,
        turnoverHours = p.TurnoverHours,
    };

    [Fact]
    public async Task VRB212_Edit_persists_and_audits()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var id = fixture.TenantAPropertyId;

        var p = (await client.GetFromJsonAsync<PropertyDto>(Detail(id)))!;
        var newTitle = $"Edited {Guid.NewGuid():N}".Substring(0, 20);

        try
        {
            var put = await client.PutAsJsonAsync(Detail(id), ToUpdateBody(p, title: newTitle, isActive: false));
            put.StatusCode.Should().Be(HttpStatusCode.OK);

            var got = (await client.GetFromJsonAsync<PropertyDto>(Detail(id)))!;
            got.Title.Should().Be(newTitle);

            var changes = await client.GetFromJsonAsync<List<SettingsChangeDto>>(
                "/api/v1/admin/settings/changes?section=property");
            changes!.Should().Contain(c => c.Action == "settings.property.update");
        }
        finally
        {
            // Restore the shared property so this mutation doesn't bleed into other
            // TwoTenantApiCollection tests (order-independence).
            await client.PutAsJsonAsync(Detail(id), ToUpdateBody(p));
        }
    }

    [Fact]
    public async Task VRB212_ActivationGate_blocks_publish_when_tenant_not_payment_ready()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var id = fixture.TenantAPropertyId;

        var p = (await client.GetFromJsonAsync<PropertyDto>(Detail(id)))!;

        try
        {
            // Read-surface: the gate is exposed for the UI (fixture tenant is PendingOnboarding).
            p.CanActivate.Should().BeFalse();
            p.ActivationBlockedReason.Should().NotBeNullOrWhiteSpace();

            // Ensure inactive, then attempt to publish → the false→true transition is blocked.
            await client.PutAsJsonAsync(Detail(id), ToUpdateBody(p, isActive: false));
            var publish = await client.PutAsJsonAsync(Detail(id), ToUpdateBody(p, isActive: true));

            publish.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
                "a property cannot go live while its tenant isn't Stripe-ready (property.tenant_not_payment_ready).");
        }
        finally
        {
            await client.PutAsJsonAsync(Detail(id), ToUpdateBody(p)); // restore shared property state
        }
    }

    [Fact]
    public async Task VRB212_CrossTenant_edit_is_rejected()
    {
        var ownerA = fixture.CreateClientAs("OwnerA");
        var id = fixture.TenantAPropertyId;
        var p = (await ownerA.GetFromJsonAsync<PropertyDto>(Detail(id)))!;

        var ownerB = fixture.CreateClientAs("OwnerB");
        var resp = await ownerB.PutAsJsonAsync(Detail(id), ToUpdateBody(p, title: "hijack"));

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "a tenant admin must not edit another tenant's property (RLS + tenant-auth).");
    }
}
