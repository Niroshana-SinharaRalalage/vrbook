using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 Step 1 — smoke test for
/// <see cref="TwoTenantApiFixture"/>. Verifies the fixture spins up, the
/// migrations apply, the seed lands, and the persona cookie resolves to
/// the three expected principals.
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class TwoTenantApiFixtureTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task Fixture_OwnerA_persona_resolves_to_tenant_A_membership()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var me = await client.GetFromJsonAsync<MeTenantDto>("/api/v1/me/tenant");
        me.Should().NotBeNull();
        me!.Id.Should().Be(TwoTenantApiFixture.TenantA);
        me.DisplayName.Should().Be("Tenant A Stays");
    }

    [Fact]
    public async Task Fixture_OwnerB_persona_resolves_to_tenant_B_membership()
    {
        var client = fixture.CreateClientAs("OwnerB");
        var me = await client.GetFromJsonAsync<MeTenantDto>("/api/v1/me/tenant");
        me.Should().NotBeNull();
        me!.Id.Should().Be(TwoTenantApiFixture.TenantB);
        me.DisplayName.Should().Be("Tenant B Stays");
    }

    [Fact]
    public async Task Fixture_PlatformAdmin_persona_carries_isPlatformAdmin_true()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");
        var me = await client.GetFromJsonAsync<UserDto>("/api/v1/me");
        me.Should().NotBeNull();
        me!.IsPlatformAdmin.Should().BeTrue(
            because: "the seed grants the PlatformAdmin user the is_platform_admin bit; " +
                     "the M.8 middleware reads the column and stamps the Role claim.");
    }

    [Fact]
    public async Task Fixture_anonymous_request_returns_401_on_authenticated_endpoint()
    {
        var client = fixture.CreateClientAs(persona: null);
        var resp = await client.GetAsync("/api/v1/me");
        resp.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden },
            "no persona cookie → DevAuth returns NoResult → the auth pipeline rejects.");
    }
}
