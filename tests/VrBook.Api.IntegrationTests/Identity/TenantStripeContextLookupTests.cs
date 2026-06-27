using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.5 Step 4 RED — pins the
/// <see cref="ITenantStripeContextLookup"/> contract per
/// `docs/OPS_M_5_PLAN.md` §3.4 (D4) + §9 Step 4.
///
/// <para>The lookup feeds Payment handlers that need to route a booking's
/// PaymentIntent to the right Connect account with the right platform fee.
/// Wrong field plumbing here silently corrupts the audit trail (charges
/// land on the wrong account / wrong fee) and is invisible until a tenant
/// complains. Pin the shape from day one.</para>
///
/// <para>Reuses <see cref="TenantIdRolloutFixture"/> — same Postgres
/// testcontainer + migration runner pattern as OPS.M.3 Step 7 + OPS.M.5 Step 1.</para>
/// </summary>
[Collection(nameof(TenantIdRolloutCollection))]
public sealed class TenantStripeContextLookupTests
{
    private readonly TenantIdRolloutFixture _fixture;

    public TenantStripeContextLookupTests(TenantIdRolloutFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Returns_null_when_tenant_absent()
    {
        await using var ctx = NewIdentityDb();
        var lookup = NewLookup(ctx);

        var result = await lookup.GetAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_expected_record_shape_for_existing_tenant()
    {
        await using var ctx = NewIdentityDb();
        var tenant = await SeedTenantAsync(ctx,
            slug: $"acme-{Guid.NewGuid():N}",
            stripeAccountId: "acct_seedtest1",
            platformFeeBps: 1500,
            currency: "USD");
        var lookup = NewLookup(ctx);

        var result = await lookup.GetAsync(tenant.Id);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenant.Id);
        result.StripeAccountId.Should().Be("acct_seedtest1");
        result.PlatformFeeBps.Should().Be(1500);
        result.DefaultCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task Reads_PlatformFeeBps_from_tenants_table_not_default()
    {
        await using var ctx = NewIdentityDb();
        var tenant = await SeedTenantAsync(ctx,
            slug: $"acme-{Guid.NewGuid():N}",
            stripeAccountId: "acct_override",
            platformFeeBps: 2000,   // overrides the 1500 default
            currency: "USD");
        var lookup = NewLookup(ctx);

        var result = await lookup.GetAsync(tenant.Id);

        result!.PlatformFeeBps.Should().Be(2000,
            "the lookup must read the per-tenant override, not the default.");
    }

    [Fact]
    public async Task Reads_StripeAccountId_null_when_unassigned()
    {
        await using var ctx = NewIdentityDb();
        var tenant = await SeedTenantAsync(ctx,
            slug: $"acme-{Guid.NewGuid():N}",
            stripeAccountId: null,   // tenant hasn't onboarded Stripe yet
            platformFeeBps: 1500,
            currency: "EUR");
        var lookup = NewLookup(ctx);

        var result = await lookup.GetAsync(tenant.Id);

        result!.StripeAccountId.Should().BeNull(
            "tenants pre-Stripe-onboarding surface null so the caller throws connect_account_missing.");
        result.DefaultCurrency.Should().Be("EUR");
    }

    private IdentityDbContext NewIdentityDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(_fixture.ConnectionString));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IdentityDbContext>();
    }

    private static ITenantStripeContextLookup NewLookup(IdentityDbContext db)
    {
        // Reach into the Identity module to find the concrete implementation by
        // reflection — the type is internal in the module. Step 4 GREEN ships the
        // implementation; until then this throws and the tests Red.
        var asm = typeof(IdentityDbContext).Assembly;
        var type = asm.GetType("VrBook.Modules.Identity.Infrastructure.TenantStripeContextLookup")
            ?? throw new InvalidOperationException(
                "TenantStripeContextLookup not found. Wire in Step 4 GREEN.");
        return (ITenantStripeContextLookup)Activator.CreateInstance(type, db)!;
    }

    private static async Task<Tenant> SeedTenantAsync(
        IdentityDbContext db, string slug, string? stripeAccountId, int platformFeeBps, string currency)
    {
        var tenant = Tenant.Create(slug, $"Display-{slug}", new Email("ops@example.com"));
        if (platformFeeBps != 1500)
        {
            tenant.SetPlatformFeeBps(platformFeeBps);
        }
        if (stripeAccountId is not null)
        {
            tenant.SetStripeAccount(stripeAccountId);
        }
        // Use the SQL-level UPDATE for the columns the aggregate doesn't expose
        // a public setter for (DefaultCurrency is set at create-time only in
        // production). Tests need to override it.
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        if (currency != tenant.DefaultCurrency)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE identity.tenants SET default_currency = {currency} WHERE ""Id"" = {tenant.Id}");
        }
        return tenant;
    }
}
