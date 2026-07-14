using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace VrBook.Api.IntegrationTests.ConfigValidation;

/// <summary>
/// VRB-200 (gap G5) — proves the REAL host (<c>Program</c>) fails to start in
/// Staging/Production when a required config section is missing, rather than
/// silently booting with JwtBearer unwired. Boots the composition root with a
/// syntactically-valid (never-connected) connection string so the fail-fast
/// config reporter is what aborts startup — no Postgres/Docker needed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StartupFailFastTests
{
    private sealed class ConfiguredFactory : WebApplicationFactory<Program>
    {
        private readonly string _environment;
        private readonly IDictionary<string, string?> _overrides;

        public ConfiguredFactory(string environment, IDictionary<string, string?> overrides)
        {
            _environment = environment;
            _overrides = overrides;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            builder.UseSetting("ConnectionStrings:Postgres", _overrides["ConnectionStrings:Postgres"]);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(_overrides));
        }
    }

    private static Dictionary<string, string?> BaseConfig() => new()
    {
        // Well-formed connection string — registration binds it but nothing connects
        // before the fail-fast reporter runs.
        ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=vrbook;Username=x;Password=y",
        ["ConnectionStrings:Redis"] = string.Empty,
        ["Cors:AllowedOrigins:0"] = "https://web.example.com",
    };

    [Fact]
    public void Staging_WithoutEntra_HostFailsToStart()
    {
        var config = BaseConfig();
        config["EntraExternalId:Instance"] = string.Empty;
        config["EntraExternalId:TenantId"] = string.Empty;
        config["EntraExternalId:ClientId"] = string.Empty;
        using var factory = new ConfiguredFactory(Environments.Staging, config);

        // Accessing the server forces host build + start; the fail-fast reporter
        // throws before the API can serve a single request.
        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("EntraExternalId");
    }

    [Fact]
    public void Staging_WithEntra_HostStarts()
    {
        var config = BaseConfig();
        config["EntraExternalId:Instance"] = "https://vrbookcid.ciamlogin.com";
        config["EntraExternalId:TenantId"] = "11111111-2222-3333-4444-555555555555";
        config["EntraExternalId:ClientId"] = "api-client-id";
        using var factory = new ConfiguredFactory(Environments.Staging, config);

        // Valid required config → the host builds + starts without a config
        // validation failure (DB is never contacted at startup).
        var act = () => factory.CreateClient();

        act.Should().NotThrow<OptionsValidationException>();
    }
}
