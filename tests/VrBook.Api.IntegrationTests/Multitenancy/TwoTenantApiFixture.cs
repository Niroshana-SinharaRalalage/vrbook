using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using VrBook.Contracts.Enums;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Identity;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Auth;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Loyalty.Infrastructure.Persistence;
using VrBook.Modules.Messaging;
using VrBook.Modules.Messaging.Infrastructure.Persistence;
using VrBook.Modules.Notifications;
using VrBook.Modules.Notifications.Infrastructure.Persistence;
using VrBook.Modules.Payment;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Pricing;
using VrBook.Modules.Pricing.Infrastructure.Persistence;
using VrBook.Modules.Reviews;
using VrBook.Modules.Reviews.Infrastructure.Persistence;
using VrBook.Modules.Sync;
using VrBook.Modules.Sync.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.1 (D1) — the two-tenant fixture. Spins up a
/// Postgres testcontainer, applies every module's migrations (matching
/// production order in <c>VrBook.Migrator/Program.cs</c>), and seeds:
/// <list type="bullet">
///   <item>TenantA + TenantB (deterministic Guids for grep-ability)</item>
///   <item>OwnerA + OwnerB users with <c>tenant_memberships</c> rows</item>
///   <item>PlatformAdmin user with <c>is_platform_admin = true</c> and NO membership</item>
///   <item>One <c>Property</c> per tenant</item>
/// </list>
///
/// <para>Replaces the production <c>DevAuthHandler</c> with
/// <see cref="TwoTenantDevAuthHandler"/> via <c>ConfigureTestServices</c>,
/// so the persona cookie resolves to OwnerA / OwnerB / PlatformAdmin
/// instead of the production Owner / Guest / Admin set.</para>
///
/// <para>Seeds run under <see cref="RlsBypassScope"/> because the seed
/// crosses tenant boundaries — the M.9 policies would otherwise reject the
/// cross-tenant INSERTs.</para>
/// </summary>
public sealed class TwoTenantApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Deterministic so xUnit failure messages are grep-able.</summary>
    public static readonly Guid TenantA = Guid.Parse("aaaaaaaa-1010-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("bbbbbbbb-1010-0000-0000-000000000002");

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vrbook_m10")
        .WithUsername("vrbook")
        .WithPassword("vrbook")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();
    public Guid OwnerAUserId { get; private set; }
    public Guid OwnerBUserId { get; private set; }
    public Guid PlatformAdminUserId { get; private set; }
    public Guid TenantAPropertyId { get; private set; } = Guid.Parse("11111111-1010-0000-0000-000000000001");
    public Guid TenantBPropertyId { get; private set; } = Guid.Parse("11111111-1010-0000-0000-000000000002");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Apply every module's migrations (matches VrBook.Migrator).
        var migratorServices = new ServiceCollection();
        var migratorConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
            })
            .Build();
        migratorServices.AddIdentityDbContextForMigrator(migratorConfig);
        migratorServices.AddCatalogDbContextForMigrator(migratorConfig);
        migratorServices.AddPricingDbContextForMigrator(migratorConfig);
        migratorServices.AddBookingDbContextForMigrator(migratorConfig);
        migratorServices.AddPaymentDbContextForMigrator(migratorConfig);
        migratorServices.AddReviewsDbContextForMigrator(migratorConfig);
        migratorServices.AddSyncDbContextForMigrator(migratorConfig);
        migratorServices.AddMessagingDbContextForMigrator(migratorConfig);
        migratorServices.AddLoyaltyDbContextForMigrator(migratorConfig);
        migratorServices.AddNotificationsDbContextForMigrator(migratorConfig);
        await using (var sp = migratorServices.BuildServiceProvider())
        {
            // OPS.M.10.2 F0' DIAGNOSTIC (per docs/OPS_M_10_2_CI_ROOT_CAUSE.md §5.4).
            // F0 (commit 0ed16f9) replaced `sp.GetServices<DbContext>()` with
            // explicit per-context resolutions on the theory that the iteration
            // was last-wins. CI run 28333634153 against the F0 fix closed
            // ZERO Cluster A failures (catalog.outbox_messages still missing),
            // proving the diagnosis wrong. Static analysis says migrations
            // should apply. Need ground truth from CI.
            //
            // Console.WriteLine survives xUnit output capture and lands in
            // the GH Actions log. After this commit ships, the
            // `[OPS.M.10.2 F0']` lines tell us:
            //  - which migrations EF thinks are pending pre-Migrate,
            //  - which migrations EF thinks are applied post-Migrate,
            //  - the runtime connection string each context binds to,
            //  - whether catalog.outbox_messages actually exists at the end
            //    of migration AND immediately before the seed insert.
            async Task MigrateWithDiag<TContext>() where TContext : DbContext
            {
                var ctx = sp.GetRequiredService<TContext>();
                var name = typeof(TContext).Name;
                var conn = ctx.Database.GetDbConnection().ConnectionString;
                var pending = (await ctx.Database.GetPendingMigrationsAsync()).ToArray();
                Console.WriteLine(
                    $"[OPS.M.10.2 F0'] {name} conn={conn} pending=[{string.Join(",", pending)}]");
                await ctx.Database.MigrateAsync();
                var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToArray();
                Console.WriteLine(
                    $"[OPS.M.10.2 F0'] {name} applied=[{string.Join(",", applied)}]");
            }
            await MigrateWithDiag<IdentityDbContext>();
            await MigrateWithDiag<CatalogDbContext>();
            await MigrateWithDiag<PricingDbContext>();
            await MigrateWithDiag<BookingDbContext>();
            await MigrateWithDiag<PaymentDbContext>();
            await MigrateWithDiag<ReviewsDbContext>();
            await MigrateWithDiag<SyncDbContext>();
            await MigrateWithDiag<MessagingDbContext>();
            await MigrateWithDiag<LoyaltyDbContext>();
            await MigrateWithDiag<NotificationsDbContext>();
        }

        // OPS.M.10.2 F0' DIAGNOSTIC — verify the seed-side service provider
        // sees the same DB the migrator wrote to.
        using (var scope0 = Services.CreateScope())
        {
            var ctx0 = scope0.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var conn0 = ctx0.Database.GetDbConnection().ConnectionString;
            var applied0 = (await ctx0.Database.GetAppliedMigrationsAsync()).ToArray();
            Console.WriteLine(
                $"[OPS.M.10.2 F0'] seed-side CatalogDbContext conn={conn0} applied=[{string.Join(",", applied0)}]");
            var exists = await ctx0.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema='catalog' AND table_name='outbox_messages'")
                .FirstAsync();
            Console.WriteLine($"[OPS.M.10.2 F0'] catalog.outbox_messages exists count={exists}");
        }

        // Trigger initial WebApplicationFactory build so we can seed via DI.
        // OPS.M.9 §4.4 — seed under bypass because the seed inserts rows for
        // BOTH tenants in a single transaction; the per-statement interceptor
        // would otherwise stamp empty tenant_id and the RLS policies would
        // reject the writes.
        using var scope = Services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        await SeedAsync(scope.ServiceProvider);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task SeedAsync(IServiceProvider sp)
    {
        var idDb = sp.GetRequiredService<IdentityDbContext>();
        var catalogDb = sp.GetRequiredService<CatalogDbContext>();

        // Tenants
        var tenantA = Tenant.Create(
            "tenant-a", "Tenant A Stays", new Email("ops-a@vrbook.test"));
        ForceId(tenantA, TenantA);
        var tenantB = Tenant.Create(
            "tenant-b", "Tenant B Stays", new Email("ops-b@vrbook.test"));
        ForceId(tenantB, TenantB);
        idDb.Tenants.AddRange(tenantA, tenantB);

        // Users
        var ownerA = User.Provision(
            TwoTenantDevAuthHandler.OwnerAOid,
            new Email("owner-a@vrbook.test"),
            "Owner A",
            emailVerified: true, isOwner: true, isAdmin: true);
        var ownerB = User.Provision(
            TwoTenantDevAuthHandler.OwnerBOid,
            new Email("owner-b@vrbook.test"),
            "Owner B",
            emailVerified: true, isOwner: true, isAdmin: true);
        var platformAdmin = User.Provision(
            TwoTenantDevAuthHandler.PlatformAdminOid,
            new Email("platform-admin@vrbook.test"),
            "Platform Admin",
            emailVerified: true, isOwner: false, isAdmin: false);
        platformAdmin.GrantPlatformAdmin(actorId: Guid.NewGuid());
        idDb.Users.AddRange(ownerA, ownerB, platformAdmin);
        OwnerAUserId = ownerA.Id;
        OwnerBUserId = ownerB.Id;
        PlatformAdminUserId = platformAdmin.Id;

        // Memberships (primary so the M.2 middleware stamps the tenant claim)
        var membershipA = TenantMembership.Create(
            ownerA.Id, tenantA.Id, TenantMembership.RoleTenantAdmin, isPrimary: true);
        var membershipB = TenantMembership.Create(
            ownerB.Id, tenantB.Id, TenantMembership.RoleTenantAdmin, isPrimary: true);
        idDb.Set<TenantMembership>().AddRange(membershipA, membershipB);

        await idDb.SaveChangesAsync();

        // One property per tenant — use the real Property.Create surface
        // with minimal valid value-objects.
        var address = new Address("1 Test Lane", "Honolulu", "HI", "96801", "US", 21.3m, -157.85m);
        var capacity = new Capacity(maxGuests: 4, bedrooms: 2, bathrooms: 1, beds: 2);
        var checkIn = new CheckInWindow(
            new TimeOnly(15, 0), new TimeOnly(20, 0), new TimeOnly(11, 0));

        var propertyA = Property.Create(
            tenantId: tenantA.Id,
            ownerUserId: ownerA.Id,
            title: "Tenant A's Villa",
            description: "Seed property for OPS.M.10 Wave 2.",
            type: PropertyType.Villa,
            address: address,
            capacity: capacity,
            checkIn: checkIn,
            houseRules: Array.Empty<string>(),
            amenityIds: Array.Empty<Guid>(),
            slug: "tenant-a-villa");
        ForceId(propertyA, TenantAPropertyId);
        var propertyB = Property.Create(
            tenantId: tenantB.Id,
            ownerUserId: ownerB.Id,
            title: "Tenant B's Villa",
            description: "Seed property for OPS.M.10 Wave 2.",
            type: PropertyType.Villa,
            address: address,
            capacity: capacity,
            checkIn: checkIn,
            houseRules: Array.Empty<string>(),
            amenityIds: Array.Empty<Guid>(),
            slug: "tenant-b-villa");
        ForceId(propertyB, TenantBPropertyId);
        catalogDb.Properties.AddRange(propertyA, propertyB);
        await catalogDb.SaveChangesAsync();
    }

    /// <summary>
    /// Reflection helper — force the aggregate's Id to a deterministic value
    /// so xUnit failure messages contain the seeded id, not a random Guid.
    /// </summary>
    private static void ForceId<T>(T entity, Guid id)
    {
        var prop = typeof(T).GetProperty("Id");
        prop?.SetValue(entity, id);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["ConnectionStrings:Redis"] = string.Empty,
                ["EntraExternalId:Instance"] = string.Empty,
                ["EntraExternalId:TenantId"] = string.Empty,
                ["EntraExternalId:ClientId"] = string.Empty,
                ["DevAuth:AllowAnonymous"] = "true",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace the production DevAuthHandler with our test-only one.
            // Both register under the same SchemeName so [Authorize] still
            // routes through it. ConfigureTestServices runs AFTER the app's
            // own ConfigureServices, so the AuthenticationOptions builder's
            // handler-type registration is the LAST one to win.
            services.AddAuthentication(DevAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TwoTenantDevAuthHandler>(
                    DevAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>
    /// Create a fresh <see cref="HttpClient"/> with the persona cookie set
    /// to one of <c>"OwnerA"</c>, <c>"OwnerB"</c>, <c>"PlatformAdmin"</c>,
    /// or <c>null</c> (anonymous).
    /// </summary>
    public HttpClient CreateClientAs(string? persona)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test");
        if (!string.IsNullOrEmpty(persona))
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{DevAuthPersonas.CookieName}={persona}");
        }
        return client;
    }

    public static Guid IdFor(string tenant) => tenant switch
    {
        "A" => TenantA,
        "B" => TenantB,
        _ => Guid.Empty,
    };
}

[CollectionDefinition(nameof(TwoTenantApiCollection))]
public sealed class TwoTenantApiCollection : ICollectionFixture<TwoTenantApiFixture> { }
