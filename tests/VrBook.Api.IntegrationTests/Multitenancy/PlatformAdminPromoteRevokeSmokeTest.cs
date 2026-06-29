using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.8 (D8) Step 7 — promote/revoke smoke test.
/// Verifies the OPS.M.8 manual-SQL operator runbook actually works
/// end-to-end:
/// <list type="number">
///   <item>OwnerA (no platform-admin bit) gets 403 on the platform list.</item>
///   <item>Operator SQL flips <c>is_platform_admin = true</c> on OwnerA's row.</item>
///   <item>OwnerA (now elevated) gets 200 on the same endpoint.</item>
///   <item>Operator SQL flips it back to <c>false</c>.</item>
///   <item>OwnerA gets 403 again.</item>
/// </list>
///
/// <para>This is the end-to-end proof that the OPS.M.8 promote runbook
/// (manual SQL until the PowerShell cmdlet ships) actually delivers the
/// auth state change the operator expects. If the M.8 middleware caches
/// the flag aggressively, this test catches it; the middleware is supposed
/// to re-read on every request per ADR-0014 DB-wins.</para>
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class PlatformAdminPromoteRevokeSmokeTest(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task Promote_revoke_flow_flips_OwnerA_access_to_platform_endpoints()
    {
        var clientA = fixture.CreateClientAs("OwnerA");

        // Baseline: OwnerA is rejected from the platform-admin surface.
        var pre = await clientA.GetAsync("/api/v1/admin/platform/tenants");
        pre.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "baseline — OwnerA has is_platform_admin = false (seeded).");

        // Operator runs the OPS_M_8_PROMOTE_PLATFORM_ADMIN.md SQL.
        // OPS.M.10.2 F-residual — `id` (unquoted) resolves PG-lowercase but EF
        // created the column as `"Id"` (quoted PascalCase). Use the quoted form.
        await ExecuteAsync("UPDATE identity.users SET is_platform_admin = true WHERE \"Id\" = @id;",
            new { id = fixture.OwnerAUserId });

        // OwnerA refresh: now reaches the platform endpoint.
        var clientA2 = fixture.CreateClientAs("OwnerA");
        var mid = await clientA2.GetAsync("/api/v1/admin/platform/tenants");
        mid.StatusCode.Should().Be(HttpStatusCode.OK,
            "after promote, OwnerA's request runs through the UserProvisioningMiddleware " +
            "which re-reads is_platform_admin = true and adds the PlatformAdmin role claim.");

        // Revoke.
        await ExecuteAsync("UPDATE identity.users SET is_platform_admin = false WHERE \"Id\" = @id;",
            new { id = fixture.OwnerAUserId });

        // OwnerA is rejected again.
        var clientA3 = fixture.CreateClientAs("OwnerA");
        var post = await clientA3.GetAsync("/api/v1/admin/platform/tenants");
        post.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "after revoke, the middleware re-reads is_platform_admin = false and the " +
            "PlatformAdmin role claim is no longer added.");
    }

    private async Task ExecuteAsync(string sql, object parameters)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var idProp = parameters.GetType().GetProperty("id");
        var idValue = idProp!.GetValue(parameters);
        await db.Database.ExecuteSqlRawAsync(
            sql.Replace("@id", $"'{idValue}'"));
    }
}
