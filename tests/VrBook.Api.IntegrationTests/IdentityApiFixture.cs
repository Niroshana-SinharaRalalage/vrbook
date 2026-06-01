using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Api.IntegrationTests;

/// <summary>
/// Postgres container + WebApplicationFactory&lt;Program&gt; fixture. xUnit drives the
/// lifecycle; one shared fixture per collection keeps the container warm across tests.
/// </summary>
public sealed class IdentityApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vrbook_test")
        .WithUsername("vrbook")
        .WithPassword("vrbook")
        .Build();

    /// <summary>Set via individual tests before <see cref="CreateClient()"/> to flip auth modes.</summary>
    public bool DevAuthEnabled { get; set; } = true;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Trigger initial WebApplicationFactory build + apply migration.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = string.Empty, // skip Redis in tests
                // Blank out all three Entra keys so AuthExtensions skips JwtBearer
                // registration entirely (otherwise it would try to fetch the OIDC
                // discovery document over the network during test startup).
                ["EntraExternalId:Instance"] = string.Empty,
                ["EntraExternalId:TenantId"] = string.Empty,
                ["EntraExternalId:ClientId"] = string.Empty,
                ["DevAuth:AllowAnonymous"] = DevAuthEnabled ? "true" : "false",
                ["DevAuth:FakeOid"] = "test-owner-aaaa",
                ["DevAuth:FakeEmail"] = "owner@vrbook.test",
                ["DevAuth:FakeDisplayName"] = "Test Owner",
                ["DevAuth:IsOwner"] = "true",
                ["DevAuth:IsAdmin"] = "true",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            });
        });
    }

    /// <summary>Reset the database between tests by truncating the user + audit tables.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE identity.users, identity.audit_log CASCADE;");
    }

    public HttpClient CreateClientWith(bool devAuth)
    {
        DevAuthEnabled = devAuth;
        var client = CreateClient();
        if (!devAuth)
        {
            // No-op — leaving authorization header off so we get the unauthenticated path.
            client.DefaultRequestHeaders.Authorization = null;
        }
        else
        {
            // DevAuth ignores the bearer entirely, but we still set one so any header logging
            // shows a non-empty value.
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "dev");
        }
        return client;
    }
}

[CollectionDefinition(nameof(IdentityApiCollection))]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityApiFixture> { }
