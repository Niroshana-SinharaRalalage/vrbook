using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Api.Configuration;
using VrBook.Modules.Catalog.Options;
using VrBook.Modules.Identity.Options;
using VrBook.Modules.Notifications.Options;
using VrBook.Modules.Payment.Application;
using Xunit;

namespace VrBook.Architecture.Tests.ConfigValidation;

/// <summary>
/// VRB-200 (gap G5) — fail-fast startup validation. Today a missing Entra
/// config silently boots the API with JwtBearer unwired (no token validation,
/// <c>AuthExtensions.cs:30-32</c>). These unit tests prove the validation
/// pipeline throws an <see cref="OptionsValidationException"/> naming the exact
/// <c>Section:Key</c> when a required value is missing/malformed in
/// Staging/Production, and boots-with-a-warning in Development (dev-loopback
/// carve-out).
/// </summary>
[Trait("Category", "Unit")]
public sealed class OptionsValidationTests
{
    private static ServiceProvider Build(
        IDictionary<string, string?> config,
        string environment,
        ILoggerProvider? loggerProvider = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (loggerProvider is not null)
            {
                b.AddProvider(loggerProvider);
            }
        });
        services.AddValidatedConfiguration(configuration, new TestHostEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> FullyValidConfig() => new()
    {
        ["EntraExternalId:Instance"] = "https://vrbookcid.ciamlogin.com",
        ["EntraExternalId:TenantId"] = "11111111-2222-3333-4444-555555555555",
        ["EntraExternalId:ClientId"] = "api-client-id",
        ["Stripe:SecretKey"] = "sk_test_abc",
        ["Stripe:WebhookSecret"] = "whsec_test_abc",
        ["Refund:ServiceFeePercent"] = "10",
        ["Acs:ConnectionString"] = "endpoint=https://acs.example.com/;accesskey=xyz",
        ["Acs:SenderAddress"] = "donotreply@vrbook.example.com",
        ["Blob:AccountUrl"] = "https://stvrbookstaging.blob.core.windows.net",
    };

    [Fact]
    public void MissingEntraClientId_FailsValidation()
    {
        var config = FullyValidConfig();
        config["EntraExternalId:ClientId"] = string.Empty;
        var sp = Build(config, Environments.Staging);

        var act = () => _ = sp.GetRequiredService<IOptions<EntraExternalIdOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("EntraExternalId:ClientId");
    }

    [Fact]
    public void RefundFeeOver100_FailsValidation()
    {
        var config = FullyValidConfig();
        config["Refund:ServiceFeePercent"] = "150";
        var sp = Build(config, Environments.Staging);

        var act = () => _ = sp.GetRequiredService<IOptions<RefundOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("Refund:ServiceFeePercent");
    }

    [Fact]
    public void NonUrlBlobAccountUrl_FailsValidation()
    {
        var config = FullyValidConfig();
        config["Blob:AccountUrl"] = "not-a-url";
        var sp = Build(config, Environments.Staging);

        var act = () => _ = sp.GetRequiredService<IOptions<BlobOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("Blob:AccountUrl");
    }

    [Fact]
    public void AllRequiredPresent_Passes()
    {
        var sp = Build(FullyValidConfig(), Environments.Staging);

        sp.Invoking(p => p.GetRequiredService<IOptions<EntraExternalIdOptions>>().Value.ClientId)
            .Should().NotThrow();
        sp.Invoking(p => p.GetRequiredService<IOptions<RefundOptions>>().Value.ServiceFeePercent)
            .Should().NotThrow();
        sp.Invoking(p => p.GetRequiredService<IOptions<BlobOptions>>().Value.AccountUrl)
            .Should().NotThrow();
    }

    [Fact]
    public async Task AllRequiredPresent_Reporter_LogsConfigValidationPassed()
    {
        var recorder = new RecordingLoggerProvider();
        var sp = Build(FullyValidConfig(), Environments.Staging, recorder);
        var reporter = sp.GetServices<IHostedService>().OfType<ConfigValidationReporter>().Single();

        await reporter.StartAsync(CancellationToken.None);

        recorder.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("ConfigValidationPassed"));
    }

    [Fact]
    public async Task Staging_MissingEntra_Reporter_Throws()
    {
        var config = FullyValidConfig();
        config["EntraExternalId:ClientId"] = string.Empty;
        var sp = Build(config, Environments.Staging);
        var reporter = sp.GetServices<IHostedService>().OfType<ConfigValidationReporter>().Single();

        var act = async () => await reporter.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OptionsValidationException>();
    }

    [Fact]
    public async Task Dev_MissingEntra_WarnsButBoots()
    {
        var recorder = new RecordingLoggerProvider();
        var config = FullyValidConfig();
        config["EntraExternalId:Instance"] = string.Empty;
        config["EntraExternalId:TenantId"] = string.Empty;
        config["EntraExternalId:ClientId"] = string.Empty;
        var sp = Build(config, Environments.Development, recorder);
        var reporter = sp.GetServices<IHostedService>().OfType<ConfigValidationReporter>().Single();

        // Missing Entra in Development must NOT throw (dev-loopback carve-out) ...
        var boot = async () => await reporter.StartAsync(CancellationToken.None);
        await boot.Should().NotThrowAsync();

        // ... but it MUST log a single explicit Warning naming the unvalidated section.
        recorder.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("EntraExternalId"));
    }
}
