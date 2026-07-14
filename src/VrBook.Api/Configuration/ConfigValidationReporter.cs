using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace VrBook.Api.Configuration;

/// <summary>
/// VRB-200 — hosted service that (1) force-resolves every validated options
/// section at startup so an invalid value throws an
/// <see cref="OptionsValidationException"/> deterministically (independent of the
/// framework startup-validator ordering) and (2) emits a single structured
/// <c>ConfigValidationPassed</c> log listing the validated section names (never
/// values) once they are all good. In Development it additionally logs one
/// explicit Warning naming the sections skipped by the dev-loopback carve-out.
/// </summary>
public sealed class ConfigValidationReporter : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfigValidationReporter> _logger;
    private readonly IReadOnlyList<(Type OptionsType, string Section)> _validated;
    private readonly IReadOnlyList<string> _skipped;

    public ConfigValidationReporter(
        IServiceProvider serviceProvider,
        ILogger<ConfigValidationReporter> logger,
        IReadOnlyList<(Type OptionsType, string Section)> validated,
        IReadOnlyList<string> skipped)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _validated = validated;
        _skipped = skipped;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var (optionsType, _) in _validated)
        {
            var accessorType = typeof(IOptions<>).MakeGenericType(optionsType);
            var accessor = _serviceProvider.GetRequiredService(accessorType);
            var valueProperty = accessorType.GetProperty("Value")!;
            try
            {
                // Reading .Value triggers the registered IValidateOptions<T>.
                _ = valueProperty.GetValue(accessor);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // Unwrap so the caller sees the real OptionsValidationException.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        if (_skipped.Count > 0)
        {
            _logger.LogWarning(
                "Config sections NOT validated (Development dev-loopback carve-out): {SkippedSections}. " +
                "These MUST be present and valid in Staging/Production or the host will fail to start.",
                string.Join(", ", _skipped));
        }

        _logger.LogInformation(
            "ConfigValidationPassed. Validated sections: {ValidatedSections}.",
            string.Join(", ", _validated.Select(v => v.Section)));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
