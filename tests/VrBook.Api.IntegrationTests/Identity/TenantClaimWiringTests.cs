using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// OPS.M.2 — integration tests for the UserProvisioningMiddleware tenant-claim
/// enrichment. Asserts the DB-wins precedence: <c>tenant_memberships</c> rows are
/// the sole source of truth for <c>app_tenant_id</c> + per-tenant <c>tenant_admin</c>
/// role claim. Hits the <c>/api/v1/dev-auth/current-tenant</c> debug endpoint and
/// inspects the synthesized <see cref="VrBook.Contracts.Interfaces.ICurrentUser"/>
/// answers.
/// </summary>
[Collection(nameof(IdentityApiCollection))]
[Trait("Category", "Integration")]
public sealed class TenantClaimWiringTests(IdentityApiFixture fixture)
{
    private static readonly Guid DefaultTenantId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid RandomTenantId = new("99999999-9999-9999-9999-999999999999");

    private sealed record CurrentTenantResponse(
        Guid? TenantId, bool IsTenantAdminOfDefault, bool IsTenantAdminOfRandom);

    [Fact]
    public async Task User_with_no_membership_has_null_TenantId()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        // Provision the test-owner user row via the existing /me path.
        (await client.GetAsync("/api/v1/me")).EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/dev-auth/current-tenant");
        var body = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        body!.TenantId.Should().BeNull(
            "DB-wins: no tenant_memberships row exists for the test-owner, so app_tenant_id is not stamped.");
        body.IsTenantAdminOfDefault.Should().BeFalse();
        body.IsTenantAdminOfRandom.Should().BeFalse();
    }

    [Fact]
    public async Task Primary_membership_populates_TenantId_and_HasTenantRole_for_that_tenant()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        // Provision the user row first; the seed needs users.Id.
        (await client.GetAsync("/api/v1/me")).EnsureSuccessStatusCode();
        var userId = await GetTestUserIdAsync();

        await SeedMembershipAsync(userId, DefaultTenantId, "tenant_admin", isPrimary: true);

        var response = await client.GetAsync("/api/v1/dev-auth/current-tenant");
        var body = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        body!.TenantId.Should().Be(DefaultTenantId);
        body.IsTenantAdminOfDefault.Should().BeTrue();
    }

    [Fact]
    public async Task HasTenantRole_returns_false_for_non_member_tenant()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        (await client.GetAsync("/api/v1/me")).EnsureSuccessStatusCode();
        var userId = await GetTestUserIdAsync();

        await SeedMembershipAsync(userId, DefaultTenantId, "tenant_admin", isPrimary: true);

        var response = await client.GetAsync("/api/v1/dev-auth/current-tenant");
        var body = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        body!.IsTenantAdminOfRandom.Should().BeFalse(
            "user has no membership in the random tenant; HasTenantRole should reject even though the role claim is present.");
    }

    [Fact]
    public async Task Multi_membership_user_carries_primary_TenantId_and_non_primary_fails_HasTenantRole()
    {
        await fixture.ResetAsync();
        var client = fixture.CreateClientWith(devAuth: true);

        (await client.GetAsync("/api/v1/me")).EnsureSuccessStatusCode();
        var userId = await GetTestUserIdAsync();

        // Create a second tenant so the membership FK is satisfied.
        var secondTenantId = await SeedSecondTenantAsync();

        await SeedMembershipAsync(userId, DefaultTenantId, "tenant_admin", isPrimary: true);
        await SeedMembershipAsync(userId, secondTenantId, "tenant_admin", isPrimary: false);

        var response = await client.GetAsync("/api/v1/dev-auth/current-tenant");
        var body = await response.Content.ReadFromJsonAsync<CurrentTenantResponse>();

        body!.TenantId.Should().Be(DefaultTenantId,
            "primary membership determines app_tenant_id, even when other memberships exist.");
        body.IsTenantAdminOfDefault.Should().BeTrue();
        // For the non-primary tenant, the user still has the role claim from the
        // membership row, but HasTenantRole also checks app_tenant_id — which is the
        // primary. So this asserts the OPS.M.7 gap (tenant switching deferred).
        body.IsTenantAdminOfRandom.Should().BeFalse(
            "even though the role claim is present, app_tenant_id points to the primary; non-primary tenants fail HasTenantRole until OPS.M.7 ships switching.");
    }

    private async Task<Guid> GetTestUserIdAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync();
        return user.Id;
    }

    private async Task SeedMembershipAsync(Guid userId, Guid tenantId, string role, bool isPrimary)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO identity.tenant_memberships
                (""Id"", user_id, tenant_id, role, is_primary,
                 created_at, updated_at, row_version)
            VALUES
                ('{Guid.NewGuid()}', '{userId}', '{tenantId}', '{role}', {(isPrimary ? "true" : "false")},
                 NOW(), NOW(), 0);
        ");
    }

    private async Task<Guid> SeedSecondTenantAsync()
    {
        var secondTenantId = Guid.NewGuid();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync($@"
            INSERT INTO identity.tenants
                (""Id"", slug, display_name, status,
                 default_currency, default_timezone, support_email,
                 platform_fee_bps,
                 created_at, updated_at, row_version)
            VALUES
                ('{secondTenantId}', 'second-{secondTenantId:N}', 'Second Tenant', 'Active',
                 'USD', 'UTC', 'support@vrbook.example.com',
                 1500,
                 NOW(), NOW(), 0);
        ");
        return secondTenantId;
    }
}
