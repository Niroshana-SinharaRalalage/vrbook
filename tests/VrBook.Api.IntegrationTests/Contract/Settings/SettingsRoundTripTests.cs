using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Settings;

/// <summary>
/// ADR-0020 Tier-B DONE evidence for the settings backend — real-Postgres Testcontainers
/// round-trips through the production auth middleware + RLS (via <see cref="TwoTenantApiFixture"/>):
/// PUT → GET-reflects → audit row surfaced by VRB-211's <c>/changes</c> projection. Covers
/// VRB-216 (platform tiers), VRB-215 (per-property model + cross-tenant rejection), and
/// VRB-211 (the changes projection) in one pass.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class SettingsRoundTripTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task VRB216_PlatformAdmin_SetTiers_persists_and_audits()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var put = await client.PutAsJsonAsync(
            "/api/v1/admin/platform/settings/cancellation-tiers",
            new { firstTierDays = 10, secondTierDays = 3, middleTierRefundPct = 40, finalCutoffHours = 36, upgradePricePct = 12 });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await put.Content.ReadFromJsonAsync<GlobalCancellationTiersDto>();
        dto!.FirstTierDays.Should().Be(10);
        dto.UpgradePricePct.Should().Be(12);

        // GET reflects the write (persistence through the real stack)
        var got = await client.GetFromJsonAsync<GlobalCancellationTiersDto>(
            "/api/v1/admin/platform/settings/cancellation-tiers");
        got!.FirstTierDays.Should().Be(10);
        got.SecondTierDays.Should().Be(3);
        got.MiddleTierRefundPct.Should().Be(40);
        got.FinalCutoffHours.Should().Be(36);
        got.UpgradePricePct.Should().Be(12);
        got.Version.Should().BeGreaterThan(0, "each edit stamps a new monotonic version");

        // VRB-211 — the audit row is surfaced by the /changes projection
        var changes = await client.GetFromJsonAsync<List<SettingsChangeDto>>(
            "/api/v1/admin/settings/changes?section=platform");
        changes!.Should().Contain(c => c.Action == "settings.platform.set-tiers");

        // Restore the seed defaults so this shared-collection mutation doesn't bleed into
        // other tests that read the tiers (order-independence; the version stays monotonic).
        await client.PutAsJsonAsync(
            "/api/v1/admin/platform/settings/cancellation-tiers",
            new { firstTierDays = 7, secondTierDays = 2, middleTierRefundPct = 50, finalCutoffHours = 48, upgradePricePct = 8 });
    }

    [Fact]
    public async Task VRB215_TenantAdmin_SetPropertyModel_persists_and_audits()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var propertyId = fixture.TenantAPropertyId;

        var put = await client.PutAsJsonAsync(
            $"/api/v1/admin/settings/cancellation/{propertyId}", new { model = "RefundableUpgrade" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await put.Content.ReadFromJsonAsync<PropertyCancellationSettingsDto>();
        dto!.Model.Should().Be(CancellationModel.RefundableUpgrade);

        // GET reflects the write
        var got = await client.GetFromJsonAsync<PropertyCancellationSettingsDto>(
            $"/api/v1/admin/settings/cancellation/{propertyId}");
        got!.Model.Should().Be(CancellationModel.RefundableUpgrade);
        got.ResolvedTiers.Should().NotBeNull("the platform tiers are echoed for the guest preview");

        // VRB-211 — the audit row is surfaced by the /changes projection
        var changes = await client.GetFromJsonAsync<List<SettingsChangeDto>>(
            "/api/v1/admin/settings/changes?section=cancellation");
        changes!.Should().Contain(c => c.Action == "settings.cancellation.set-model");

        // restore for other tests in the shared collection
        await client.PutAsJsonAsync(
            $"/api/v1/admin/settings/cancellation/{propertyId}", new { model = "Tiered" });
    }

    [Fact]
    public async Task VRB215_CrossTenant_SetOtherTenantsProperty_is_rejected()
    {
        // OwnerB tries to set the cancellation model on Tenant A's property — the real
        // TenantAuthorizationBehavior + RLS must reject it (never a 200).
        var client = fixture.CreateClientAs("OwnerB");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/admin/settings/cancellation/{fixture.TenantAPropertyId}", new { model = "RefundableUpgrade" });

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "a tenant admin must not mutate another tenant's property settings (RLS + tenant-auth).");
    }
}
