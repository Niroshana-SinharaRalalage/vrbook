using System.Reflection;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.14.2 — locks the DevAuth retirement. The DevAuth handler,
/// controller, config keys, and env-var wiring are all deleted; these facts
/// stop them from being re-added silently.
///
/// <para>Historical context: pre-M.14, DevAuth existed to unblock local
/// dev + pre-Entra staging walks. Both use cases now flow through real
/// Entra sign-in. Every month DevAuth stayed alive was another month
/// where a stray <c>DevAuth__AllowAnonymous=true</c> flip on the staging
/// container app reintroduced the full attack surface
/// (<c>SetPersonaEmailCommand</c> rewrote any user's email by oid — an
/// account-takeover primitive).</para>
///
/// <para>Migration snapshot files (<c>.Designer.cs</c> + the original
/// migration <c>.cs</c> files) are exempt from the source-substring facts
/// because EF migration history is immutable — modifying them corrupts
/// the __EFMigrationsHistory chain. Substring matches inside those files
/// are historical facts, not live surface.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM14_NoDevAuthInProductionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read source files.");
        return dir!.FullName;
    }

    private static readonly Assembly IdentityAssembly =
        typeof(VrBook.Modules.Identity.Infrastructure.Auth.HttpCurrentUser).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(VrBook.Api.Guests.GuestTenantResolver).Assembly;

    [Fact]
    public void No_production_assembly_defines_DevAuthHandler_type()
    {
        var offenders = new[] { IdentityAssembly, ApiAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "DevAuthHandler")
            .ToList();
        offenders.Should().BeEmpty(
            because: "the DevAuth handler was deleted in OPS.M.14.2; a re-introduction reopens the synthetic-principal surface.");
    }

    [Fact]
    public void No_production_assembly_defines_DevAuthPersonas_type()
    {
        var offenders = new[] { IdentityAssembly, ApiAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "DevAuthPersonas" || t.Name == "DevAuthPersona")
            .ToList();
        offenders.Should().BeEmpty(
            because: "DevAuthPersonas + DevAuthPersona enum were deleted in OPS.M.14.2 alongside the handler.");
    }

    [Fact]
    public void No_production_assembly_defines_DevAuthOptions_type()
    {
        var offenders = new[] { IdentityAssembly, ApiAssembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Name == "DevAuthOptions")
            .ToList();
        offenders.Should().BeEmpty(
            because: "the AuthenticationSchemeOptions subclass was deleted with the handler.");
    }

    [Fact]
    public void AuthExtensions_source_contains_no_DevAuth_substring()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            "DevAuth",
            because: "AuthExtensions.cs must not mention DevAuth after M.14.2 — every string reference is a re-introduction risk.");
    }

    [Fact]
    public void IdentityController_source_contains_no_DevAuthController_class()
    {
        var path = Path.Combine(RepoRoot(), "src/VrBook.Api/Controllers/IdentityController.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            "class DevAuthController",
            because: "the DevAuthController class was deleted in M.14.2; a rewrite would reopen the 7 dev-bridge endpoints.");
    }

    [Fact]
    public void Program_source_contains_no_DevAuth_substring()
    {
        var path = Path.Combine(RepoRoot(), "src/VrBook.Api/Program.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            "DevAuth",
            because: "Program.cs's auth-wiring comment mentioned DevAuth pre-M.14; the comment was rewritten to describe Entra-only wiring.");
    }

    [Fact]
    public void MainBicep_source_contains_no_DevAuth_substring()
    {
        var path = Path.Combine(RepoRoot(), "infra/main.bicep");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            "DevAuth",
            because: "the Bicep-defined DevAuth__AllowAnonymous env var was removed in M.14.2; a stray re-add would reintroduce the runtime knob.");
    }

    [Fact]
    public void EnvExample_source_contains_no_DevAuth_substring()
    {
        var path = Path.Combine(RepoRoot(), ".env.example");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().NotContain(
            "DevAuth",
            because: ".env.example must not document a config key that no longer exists — developers copying the sample would land a phantom DevAuth flag.");
    }
}
