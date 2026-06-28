using System.Net;
using System.Net.Http.Json;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.4 (D4) Step 2 — single-source-of-truth
/// enumeration of every authenticated endpoint × persona × tenant
/// combination. Drives the <see cref="CrossTenantEndpointMatrix"/>
/// <c>[Theory]</c> via <c>[MemberData]</c>.
///
/// <para>Adding a new endpoint to the platform = adding new yield rows
/// here. The Wave 1 <c>EndpointCoverageArchTest</c> guards that every
/// new controller action carries an explicit access decision; once the
/// Wave 2 second-half arch enforcement lights up, this matrix becomes
/// the contract.</para>
/// </summary>
public static class RouteMatrix
{
    public enum Persona
    {
        OwnerA,
        OwnerB,
        PlatformAdmin,
        Anonymous,
    }

    public enum TargetTenant
    {
        A,
        B,
        None,
    }

    /// <summary>One matrix row.</summary>
    public sealed record Cell(
        string Description,
        string Verb,
        string Route,
        Persona Persona,
        TargetTenant Target,
        HttpStatusCode[] AcceptedStatuses,
        Func<HttpRequestMessage>? BodyFactory = null)
    {
        // xUnit uses this as the test display name via [MemberData].
        public override string ToString() => Description;
    }

    private static HttpStatusCode[] Ok =>
        new[] { HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Created, HttpStatusCode.Accepted };

