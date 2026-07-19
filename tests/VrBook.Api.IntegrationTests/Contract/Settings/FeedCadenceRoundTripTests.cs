using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Settings;

/// <summary>
/// ADR-0020 Tier-B DONE evidence for VRB-214's tenant-scoped audited cadence write —
/// a real-Postgres round-trip through the production auth middleware + RLS: create a
/// feed → PUT cadence → GET reflects → <c>settings.availability.set-cadence</c> audit
/// row via VRB-211's <c>/changes</c>; plus a cross-tenant rejection.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class FeedCadenceRoundTripTests(TwoTenantApiFixture fixture)
{
    private async Task<Guid> CreateFeedAsync(HttpClient client, string channel)
    {
        var create = await client.PostAsJsonAsync(
            "/api/v1/admin/channel-feeds",
            new
            {
                propertyId = fixture.TenantAPropertyId,
                channel,
                inboundUrl = $"https://example.com/{channel}.ics",
                pollIntervalMinutes = 30,
            });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var feed = await create.Content.ReadFromJsonAsync<ChannelFeedDto>();
        return feed!.Id;
    }

    [Fact]
    public async Task VRB214_TenantAdmin_SetCadence_persists_and_audits()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var feedId = await CreateFeedAsync(client, "Other");

        var put = await client.PutAsJsonAsync(
            $"/api/v1/admin/settings/availability/feeds/{feedId}/cadence", new { pollIntervalMinutes = 45 });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await put.Content.ReadFromJsonAsync<ChannelFeedDto>();
        updated!.PollIntervalMinutes.Should().Be(45);

        // GET reflects the write
        var feeds = await client.GetFromJsonAsync<List<ChannelFeedDto>>(
            "/api/v1/admin/settings/availability/feeds");
        feeds!.Should().Contain(f => f.Id == feedId && f.PollIntervalMinutes == 45);

        // VRB-211 — the audit row is surfaced by the /changes projection
        var changes = await client.GetFromJsonAsync<List<SettingsChangeDto>>(
            "/api/v1/admin/settings/changes?section=availability");
        changes!.Should().Contain(c => c.Action == "settings.availability.set-cadence");
    }

    [Fact]
    public async Task VRB214_OutOfRange_cadence_is_rejected()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var feedId = await CreateFeedAsync(client, "Vrbo");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/admin/settings/availability/feeds/{feedId}/cadence", new { pollIntervalMinutes = 5 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "cadence must be 15–1440 min; 5 is below the floor.");
    }

    [Fact]
    public async Task VRB214_CrossTenant_SetCadence_is_rejected()
    {
        var ownerA = fixture.CreateClientAs("OwnerA");
        var feedId = await CreateFeedAsync(ownerA, "BookingCom");

        // OwnerB (a different tenant admin) must not mutate Tenant A's feed.
        var ownerB = fixture.CreateClientAs("OwnerB");
        var resp = await ownerB.PutAsJsonAsync(
            $"/api/v1/admin/settings/availability/feeds/{feedId}/cadence", new { pollIntervalMinutes = 60 });

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "RLS + tenant-auth hide another tenant's feed from a cross-tenant write.");
    }
}
