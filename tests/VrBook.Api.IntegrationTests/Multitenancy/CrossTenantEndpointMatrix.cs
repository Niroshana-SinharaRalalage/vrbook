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

        var resolvedRoute = cell.Route.Replace(
            "{tenantId}",
            TwoTenantApiFixture.IdFor(cell.Target switch
            {
                RouteMatrix.TargetTenant.A => "A",
                RouteMatrix.TargetTenant.B => "B",
                _ => "A", // route still resolves even for non-tenant-scoped endpoints
            }).ToString("D"));

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
