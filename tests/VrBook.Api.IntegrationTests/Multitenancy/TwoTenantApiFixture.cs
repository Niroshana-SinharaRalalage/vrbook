using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using VrBook.Api.IntegrationTests.Auth;
using VrBook.Contracts.Enums;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Identity;
using VrBook.Modules.Identity.Domain;
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
/// <para>Slice OPS.M.14.1 — replaces the production JwtBearer scheme with
/// <see cref="TestAuthHandler"/> via <c>ConfigureTestServices</c>. Tests
/// call <c>CreateClientAs("OwnerA"|"OwnerB"|"PlatformAdmin")</c> which sets
/// the <c>X-Test-Persona</c> header + a <c>Bearer test</c> Authorization;
/// the handler synthesizes an Entra-shaped <c>ClaimsPrincipal</c> that
/// downstream production middleware (<c>UserProvisioningMiddleware</c>,
/// <c>TenantAuthorizationBehavior</c>) treats identically to a real
/// Entra token.</para>
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
        // OPS.M.10.2 F0'' — the REAL Cluster A fix. CI's cd-staging-api.yml:78
        // exports `ConnectionStrings__Postgres=Host=localhost;Port=5432;
        // Database=vrbook;...` pointing at the service-container Postgres.
        // The WebApplicationFactory host's IConfiguration picks up that env
        // var BEFORE `ConfigureAppConfiguration` adds our in-memory
        // Testcontainer URL — but env vars rank higher than in-memory in
        // the .NET 8 WebApplicationBuilder pipeline (`WebApplication.
        // CreateBuilder()` adds `AddEnvironmentVariables()` last, overriding
        // in-memory adds). So:
        //   - the migrator-side service provider (which uses its own
        //     ConfigurationBuilder with ONLY in-memory cfg) migrated the
        //     Testcontainer DB,
        //   - the seed-side service provider (the WebApplicationFactory host)
        //     resolved a CatalogDbContext bound to the service-container DB,
        //   - the seed insert at line 256 then crashed because the service
        //     container had no migrations applied → "catalog.outbox_messages
        //     does not exist".
        //
        // IdentityApiFixture (which doesn't fail) avoided this by using a
        // SINGLE service provider for both migrate and read. TwoTenantApiFixture
        // can't easily do that because it must register migration-only
        // DbContexts via `AddXxxDbContextForMigrator` separately from the
        // host's RLS-scoped registrations. Cleanest scoped fix: clear the
        // env var at fixture init so the in-memory Testcontainer URL is the
        // only source. `DisposeAsync` restores it.
        _originalPostgresEnvVar = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);

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
            // OPS.M.10.2 F0''' shipped the real fix — connection string
            // alignment via UseSetting. Migrations apply per concrete type
            // in production-migrator order (Identity first because M.4 + M.9
            // schema deps reference identity.tenants).
            await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<PricingDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<BookingDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<PaymentDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<ReviewsDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<SyncDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<MessagingDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<LoyaltyDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
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
        // OPS.M.10.2 F0'' — restore the env var we cleared in InitializeAsync.
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _originalPostgresEnvVar);
    }

    private string? _originalPostgresEnvVar;

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

        // Slice OPS.M.13.4 — legacy six-arg Provision overload dropped;
        // fixture now uses the email-first overload + explicit grants.
        var ownerA = User.Provision(
            new Email("owner-a@vrbook.test"), "Owner A", emailVerified: true);
        ownerA.GrantOwner();
        ownerA.GrantAdmin();
        var ownerB = User.Provision(
            new Email("owner-b@vrbook.test"), "Owner B", emailVerified: true);
        ownerB.GrantOwner();
        ownerB.GrantAdmin();
        var platformAdmin = User.Provision(
            new Email("platform-admin@vrbook.test"), "Platform Admin", emailVerified: true);
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

        // Slice OPS.M.13.3 — pre-seed user_identities rows so the middleware's
        // new ProvisionOrLinkUserHandler hits Branch 1 (identity mapped)
        // instead of falling to Branch 2 (link-existing). Models the
        // post-M.13.4-backfill production state. Without these rows the
        // fixture would seed users unreachable by the new provisioning path
        // and every request would 403.
        var seedNow = DateTimeOffset.UtcNow.AddMinutes(-1);
        idDb.Set<UserIdentity>().AddRange(
            UserIdentity.Create(ownerA.Id, "entra", TwoTenantTestAuthHandler.OwnerAOid, seedNow),
            UserIdentity.Create(ownerB.Id, "entra", TwoTenantTestAuthHandler.OwnerBOid, seedNow),
            UserIdentity.Create(platformAdmin.Id, "entra", TwoTenantTestAuthHandler.PlatformAdminOid, seedNow));

        await idDb.SaveChangesAsync();

        // One property per tenant — use the real Property.Create surface
        // with minimal valid value-objects.
        //
        // OPS.M.10.2 F0-followup (architect-prescribed) — EF Core OwnsOne
        // requires a UNIQUE value-object instance per principal. Sharing
        // one Address/Capacity/CheckInWindow across propertyA and propertyB
        // makes EF re-stamp the owned entry's PropertyId FK to the LAST
        // principal visited, leaving the FIRST principal's owned slot with
        // no tracked entry. The first row then INSERTs with NULL street,
        // city, country → `23502: null value in column "street"`. Closes
        // ~58 of the 70 post-F0''' failures.
        static Address NewAddress() =>
            new("1 Test Lane", "Honolulu", "HI", "96801", "US", 21.3m, -157.85m);
        static Capacity NewCapacity() =>
            new(maxGuests: 4, bedrooms: 2, bathrooms: 1, beds: 2);
        static CheckInWindow NewCheckIn() =>
            new(new TimeOnly(15, 0), new TimeOnly(20, 0), new TimeOnly(11, 0));

        var propertyA = Property.Create(
            tenantId: tenantA.Id,
            ownerUserId: ownerA.Id,
            title: "Tenant A's Villa",
            description: "Seed property for OPS.M.10 Wave 2.",
            type: PropertyType.Villa,
            address: NewAddress(),
            capacity: NewCapacity(),
            checkIn: NewCheckIn(),
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
            address: NewAddress(),
            capacity: NewCapacity(),
            checkIn: NewCheckIn(),
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

        // OPS.M.10.2 F0''' (architect-prescribed) — the env var
        // `ConnectionStrings__Postgres` exported by cd-staging-api.yml:78
        // (`Host=localhost;Port=5432;Database=vrbook;...` → the CI
        // service-container Postgres) wins over our `ConfigureAppConfiguration`
        // in-memory adds because `WebApplication.CreateBuilder` (Program.cs:30)
        // appends `AddEnvironmentVariables` LAST in its app-config pipeline.
        //
        // F0' diagnostic CI run 28336563463 captured both connection strings:
        //   fixture (Testcontainer): Host=127.0.0.1;Port=32771;Database=vrbook_m10
        //   seed-side host:          Host=localhost;Port=5432;Database=vrbook
        // → match=False → seed-side `CatalogDbContext` connects to the
        // CI service container which has zero migrations applied →
        // `42P01: relation "catalog.outbox_messages" does not exist`.
        //
        // `UseSetting` writes to host-config (read FIRST in the
        // `WebApplication.CreateBuilder` pipeline, higher precedence than
        // `AddEnvironmentVariables`) and seeds the same key into the app
        // config root. This is the WebApplicationFactory-supported
        // override pattern for in-test connection strings in .NET 8.
        builder.UseSetting("ConnectionStrings:Postgres", ConnectionString);

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["ConnectionStrings:Redis"] = string.Empty,
                // Entra keys blank so AuthExtensions doesn't register a real
                // JwtBearer handler upstream; ConfigureTestServices below
                // registers TestAuthHandler under the same scheme name.
                ["EntraExternalId:Instance"] = string.Empty,
                ["EntraExternalId:TenantId"] = string.Empty,
                ["EntraExternalId:ClientId"] = string.Empty,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Slice OPS.M.14.1 — register TestAuthHandler under the
            // JwtBearer scheme name so the production [Authorize] decorator
            // (which routes to the default scheme = JwtBearer) hits our
            // handler. ConfigureTestServices runs AFTER the app's own
            // ConfigureServices, so this registration wins.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddScheme<TestAuthOptions, TestAuthHandler>(
                    JwtBearerDefaults.AuthenticationScheme,
                    opts => opts.Personas = TwoTenantTestAuthHandler.Personas);
        });
    }

    /// <summary>
    /// Create a fresh <see cref="HttpClient"/> stamped with the persona's
    /// <c>X-Test-Persona</c> header and a fake <c>Bearer test</c>
    /// Authorization header. Persona names: <c>"OwnerA"</c>, <c>"OwnerB"</c>,
    /// <c>"PlatformAdmin"</c>, or <c>null</c> (anonymous — no persona
    /// header, no bearer).
    /// </summary>
    public HttpClient CreateClientAs(string? persona)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        if (!string.IsNullOrEmpty(persona))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "test");
            client.DefaultRequestHeaders.Add(
                TestAuthHandler.PersonaHeader, persona);
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
