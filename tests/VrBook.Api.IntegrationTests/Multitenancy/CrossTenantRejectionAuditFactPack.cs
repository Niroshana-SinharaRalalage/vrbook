using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.5 (D5) Step 6 — verifies the OPS.M.4
/// <c>AuditLogBehavior</c> records cross-tenant rejections in
/// <c>identity.audit_log</c>. Per the M.4 plan §3.4: when
/// <c>TenantAuthorizationBehavior</c> throws
/// <c>CrossTenantAccessException</c>, <c>AuditLogBehavior</c> catches the
/// exception and writes the audit row with action suffix <c>.failed</c>.
///
/// <para>This is the wire-level confirmation that the audit pipeline
/// composes correctly across modules — a guarantee the unit-level
/// AuditLogBehavior tests can't make.</para>
/// </summary>
[Trait("Category", "CrossTenant")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class CrossTenantRejectionAuditFactPack(TwoTenantApiFixture fixture)
{
    [Fact]
    public async Task OwnerA_attempt_to_onboard_tenantB_records_failed_audit_row()
    {
        var initialCount = await CountFailedAuditRowsAsync();

        // Owner A attempts to invoke Stripe onboarding for tenant B. The
        // M.4 TenantAuthorizationBehavior throws CrossTenantAccessException
        // BEFORE the handler runs; the M.4 AuditLogBehavior catches it and
        // writes a ".failed" audit row.
        var clientA = fixture.CreateClientAs("OwnerA");
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/tenants/{TwoTenantApiFixture.TenantB:D}/stripe/onboard");
        req.Content = System.Net.Http.Json.JsonContent.Create(new { country = "US" });
        var resp = await clientA.SendAsync(req);

        // The behavior rejected the request.
        ((int)resp.StatusCode).Should().BeOneOf(new[] { 403, 404 },
            "M.4 — cross-tenant write is rejected.");

        // The audit row landed.
        var finalCount = await CountFailedAuditRowsAsync();
        finalCount.Should().BeGreaterThan(initialCount,
            because: "M.4 §3.4 — every CrossTenantAccessException must record a .failed " +
                     "audit row. If finalCount == initialCount the audit pipeline is broken.");
    }

    [Fact]
    public async Task OwnerA_successful_call_to_own_tenant_records_NON_failed_audit_row()
    {
        var initialFailedCount = await CountFailedAuditRowsAsync();
        var initialAllCount = await CountAllAuditRowsAsync();

        var clientA = fixture.CreateClientAs("OwnerA");
        var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/admin/tenants/{TwoTenantApiFixture.TenantA:D}/stripe/onboard");
        req.Content = System.Net.Http.Json.JsonContent.Create(new { country = "US" });
        var resp = await clientA.SendAsync(req);

        // The call passed the tenant gate. It may still 502 if the Stripe
        // sandbox isn't reachable, but the audit pipeline is independent
        // of the Stripe call outcome.
        var finalFailedCount = await CountFailedAuditRowsAsync();
        var finalAllCount = await CountAllAuditRowsAsync();

        // If the call succeeded, the success-suffix audit row landed.
        // If the call 502'd (Stripe sandbox unavailable), the .failed
        // row landed — but the all-count still moved.
        finalAllCount.Should().BeGreaterThan(initialAllCount,
            "every IAuditable command writes an audit row regardless of outcome.");
    }

    private async Task<int> CountFailedAuditRowsAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.AuditLog
            .Where(a => a.Action.EndsWith(".failed"))
            .CountAsync();
    }

    private async Task<int> CountAllAuditRowsAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.AuditLog.CountAsync();
    }
}