    private static HttpStatusCode[] Forbidden =>
        new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound };

    private static HttpStatusCode[] Unauthorized =>
        new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.Found };

    private static Func<HttpRequestMessage> JsonBody(object payload) =>
        () =>
        {
            var req = new HttpRequestMessage();
            req.Content = JsonContent.Create(payload);
            return req;
        };

    public static IEnumerable<object[]> GetAll()
    {
        foreach (var cell in Build())
        {
            yield return new object[] { cell };
        }
    }

    private static IEnumerable<Cell> Build()
    {
        // ====================================================================
        // M.7 + Identity — /api/v1/me + /api/v1/me/tenant
        // ====================================================================
        // OwnerA / OwnerB get their own tenant. PlatformAdmin has NO
        // membership (per fixture seed) so /me/tenant returns 403/404.
        // Anonymous returns 401/403.
        yield return new Cell("OwnerA_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.OwnerA, TargetTenant.None, Ok);
        yield return new Cell("OwnerB_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.OwnerB, TargetTenant.None, Ok);
        yield return new Cell("PlatformAdmin_GET_me_returns_200",
            "GET", "/api/v1/me", Persona.PlatformAdmin, TargetTenant.None, Ok);
        yield return new Cell("Anonymous_GET_me_returns_401",
            "GET", "/api/v1/me", Persona.Anonymous, TargetTenant.None, Unauthorized);

        yield return new Cell("OwnerA_GET_me_tenant_returns_tenantA",
            "GET", "/api/v1/me/tenant", Persona.OwnerA, TargetTenant.A, Ok);
        yield return new Cell("OwnerB_GET_me_tenant_returns_tenantB",
            "GET", "/api/v1/me/tenant", Persona.OwnerB, TargetTenant.B, Ok);
        yield return new Cell("PlatformAdmin_GET_me_tenant_returns_403_or_404_no_membership",
            "GET", "/api/v1/me/tenant", Persona.PlatformAdmin, TargetTenant.None,
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        yield return new Cell("Anonymous_GET_me_tenant_returns_401",
            "GET", "/api/v1/me/tenant", Persona.Anonymous, TargetTenant.None, Unauthorized);

        // ====================================================================
        // M.5 — /api/v1/admin/tenants/{tenantId}/stripe/* (TenantsAdminController)
        // ====================================================================
        // The {tenantId} route segment is gated by TenantAuthorizationBehavior
        // (M.4). Owner-of-A can call own tenant's onboard; Owner-of-A cannot
        // call tenant-B's onboard (cross-tenant 403/404).
        // PlatformAdmin can NOT use this surface (TenantsAdminController gates
        // on Roles="Owner,Admin", not PlatformAdmin) — these endpoints are
        // tenant-self-service, not platform-wide.
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var route = $"/api/v1/admin/tenants/{{tenantId}}/stripe/onboard";
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{ownerPersona}_POST_stripe_onboard_for_own_tenant_{tenant}_passes_or_returns_502",
                "POST", route, ownerPersona, tenant,
                new[] { HttpStatusCode.OK, HttpStatusCode.BadGateway, HttpStatusCode.UnprocessableEntity },
                JsonBody(new { country = "US" }));
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_onboard_for_cross_tenant_{tenant}_rejected",
                "POST", route, oppositeOwner, tenant, Forbidden,
                JsonBody(new { country = "US" }));
        }

        // account-link path
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_account_link_for_cross_tenant_{tenant}_rejected",
                "POST", "/api/v1/admin/tenants/{tenantId}/stripe/account-link",
                oppositeOwner, tenant, Forbidden);
        }

        // login-link path
        foreach (var (tenant, ownerPersona) in new[]
        {
            (TargetTenant.A, Persona.OwnerA),
            (TargetTenant.B, Persona.OwnerB),
        })
        {
            var oppositeOwner = ownerPersona == Persona.OwnerA ? Persona.OwnerB : Persona.OwnerA;
            yield return new Cell(
                $"Owner_{oppositeOwner}_POST_stripe_login_link_for_cross_tenant_{tenant}_rejected",
                "POST", "/api/v1/admin/tenants/{tenantId}/stripe/login-link",
                oppositeOwner, tenant, Forbidden);
        }

        // Anonymous on any stripe path is rejected.
        yield return new Cell("Anonymous_POST_stripe_onboard_returns_401",
            "POST", "/api/v1/admin/tenants/{tenantId}/stripe/onboard",
            Persona.Anonymous, TargetTenant.A, Unauthorized,
            JsonBody(new { country = "US" }));

        // ====================================================================
        // M.8 — /api/v1/admin/platform/tenants/* (TenantsPlatformController)
        // ====================================================================
        // [Authorize(Roles="PlatformAdmin")] — only PlatformAdmin passes.
        // OwnerA / OwnerB / Anonymous all rejected with 403/401.
        yield return new Cell("PlatformAdmin_GET_platform_tenants_list_returns_200",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.PlatformAdmin, TargetTenant.None, Ok);
        yield return new Cell("OwnerA_GET_platform_tenants_list_returns_403",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.OwnerA, TargetTenant.None, Forbidden);
        yield return new Cell("OwnerB_GET_platform_tenants_list_returns_403",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.OwnerB, TargetTenant.None, Forbidden);
        yield return new Cell("Anonymous_GET_platform_tenants_list_returns_401",
            "GET", "/api/v1/admin/platform/tenants",
            Persona.Anonymous, TargetTenant.None, Unauthorized);

        foreach (var tenant in new[] { TargetTenant.A, TargetTenant.B })
        {
            yield return new Cell(
                $"PlatformAdmin_GET_platform_tenant_detail_{tenant}_returns_200",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.PlatformAdmin, tenant, Ok);
            yield return new Cell(
                $"OwnerA_GET_platform_tenant_detail_{tenant}_returns_403",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.OwnerA, tenant, Forbidden);
            yield return new Cell(
                $"OwnerB_GET_platform_tenant_detail_{tenant}_returns_403",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.OwnerB, tenant, Forbidden);
            yield return new Cell(
                $"Anonymous_GET_platform_tenant_detail_{tenant}_returns_401",
                "GET", "/api/v1/admin/platform/tenants/{tenantId}",
                Persona.Anonymous, tenant, Unauthorized);

            // Suspend / Reactivate / SetFee — only PlatformAdmin passes
            yield return new Cell(
                $"OwnerA_POST_platform_suspend_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.OwnerA, tenant, Forbidden,
                JsonBody(new { reason = "test" }));
            yield return new Cell(
                $"OwnerB_POST_platform_suspend_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.OwnerB, tenant, Forbidden,
                JsonBody(new { reason = "test" }));
            yield return new Cell(
                $"Anonymous_POST_platform_suspend_{tenant}_returns_401",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/suspend",
                Persona.Anonymous, tenant, Unauthorized,
                JsonBody(new { reason = "test" }));

            yield return new Cell(
                $"OwnerA_POST_platform_reactivate_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/reactivate",
                Persona.OwnerA, tenant, Forbidden);
            yield return new Cell(
                $"OwnerB_POST_platform_reactivate_{tenant}_returns_403",
                "POST", "/api/v1/admin/platform/tenants/{tenantId}/reactivate",
                Persona.OwnerB, tenant, Forbidden);

            yield return new Cell(
                $"OwnerA_PUT_platform_fee_{tenant}_returns_403",
                "PUT", "/api/v1/admin/platform/tenants/{tenantId}/platform-fee",
                Persona.OwnerA, tenant, Forbidden,
                JsonBody(new { bps = 2000 }));
            yield return new Cell(
                $"OwnerB_PUT_platform_fee_{tenant}_returns_403",
                "PUT", "/api/v1/admin/platform/tenants/{tenantId}/platform-fee",
                Persona.OwnerB, tenant, Forbidden,
                JsonBody(new { bps = 2000 }));
        }
    }
}
