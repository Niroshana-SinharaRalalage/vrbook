using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests.Infra;

/// <summary>
/// VRB-201 (gap G6) — secret seed-parity. Every Key Vault secret referenced by a
/// Container App <c>secretRef</c> in <c>infra/main.bicep</c> must have a guaranteed
/// seed line in <c>infra/scripts/10-store-secrets.ps1</c> (or a documented alternate
/// producer), so a clean/first deploy cannot fail atomically on an unresolved
/// <c>secretRef</c>. Historically <c>stripe-publishable-key</c> + <c>acs-sender-address</c>
/// were referenced but unseeded. The reverse guard catches orphan secrets seeded but
/// never referenced (e.g. <c>sendgrid-key</c>, <c>b2c-api-client-secret</c>).
/// </summary>
[Trait("Category", "Unit")]
public sealed class SeedSecretsParityTests
{
    /// <summary>Secrets referenced by Bicep but produced by something OTHER than the
    /// seed script — each entry documents its producer.</summary>
    private static readonly IReadOnlyDictionary<string, string> AlternateProducers =
        new Dictionary<string, string>
        {
            // Written by infra/modules/acs.bicep at deploy time (main.bicep line ~74 in acs.bicep).
            ["acs-connection-string"] = "infra/modules/acs.bicep",
            // VRB-311 — written by infra/main.bicep from appi.outputs.connectionString
            // (the web app dependsOn it so it exists before the secretRef binds).
            ["appinsights-connection-string"] = "infra/main.bicep",
        };

    /// <summary>Secrets seeded but intentionally NOT bound as a Container App secretRef —
    /// each has a documented non-secretRef consumer.</summary>
    private static readonly IReadOnlyDictionary<string, string> NonSecretRefSeeds =
        new Dictionary<string, string>
        {
            ["postgres-admin-password"] = "Bicep Postgres admin-password param (not a container secretRef)",
            ["e2e-guest-password"] = "nightly-playwright workflow reads via az keyvault secret show",
            ["e2e-owner-password"] = "nightly-playwright workflow reads via az keyvault secret show",
            ["e2e-platform-admin-password"] = "nightly-playwright workflow reads via az keyvault secret show",
        };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "the test must run from inside the repo to read infra files.");
        return dir!.FullName;
    }

    private static HashSet<string> BicepSecretRefNames()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.bicep"));
        return Regex.Matches(text, @"secretRef:\s*['""]([^'""]+)['""]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> SeededSecretNames()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "scripts", "10-store-secrets.ps1"));
        return Regex.Matches(text, @"Set-KvSecret\s+-Name\s+['""]([^'""]+)['""]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void EverySecretRefInBicep_HasSeedLineOrDocumentedProducer()
    {
        var referenced = BicepSecretRefNames();
        var seeded = SeededSecretNames();

        var missing = referenced
            .Where(name => !seeded.Contains(name) && !AlternateProducers.ContainsKey(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            because: "every Bicep secretRef must be seeded by 10-store-secrets.ps1 (or have a documented " +
                     "alternate producer) so a first/clean deploy cannot fail atomically on an unresolved " +
                     $"secretRef (G6). Missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void NoOrphanSeededSecrets_EveryStripeAndAcsRefIsSeeded()
    {
        // The two historically-unseeded G6 secrets must now be present.
        var seeded = SeededSecretNames();
        seeded.Should().Contain("stripe-publishable-key",
            because: "stripe-publishable-key is a Bicep secretRef and was the G6 first-deploy risk.");
        seeded.Should().Contain("acs-sender-address",
            because: "acs-sender-address is a Bicep secretRef with no other producer (G6).");
    }

    [Fact]
    public void NoOrphanSeededSecrets_EverySeededSecretIsUsed()
    {
        var referenced = BicepSecretRefNames();
        var seeded = SeededSecretNames();

        var orphans = seeded
            .Where(name => !referenced.Contains(name)
                        && !NonSecretRefSeeds.ContainsKey(name)
                        && !AlternateProducers.ContainsKey(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        orphans.Should().BeEmpty(
            because: "a secret seeded but never referenced by Bicep (and with no documented non-secretRef " +
                     $"consumer) is dead vault inventory — remove it. Orphans: {string.Join(", ", orphans)}");
    }
}
