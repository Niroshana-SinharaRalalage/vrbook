using Microsoft.Extensions.Options;

namespace VrBook.Api.Configuration;

/// <summary>
/// Bound from configuration section <c>Cors</c> (VRB-205). The browser origins the
/// API allows for credentialed CORS. Required + fail-fast validated in
/// Staging/Production (an API that allows no origins can't serve its own SPA);
/// Development boots with the appsettings localhost default (dev-loopback carve-out,
/// like <see cref="VrBook.Modules.Identity.Options.EntraExternalIdOptions"/>).
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

/// <summary>VRB-205 — CORS origins must be present and each a valid absolute origin
/// (scheme+host, no trailing slash — browsers match the <c>Origin</c> header exactly).</summary>
internal sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        if (options.AllowedOrigins.Length == 0)
        {
            return ValidateOptionsResult.Fail(
                "Cors:AllowedOrigins must contain at least one origin (Staging/Production) — " +
                "the SPA cannot call the API otherwise.");
        }

        var invalid = options.AllowedOrigins
            .Where(o => !Uri.TryCreate(o, UriKind.Absolute, out var uri)
                        || uri.AbsolutePath != "/"
                        || o.EndsWith('/'))
            .ToList();
        if (invalid.Count > 0)
        {
            return ValidateOptionsResult.Fail(
                $"Cors:AllowedOrigins contains invalid origin(s) (must be an absolute scheme+host with no path/trailing slash): {string.Join(", ", invalid)}.");
        }

        return ValidateOptionsResult.Success;
    }
}
