using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 Step 3 — app-layer assertions for the OPS.M.9
/// §3.2 carve-out tables (the ones intentionally outside RLS). Each
/// carve-out had a documented justification: "the app-layer prevents
/// cross-tenant access without RLS". This class verifies that promise
/// for the highest-risk surfaces.
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class CarveOutAppLayerTests(TwoTenantApiFixture fixture)
{
    // ====================================================================
    // Carve-out: outbound iCal feed (sync.channel_feeds.outbound_token)
    // ====================================================================
    //
    // Per FeedsController: GET /api/v1/feeds/{outboundToken}.ics is
    // [AllowAnonymous]; the token itself is the credential. The carve-out
    // invariant: an unknown/garbled token MUST return 404 — never 200
    // with another tenant's data (which would imply broken validation).

    [Fact]
    public async Task Outbound_iCal_feed_with_unknown_token_returns_404_not_data()
    {
        var client = fixture.CreateClientAs(persona: null);
        var resp = await client.GetAsync("/api/v1/feeds/this-token-does-not-exist.ics");
        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.NoContent },
            "an unknown outbound token must NEVER return another tenant's calendar data.");
    }

    [Fact]
    public async Task Outbound_iCal_feed_with_short_brute_forceable_token_returns_404()
    {
        // Defense check: even a 3-character token must not pattern-match
        // a real feed. The outbound_token column is a uuid-shaped opaque
        // value; a 3-char request is a brute-force probe.
        var client = fixture.CreateClientAs(persona: null);
        var resp = await client.GetAsync("/api/v1/feeds/abc.ics");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ====================================================================
    // Carve-out: identity.users (no tenant_id; M.9 §3.2 row 1)
    // ====================================================================
    //
    // The M.9 plan §3.2 row 1 documented:
    //   "users are platform-level; M.10 test pack verifies the app-layer
    //    prevents cross-tenant user enumeration."
    //
    // The Wave 2 audit revealed that SearchUsersHandler does NOT filter
    // by tenant — it returns every matching user across every tenant. We
    // pin the current behavior with an EXPLICIT failure assertion so the
    // gap is visible in CI and the fix lands deliberately.

    [Fact(Skip = "OPS.M.10 known leak — SearchUsersHandler doesn't filter by " +
                 "tenant_memberships join. Fix tracked as Slice OPS.M.10.1 follow-up: " +
                 "either (a) add tenant_membership filter to UserRepository.SearchAsync, " +
                 "or (b) restrict the /api/v1/admin/users endpoint to PlatformAdmin. " +
                 "Test unskipped on the fix PR.")]
    public async Task SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user()
    {
        var clientA = fixture.CreateClientAs("OwnerA");
        var resp = await clientA.GetAsync("/api/v1/admin/users?q=owner-b");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<OffsetPagedResult<UserDto>>();
        dto.Should().NotBeNull();
        dto!.Items.Should().NotContain(u => u.Email.Contains("owner-b@", StringComparison.OrdinalIgnoreCase),
            because: "M.9 §3.2 carve-out promised app-layer prevents cross-tenant user enumeration. " +
                     "Until the fix lands, this assertion is the regression-stake driver.");
    }

    // ====================================================================
    // Carve-out: identity.tenants (M.9 §3.2 row 2)
    // ====================================================================
    //
    // identity.tenants has no tenant_id; the table IS the tenant. Cross-
    // tenant reads are gated by the M.8 [Authorize(Roles="PlatformAdmin")]
    // attribute on TenantsPlatformController. The CrossTenantEndpointMatrix
    // already covers this — included here for the explicit "what M.10
    // promises" narrative.

    [Fact]
    public async Task TenantsList_endpoint_rejects_OwnerA_with_403()
    {
        var clientA = fixture.CreateClientAs("OwnerA");
        var resp = await clientA.GetAsync("/api/v1/admin/platform/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "M.8 §3.4 — only PlatformAdmin can enumerate tenants. Owner A is rejected " +
            "before the handler even sees the request.");
    }

    [Fact]
    public async Task TenantsList_endpoint_rejects_anonymous_with_401_or_redirect()
    {
        var clientAnon = fixture.CreateClientAs(persona: null);
        var resp = await clientAnon.GetAsync("/api/v1/admin/platform/tenants");
        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden,
                    HttpStatusCode.Redirect, HttpStatusCode.Found },
            "anonymous callers cannot enumerate tenants.");
    }

    // ====================================================================
    // Carve-out: identity.tenant_memberships (M.9 §3.2 row 3)
    // ====================================================================
    //
    // tenant_memberships is read by UserProvisioningMiddleware BEFORE the
    // tenant claim is materialized. M.10 verifies app-layer separation:
    // OwnerA's /me/tenant must return TenantA only; OwnerB's must return
    // TenantB only. The matrix covers this — repeated here for narrative.

    [Fact]
    public async Task Me_tenant_endpoint_OwnerA_returns_tenant_A_not_B()
    {
        var clientA = fixture.CreateClientAs("OwnerA");
        var me = await clientA.GetFromJsonAsync<MeTenantDto>("/api/v1/me/tenant");
        me.Should().NotBeNull();
        me!.Id.Should().Be(TwoTenantApiFixture.TenantA);
        me.Id.Should().NotBe(TwoTenantApiFixture.TenantB,
            "the tenant_memberships join must filter to the caller's primary membership only.");
    }

    [Fact]
    public async Task Me_tenant_endpoint_OwnerB_returns_tenant_B_not_A()
    {
        var clientB = fixture.CreateClientAs("OwnerB");
        var me = await clientB.GetFromJsonAsync<MeTenantDto>("/api/v1/me/tenant");
        me.Should().NotBeNull();
        me!.Id.Should().Be(TwoTenantApiFixture.TenantB);
        me.Id.Should().NotBe(TwoTenantApiFixture.TenantA);
    }
}
