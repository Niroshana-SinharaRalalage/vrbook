using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using Xunit;

namespace VrBook.Api.IntegrationTests.Contract.Platform;

/// <summary>
/// VRB-300 — contract tests for the PlatformAdmin cross-tenant surface
/// (<c>/api/v1/admin/platform/tenants</c>, TenantsPlatformController). Covers
/// the happy-path body, the documented error contract (unknown id → 404 +
/// <c>problem+json</c> <c>type</c>), input validation (missing required field →
/// 400 + validation <c>type</c>), and the role gate (a tenant owner is 403).
/// Assertions read only the standard <c>type</c>/<c>status</c> problem fields —
/// Hellang strips custom extensions on 4xx (reference_problem_details_strips_body).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class PlatformTenantsContractTests(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task GET_tenant_detail_as_PlatformAdmin_returns_the_tenant()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var resp = await client.GetAsync($"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantA:D}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<PlatformTenantDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(TwoTenantApiFixture.TenantA);
        dto.DisplayName.Should().Be("Tenant A Stays");
    }

    [Fact]
    public async Task GET_tenant_list_as_PlatformAdmin_succeeds()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        var resp = await client.GetAsync("/api/v1/admin/platform/tenants");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task GET_unknown_tenant_returns_404_problem()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");
        var unknown = Guid.Parse("dddddddd-0000-0000-0000-0000000000dd");

        var resp = await client.GetAsync($"/api/v1/admin/platform/tenants/{unknown:D}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.NotFound,
            "the error contract is the stable problem `type` URI, not the (stripped) detail fields.");
    }

    [Fact]
    public async Task POST_suspend_with_missing_reason_returns_400_validation_problem()
    {
        var client = fixture.CreateClientAs("PlatformAdmin");

        // Reason is [JsonRequired]; an empty body fails model binding → 400
        // BEFORE the handler, so the tenant is never actually suspended (the
        // shared fixture state is undisturbed).
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantA:D}/suspend",
            new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Type.Should().Be(ProblemTypes.Validation);
    }

    [Fact]
    public async Task GET_tenant_detail_as_tenant_owner_is_forbidden()
    {
        var client = fixture.CreateClientAs("OwnerA");

        var resp = await client.GetAsync($"/api/v1/admin/platform/tenants/{TwoTenantApiFixture.TenantA:D}");

        // Role gate ([Authorize(Roles="PlatformAdmin")]) rejects at the
        // authorization middleware — assert status only (the middleware's 403
        // body is not the ForbiddenException problem shape).
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the platform surface is PlatformAdmin-only; a tenant admin is rejected by the role gate.");
    }
}
