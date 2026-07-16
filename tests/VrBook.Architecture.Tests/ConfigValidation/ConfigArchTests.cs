using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VrBook.Api.Configuration;
using VrBook.Modules.Catalog.Options;
using VrBook.Modules.Identity.Options;
using VrBook.Modules.Notifications.Options;
using VrBook.Modules.Payment.Application;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Architecture.Tests.ConfigValidation;

/// <summary>
/// VRB-200 architecture guard. Every required options section must be wired
/// through <c>AddOptions&lt;T&gt;().Bind(section).ValidateDataAnnotations()
/// .ValidateOnStart()</c> with a companion <see cref="IValidateOptions{T}"/>
/// for cross-field rules. If a new required section is added but not validated,
/// this fails the build — closing the "silently boots degraded" hole (G5).
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConfigArchTests
{
    // The required, fail-fast-validated config sections owned by VRB-200.
    private static readonly Type[] RequiredValidatedOptions =
    {
        typeof(EntraExternalIdOptions),
        typeof(CorsOptions),        // VRB-205
        typeof(StripeOptions),
        typeof(RefundOptions),
        typeof(AcsOptions),
        typeof(BlobOptions),
    };

    private static ServiceCollection ProductionServices()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        // Production env → the environment-aware Entra carve-out is OFF, so every
        // required section is registered with its validator.
        services.AddValidatedConfiguration(configuration, new TestHostEnvironment(Environments.Production));
        return services;
    }

    [Fact]
    public void EveryRequiredOptionsClass_HasValidateOptionsRegistered()
    {
        var services = ProductionServices();

        foreach (var optionsType in RequiredValidatedOptions)
        {
            var validatorService = typeof(IValidateOptions<>).MakeGenericType(optionsType);
            services.Any(d => d.ServiceType == validatorService).Should().BeTrue(
                because: $"{optionsType.Name} is a required config section and must have an " +
                         "IValidateOptions<T> registered (ValidateDataAnnotations + a cross-field validator).");
        }
    }

    [Fact]
    public void ValidateOnStart_IsWired_SoMisconfigFailsAtStartupNotFirstUse()
    {
        var services = ProductionServices();

        // ValidateOnStart() registers Microsoft.Extensions.Options' internal
        // IStartupValidator. Its presence proves at least one section fails the
        // host at startup rather than degrading silently.
        services.Any(d => d.ServiceType.FullName == "Microsoft.Extensions.Options.IStartupValidator")
            .Should().BeTrue(
                because: "required options must be validated eagerly via .ValidateOnStart(), " +
                         "so a misconfigured deploy crashes loudly instead of booting degraded (G5).");
    }

    [Fact]
    public void EveryRequiredOptionsClass_HasCrossFieldValidatorImplementation()
    {
        // Static guard independent of DI: each required section ships a concrete
        // IValidateOptions<T> so the failure message can name the exact Section:Key.
        foreach (var optionsType in RequiredValidatedOptions)
        {
            var validatorInterface = typeof(IValidateOptions<>).MakeGenericType(optionsType);
            var impls = optionsType.Assembly.GetTypes()
                .Concat(typeof(ConfigValidationExtensions).Assembly.GetTypes())
                .Where(t => t is { IsClass: true, IsAbstract: false } && validatorInterface.IsAssignableFrom(t))
                .ToList();
            impls.Should().NotBeEmpty(
                because: $"{optionsType.Name} needs a concrete IValidateOptions<{optionsType.Name}> " +
                         "so cross-field rules name the exact Section:Key that failed.");
        }
    }
}
