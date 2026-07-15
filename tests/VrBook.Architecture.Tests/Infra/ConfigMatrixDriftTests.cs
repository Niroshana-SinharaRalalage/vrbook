using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests.Infra;

/// <summary>
/// VRB-202 — keeps <c>docs/ops/CONFIG-MATRIX.md</c> from drifting out of sync with
/// the code. Every key in <c>appsettings.json</c> and every Container App
/// <c>secretRef</c> in <c>infra/main.bicep</c> must have a row in the matrix, so a
/// new key/secret can't land without being documented ("the developer will figure
/// it out" is a defect). Doc-only edits don't trigger CI, so this runs on any code
/// change that touches config files.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ConfigMatrixDriftTests
{
    // Framework/infra config sections that are intentionally NOT tracked in the
    // per-key matrix (they are logging plumbing, not app/deploy configuration).
    private static readonly string[] IgnoredRoots = { "Serilog", "Logging" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "the test must run from inside the repo to read config files.");
        return dir!.FullName;
    }

    private static string MatrixText() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "docs", "ops", "CONFIG-MATRIX.md"));

    private static List<string> AppsettingsKeys()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "src", "VrBook.Api", "appsettings.json"));
        using var doc = JsonDocument.Parse(json);
        var keys = new List<string>();
        Walk(doc.RootElement, string.Empty, keys);
        return keys
            .Where(k => !IgnoredRoots.Any(r => k == r || k.StartsWith(r + ":", StringComparison.Ordinal)))
            .ToList();
    }

    // Emits one colon-joined path per leaf (scalar or array); arrays are treated as a
    // single leaf keyed by their property name (e.g. Cors:AllowedOrigins), matching how
    // the matrix documents them.
    private static void Walk(JsonElement element, string prefix, List<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var path = prefix.Length == 0 ? prop.Name : $"{prefix}:{prop.Name}";
                Walk(prop.Value, path, keys);
            }
        }
        else if (prefix.Length > 0)
        {
            keys.Add(prefix);
        }
    }

    private static List<string> BicepSecretRefs()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.bicep"));
        return Regex.Matches(text, @"secretRef:\s*['""]([^'""]+)['""]")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void EveryAppsettingsKey_HasMatrixRow()
    {
        var matrix = MatrixText();
        var missing = AppsettingsKeys()
            .Where(key => !matrix.Contains($"`{key}`", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            because: "every appsettings.json key must be documented as a row in CONFIG-MATRIX.md " +
                     $"(VRB-202 drift check). Undocumented keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryBicepSecretRef_HasMatrixRow()
    {
        var matrix = MatrixText();
        var missing = BicepSecretRefs()
            .Where(name => !matrix.Contains($"`{name}`", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            because: "every infra/main.bicep secretRef must be documented as a row in CONFIG-MATRIX.md " +
                     $"(VRB-202 drift check). Undocumented secrets: {string.Join(", ", missing)}");
    }
}
