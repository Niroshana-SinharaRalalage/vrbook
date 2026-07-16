using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Admin;

/// <summary>
/// VRB-203 — contract tests for the PlatformAdmin-only feature-flag toggle API
/// (<c>/api/v1/admin/toggles</c>, <c>[Authorize(Roles="PlatformAdmin")]</c>). Covers the
/// role dimension (200 / 403 / 401), a set→list round-trip (override takes effect), and
/// input validation (non-global scope → 400).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class TogglesContractTests(TwoTenantApiFixture fixture)
{
    private const string ProbeKey = "Features:Test.ContractProbe";

    private static string Path(string key) => $"/api/v1/admin/toggles/{Uri.EscapeDataString(key)}";

    [Fact]
    public async Task GET_toggles_as_PlatformAdmin_returns_the_list()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var resp = await client.GetAsync("/api/v1/admin/toggles");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GET_toggles_as_tenant_owner_is_forbidden()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.GetAsync("/api/v1/admin/toggles");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "feature flags are platform-global; a tenant admin has no authority over them.");
    }

    [Fact]
    public async Task GET_toggles_anonymous_is_rejected()
    {
        var client = fixture.CreateClientAs(persona: null);

        var resp = await client.GetAsync("/api/v1/admin/toggles");

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PUT_toggle_as_PlatformAdmin_sets_the_override_and_list_reflects_it()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var put = await client.PutAsJsonAsync(
            Path(ProbeKey), new UpdateFeatureToggleRequest("global", null, true));
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await put.Content.ReadFromJsonAsync<FeatureToggleDto>();
        dto!.Key.Should().Be(ProbeKey);
        dto.Enabled.Should().BeTrue();

        var list = await client.GetFromJsonAsync<List<FeatureToggleDto>>("/api/v1/admin/toggles");
        list!.Should().Contain(f => f.Key == ProbeKey && f.Enabled);
    }

    [Fact]
    public async Task PUT_toggle_as_tenant_owner_is_forbidden()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.PutAsJsonAsync(
            Path("Features:Loyalty.Enabled"), new UpdateFeatureToggleRequest("global", null, false));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PUT_toggle_with_non_global_scope_is_a_validation_error()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var resp = await client.PutAsJsonAsync(
            Path("Features:Loyalty.Enabled"), new UpdateFeatureToggleRequest("property", Guid.NewGuid(), true));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "VRB-203 supports global flags only; a per-property scope must be rejected with a 400.");
    }
}
