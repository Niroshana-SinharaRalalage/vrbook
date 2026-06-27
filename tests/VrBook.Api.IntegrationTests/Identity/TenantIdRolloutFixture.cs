using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using VrBook.Modules.Booking;
using VrBook.Modules.Catalog;
using VrBook.Modules.Identity;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Messaging;
using VrBook.Modules.Notifications;
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Reviews;
using VrBook.Modules.Sync;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// OPS.M.3 Step 7 — fixture that spins up a fresh Postgres testcontainer, runs
/// every module's migrations the same way the production migrator does, and
/// exposes the connection string so schema-audit tests can read
/// <c>information_schema</c> directly. Mirrors the wiring in
/// <c>src/VrBook.Migrator/Program.cs</c>.
/// </summary>
public sealed class TenantIdRolloutFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vrbook_rollout_test")
        .WithUsername("vrbook")
        .WithPassword("vrbook")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
            })
            .Build();

        services.AddIdentityDbContextForMigrator(config);
        services.AddCatalogDbContextForMigrator(config);
        services.AddPricingDbContextForMigrator(config);
        services.AddBookingDbContextForMigrator(config);
        services.AddPaymentDbContextForMigrator(config);
        services.AddReviewsDbContextForMigrator(config);
        services.AddSyncDbContextForMigrator(config);
        services.AddMessagingDbContextForMigrator(config);
        services.AddLoyaltyDbContextForMigrator(config);
        services.AddNotificationsDbContextForMigrator(config);

        await using var sp = services.BuildServiceProvider();
        foreach (var ctx in sp.GetServices<DbContext>())
        {
            await ctx.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}

[CollectionDefinition(nameof(TenantIdRolloutCollection))]
public sealed class TenantIdRolloutCollection : ICollectionFixture<TenantIdRolloutFixture> { }
