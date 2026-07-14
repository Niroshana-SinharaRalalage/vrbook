using System.Text.RegularExpressions;
using FluentAssertions;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.4 (D4) Step 2 — the master cross-tenant
/// matrix. One <c>[Theory]</c> row per <see cref="RouteMatrix.Cell"/>;
/// each row substitutes the route's <c>{tenantId}</c> placeholder with
/// the cell's <see cref="RouteMatrix.TargetTenant"/>, sends the request
/// as the cell's <see cref="RouteMatrix.Persona"/>, and asserts the
/// response status is in the cell's <c>AcceptedStatuses</c> set.
///
/// <para>The <c>AcceptedStatuses</c> set rather than a single value
/// accommodates: 401 vs 403 vs 302 for anonymous (depends on auth
/// scheme registration order); 200 vs 502 vs 422 for Stripe-onboard
/// (production gateway returns 502 when the test sandbox isn't reachable;
/// 422 when the tenant has no Stripe configured). The matrix codifies
/// the multi-tenancy property — which statuses are acceptable for an
/// authorized request — not the business outcome.</para>
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class CrossTenantEndpointMatrix(TwoTenantApiFixture fixture)
{
    /// <summary>A well-formed placeholder for non-tenant/non-property route
    /// parameters — valid against a <c>:guid</c> constraint so the route binds,
    /// while naming no real resource (the request 401/403s before it matters).</summary>
    private const string PlaceholderId = "ffffffff-0000-0000-0000-0000000000ff";

    private static readonly Regex PlaceholderRouteParam = new(@"\{[^}]+\}", RegexOptions.Compiled);


    [Theory]
    [MemberData(nameof(RouteMatrix.GetAll), MemberType = typeof(RouteMatrix))]
    public async Task Endpoint_persona_cross_tenant_status_within_accepted_set(RouteMatrix.Cell cell)
    {
        var personaCookie = cell.Persona switch
        {
            RouteMatrix.Persona.OwnerA => "OwnerA",
            RouteMatrix.Persona.OwnerB => "OwnerB",
            RouteMatrix.Persona.PlatformAdmin => "PlatformAdmin",
            _ => null,
        };
        var client = fixture.CreateClientAs(personaCookie);

        // {tenantId} → the target tenant (the load-bearing substitution: a
        // cross-tenant cell puts tenant-A's id in the path and sends as OwnerB).
        // {propertyId} → the target tenant's seeded property, so property-scoped
        // isolation cells hit a real cross-tenant resource.
        var targetTenantId = TwoTenantApiFixture.IdFor(cell.Target switch
        {
            RouteMatrix.TargetTenant.A => "A",
            RouteMatrix.TargetTenant.B => "B",
            _ => "A", // route still resolves even for non-tenant-scoped endpoints
        });
        var targetPropertyId = cell.Target == RouteMatrix.TargetTenant.B
            ? fixture.TenantBPropertyId
            : fixture.TenantAPropertyId;

        var resolvedRoute = cell.Route
            .Replace("{tenantId}", targetTenantId.ToString("D"))
            .Replace("{propertyId}", targetPropertyId.ToString("D"));

        // VRB-300 — any OTHER route parameter ({id}, {bookingId}, {ruleId},
        // {blockId}, {holdId}, {imageId}, {key}, …) names a resource the request
        // never reaches for the assertions the matrix makes: the auth challenge
        // (401) and the tenant/role gate (403) both fire before the handler
        // loads it. Fill each with a deterministic, well-formed placeholder so
        // the route binds (including the :guid constraint) and endpoint
        // selection succeeds — routing to the [Authorize] endpoint is what makes
        // the 401/403 assertion meaningful. Deeper per-resource isolation
        // (OwnerB at TenantA's booking/feed → 403/404) is asserted in the
        // per-module Contract/* tests where the exact resource is seeded.
        resolvedRoute = PlaceholderRouteParam.Replace(resolvedRoute, PlaceholderId);

        HttpRequestMessage request;
        if (cell.BodyFactory is not null)
        {
            request = cell.BodyFactory();
            request.Method = new HttpMethod(cell.Verb);
            request.RequestUri = new Uri(resolvedRoute, UriKind.Relative);
        }
        else
        {
            request = new HttpRequestMessage(new HttpMethod(cell.Verb), resolvedRoute);
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().BeOneOf(cell.AcceptedStatuses,
            because: $"{cell.Description} — expected one of [{string.Join(", ", cell.AcceptedStatuses)}] " +
                     $"but got {(int)response.StatusCode} {response.StatusCode}.");
    }
}
