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
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Api.IntegrationTests;

/// <summary>
/// Postgres container + WebApplicationFactory&lt;Program&gt; fixture. xUnit drives the
/// lifecycle; one shared fixture per collection keeps the container warm across tests.
///
/// <para>Slice OPS.M.14.1 — auth is served by <see cref="TestAuthHandler"/>
/// registered under the JwtBearer scheme via <c>ConfigureTestServices</c>.
/// Two personas are seeded: <c>"Owner"</c> (isOwner + isAdmin true) and
/// <c>"Anonymous"</c> (no persona → 401 for authenticated routes).</para>
/// </summary>
public sealed class IdentityApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Stable Owner OID reused by tests that assert on it.</summary>
    public const string OwnerOid = "test-owner-aaaa";

    private static readonly IReadOnlyDictionary<string, TestPersona> DefaultPersonas =
        new Dictionary<string, TestPersona>
        {
            // Slice OPS.M.15.5 — Roles left null; owner authority for the
            // tenant-scoped identity API tests comes from the seed data's
            // identity.tenant_memberships row.
            ["Owner"] = new(
                Oid: OwnerOid,
                Email: "owner@vrbook.test",
                DisplayName: "Test Owner"),
        };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vrbook_test")
        .WithUsername("vrbook")
        .WithPassword("vrbook")
        .Build();

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
                // Entra keys blank so AuthExtensions doesn't wire real JwtBearer
                // (avoids startup-time OIDC discovery fetch); ConfigureTestServices
                // below registers TestAuthHandler under the same scheme name.
                ["EntraExternalId:Instance"] = string.Empty,
                ["EntraExternalId:TenantId"] = string.Empty,
                ["EntraExternalId:ClientId"] = string.Empty,
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Slice OPS.M.14.1 — see TwoTenantApiFixture for rationale.
            // ConfigureTestServices runs AFTER app ConfigureServices so this
            // handler-type replacement wins.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddScheme<TestAuthOptions, TestAuthHandler>(
                    JwtBearerDefaults.AuthenticationScheme,
                    opts => opts.Personas = DefaultPersonas);
        });
    }

    /// <summary>Reset the database between tests by truncating the user + audit tables.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // OPS.M.2 — also truncate tenant_memberships so integration tests that seed
        // memberships are repeatable (the partial unique index from OPS.M.1 would
        // otherwise reject re-seeds across test runs sharing the fixture).
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE identity.users, identity.audit_log, identity.tenant_memberships CASCADE;");
    }

    /// <summary>
    /// Create a fresh <see cref="HttpClient"/>.
    /// <para><c>authenticated=true</c> stamps <c>Authorization: Bearer test</c>
    /// + <c>X-Test-Persona: Owner</c> so <see cref="TestAuthHandler"/> resolves
    /// the Owner persona.</para>
    /// <para><c>authenticated=false</c> leaves both headers off — the auth
    /// pipeline sees no bearer and challenges with 401 for authenticated
    /// routes.</para>
    /// </summary>
    public HttpClient CreateClientWith(bool authenticated)
    {
        var client = CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", "test");
            client.DefaultRequestHeaders.Add(TestAuthHandler.PersonaHeader, "Owner");
        }
        return client;
    }
}

[CollectionDefinition(nameof(IdentityApiCollection))]
public sealed class IdentityApiCollection : ICollectionFixture<IdentityApiFixture> { }
