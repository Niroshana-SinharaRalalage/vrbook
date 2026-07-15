using System.Net;
using FluentAssertions;
using VrBook.Api.IntegrationTests.Multitenancy;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Catalog;

/// <summary>
/// VRB-300 — contract tests for the PlatformAdmin-only amenity catalog
/// (<c>/api/v1/admin/amenities</c>, <c>[Authorize(Roles="PlatformAdmin")]</c>).
/// Asserts the role dimension the status-set matrix summarises: a platform admin
/// reads the global catalog (200), a tenant owner is refused by the role gate
/// (403), and an anonymous caller is rejected (401).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class AdminAmenitiesContractTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task GET_admin_amenities_as_PlatformAdmin_returns_the_catalog()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var resp = await client.GetAsync("/api/v1/admin/amenities");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GET_admin_amenities_as_tenant_owner_is_forbidden()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.GetAsync("/api/v1/admin/amenities");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the amenity catalog is platform-global; a tenant admin has no authority over it.");
    }

    [Fact]
    public async Task GET_admin_amenities_anonymous_is_rejected()
    {
        var client = fixture.CreateClientAs(persona: null);

        var resp = await client.GetAsync("/api/v1/admin/amenities");

        resp.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
