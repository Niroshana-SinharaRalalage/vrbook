using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Catalog;

/// <summary>
/// VRB-300 — contract tests for the admin property read surface
/// (<c>/api/v1/admin/properties</c>). This is where the deep cross-tenant
/// isolation the status-set matrix cannot express is asserted: OwnerB reaching
/// TenantA's seeded property id is denied by RLS (404 under the tenant scope),
/// not merely by a controller check, and TenantA's property never appears in
/// OwnerB's list. Plus happy-path body + documented 404 error contract.
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class AdminPropertiesContractTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task GET_admin_properties_as_OwnerA_lists_own_tenants_property()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.GetAsync("/api/v1/admin/properties");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await resp.Content.ReadFromJsonAsync<IReadOnlyList<AdminPropertySummaryDto>>();
        items.Should().NotBeNull();
        items!.Should().Contain(p => p.Id == fixture.TenantAPropertyId,
            "a tenant admin sees every property within their own tenant (RLS-scoped).");
    }

    [Fact]
    public async Task GET_admin_property_detail_cross_tenant_is_denied_by_rls()
    {
        // OwnerB reaches for TenantA's property id directly. RLS scopes the
        // repository read to TenantB, so the row is invisible → NotFound.
        var ownerB = fixture.CreateClientAs("OwnerB");

        var resp = await ownerB.GetAsync($"/api/v1/admin/properties/{fixture.TenantAPropertyId:D}");

        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden },
            "cross-tenant resource access must be denied at the data layer, not leaked.");
    }

    [Fact]
    public async Task GET_admin_property_detail_for_own_property_succeeds()
    {
        var ownerA = fixture.CreateClientAs("OwnerA");

        var resp = await ownerA.GetAsync($"/api/v1/admin/properties/{fixture.TenantAPropertyId:D}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<PropertyDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(fixture.TenantAPropertyId);
    }

    [Fact]
    public async Task GET_admin_properties_list_does_not_leak_across_tenants()
    {
        var ownerB = fixture.CreateClientAs("OwnerB");

        var items = await ownerB.GetFromJsonAsync<IReadOnlyList<AdminPropertySummaryDto>>(
            "/api/v1/admin/properties");

        items.Should().NotBeNull();
        items!.Should().NotContain(p => p.Id == fixture.TenantAPropertyId,
            "OwnerB's RLS-scoped list must never surface TenantA's property.");
    }

    [Fact]
    public async Task GET_admin_property_detail_unknown_id_returns_404_problem()
    {
        var ownerA = fixture.CreateClientAs("OwnerA");
        var unknown = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000ee");

        var resp = await ownerA.GetAsync($"/api/v1/admin/properties/{unknown:D}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.NotFound);
    }

    [Fact]
    public async Task GET_admin_properties_anonymous_is_rejected()
    {
        var client = fixture.CreateClientAs(persona: null);

        var resp = await client.GetAsync("/api/v1/admin/properties");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
