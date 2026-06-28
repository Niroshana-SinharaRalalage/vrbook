using System.Net;
using FluentAssertions;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.6 (D6) Step 5 — verifies the OPS.M.9
/// <c>IRlsBypassDbContextFactory</c> bypass works AT THE WIRE for every
/// call site enumerated in OPS.M.9 §7. PlatformAdmin can read across
/// tenants; non-admins cannot reach the same endpoint.
///
/// <para>The §9 plan added a per-call-site log assertion via Serilog
/// InMemorySink. We omit the in-memory sink wiring in this commit (the
/// production-shape log line is already emitted by
/// <c>RlsBypassDbContextFactory.CreateForBypassAsync</c>; verifying its
/// content needs a sink override that races with the fixture's host
/// build). Wave 2 ships the behavioral assertion (the call succeeds /
/// fails per persona); the log-content assertion is a Slice OPS.M.10.1
/// follow-up.</para>
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class PlatformAdminBypassFactPack(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task ListPlatformTenants_PlatformAdmin_can_read_BOTH_tenants_through_bypass()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");
        var resp = await client.GetAsync("/api/v1/admin/platform/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "M.9 §4.6 D6 — the bypass factory opens app.is_platform_admin=true, " +
            "letting the handler enumerate identity.tenants across tenant boundaries.");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant A Stays",
            "the bypass-read returns TenantA's row.");
        body.Should().Contain("Tenant B Stays",
            "the bypass-read also returns TenantB's row — confirming cross-tenant.");
    }

    [Fact]
    public async Task GetPlatformTenant_PlatformAdmin_can_read_tenantA_detail_through_bypass()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");
        var resp = await client.GetAsync(
            $"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantA:D}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "M.9 §4.6 D6 — GetPlatformTenantHandler opens the bypass; identity.tenants " +
            "is a §3.2 carve-out so the read passes either way, but the bypass log emits.");
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant A Stays");
    }

    [Fact]
    public async Task GetPlatformTenant_PlatformAdmin_can_read_tenantB_detail_through_bypass()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");
        var resp = await client.GetAsync(
            $"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantB:D}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("Tenant B Stays");
    }

    [Fact]
    public async Task ListPlatformTenants_OwnerA_is_403_even_though_their_own_tenant_data_exists()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var resp = await client.GetAsync("/api/v1/admin/platform/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the role gate ([Authorize(Roles=PlatformAdmin)]) fires BEFORE the handler — " +
            "OwnerA never reaches the bypass code path.");
    }

    [Fact]
    public async Task GetPlatformTenant_OwnerA_for_own_tenant_is_403_no_bypass_for_non_admin()
    {
        var client = fixture.CreateClientAs("OwnerA");
        var resp = await client.GetAsync(
            $"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantA:D}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "OwnerA can read THEIR own tenant via /me/tenant but NOT via the platform " +
            "endpoint. The latter exists only for cross-tenant operator surface.");
    }
}
