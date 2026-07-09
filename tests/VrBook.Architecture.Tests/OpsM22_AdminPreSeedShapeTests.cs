using FluentAssertions;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.22.1 — RED-then-GREEN arch tests pinning the shape of the
/// admin pre-seed pipeline per <c>docs/OPS_M_22_ADMIN_PRESEED_PLAN.md</c>
/// §1.
///
/// <para><b>RED phase:</b> committed intentionally failing on 2026-07-08.
/// The 5 facts in this file cover the five M.22 deliverables that flip
/// GREEN across sub-commits M.22.2 → M.22.4:</para>
///
/// <list type="bullet">
///   <item>M.22.2 flips <see cref="SeedAdminUserCommand_type_exists"/>
///   and <see cref="AdminAccountNotProvisionedException_type_exists_and_maps_to_401"/>.</item>
///   <item>M.22.3 flips <see cref="HttpCurrentUser_exposes_EntraFlow_string_property"/>.</item>
///   <item>M.22.4 flips <see cref="UserProvisioningMiddleware_source_references_admin_gate_markers"/>
///   and <see cref="Users_pre_seeded_at_column_is_declared_in_a_migration"/>.</item>
/// </list>
///
/// <para>Pattern matches M.15.1 + M.13.3 — reflection-only shape assertions
/// that COMPILE while types are missing (null checks) but FAIL loud until
/// the corresponding subsequent commit lands. Each failure is expected
/// until its owner sub-commit ships; a green run before M.22.4 lands
/// means someone bypassed the RED-GREEN discipline.</para>
///
/// <para>Expected failure count at land time (2026-07-08, M.22.1
/// commit): <b>5 of 5 facts</b>. Anything less means the invariants
/// aren't pinned tightly enough.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM22_AdminPreSeedShapeTests
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

    /// <summary>
    /// M.22.2 deliverable — the operator-facing seed command type.
    /// GREEN when the command class is added to
    /// <c>VrBook.Modules.Identity.Application.Users.Commands</c>.
    /// </summary>
    [Fact]
    public void SeedAdminUserCommand_type_exists()
    {
        var identityAssembly = typeof(UserProvisioningMiddleware).Assembly;
        var t = identityAssembly.GetType(
            "VrBook.Modules.Identity.Application.Users.Commands.SeedAdminUserCommand");
        t.Should().NotBeNull(
            because: "M.22.2 owns the operator-facing pre-seed command; " +
                     "PlatformAdmin calls it via POST /api/v1/admin/platform/users/seed to " +
                     "create an identity.users row BEFORE the admin's first sign-in. If this " +
                     "type goes missing, the entire admin-preseed slice regresses to the " +
                     "lazy-provisioning shim.");
    }

    /// <summary>
    /// M.22.2 deliverable — the exception the admin-gate throws when a
    /// valid Entra admin-flow token arrives for an email that has no
    /// pre-seeded row. Must map to 401 (not 403) — token is valid, the
    /// account just isn't provisioned. Problem type
    /// <c>admin_account_not_provisioned</c>.
    /// </summary>
    [Fact]
    public void AdminAccountNotProvisionedException_type_exists_and_maps_to_401()
    {
        // The exception lives in the Domain assembly alongside its M.12
        // sibling AdminSocialIdpRejectedException (per the M.12 pattern —
        // both are domain-signal exceptions the API middleware layer maps
        // to specific status codes). Namespace-flexible probe searches all
        // three candidate namespaces so an intra-project relocation still
        // satisfies the invariant.
        var candidates = new[]
        {
            typeof(VrBook.Domain.Common.DomainException).Assembly,
            typeof(UserProvisioningMiddleware).Assembly,
        };
        var exceptionType = candidates
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "AdminAccountNotProvisionedException");
        exceptionType.Should().NotBeNull(
            because: "M.22.4 middleware throws this on unknown-email + admin-flow tokens; " +
                     "it must exist as a named type so the ProblemDetails mapper can route it " +
                     "to 401 with problem type admin_account_not_provisioned per plan §3/§6.");
        exceptionType!.IsAbstract.Should().BeFalse();
        exceptionType.IsSealed.Should().BeTrue(
            because: "domain-signal exceptions are sealed — a sub-type would be caught by " +
                     "the specific mapper AND the generic Exception fallback, doubling the " +
                     "response body.");

        var problemTypesAssembly = typeof(VrBook.Contracts.Common.ProblemTypes).Assembly;
        var problemTypesType = problemTypesAssembly.GetType("VrBook.Contracts.Common.ProblemTypes");
        problemTypesType.Should().NotBeNull();
        var field = problemTypesType!.GetField(
            "AdminAccountNotProvisioned",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        field.Should().NotBeNull(
            because: "M.22.4 needs a stable ProblemTypes.AdminAccountNotProvisioned constant " +
                     "so the SPA rejection page (M.22.7) can switch on the problem type URI. " +
                     "Renaming the constant silently breaks the client-side routing — same " +
                     "trap as ProblemTypes.AdminSocialIdpRejected.");
    }

    /// <summary>
    /// M.22.3 deliverable — the flow-marker claim reader on
    /// <see cref="HttpCurrentUser"/>. Returns the admin/guest flow the
    /// current token was minted for so the middleware can gate lazy
    /// provisioning.
    /// </summary>
    [Fact]
    public void HttpCurrentUser_exposes_EntraFlow_string_property()
    {
        var t = typeof(HttpCurrentUser);
        var prop = t.GetProperty(
            "EntraFlow",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop.Should().NotBeNull(
            because: "M.22.3 adds this accessor so UserProvisioningMiddleware can distinguish " +
                     "admin-flow tokens from guest-flow tokens without duplicating the " +
                     "config-key + claim-name plumbing. Reads tfp/acr/custom claim per plan §3.");
        prop!.PropertyType.Should().Be(typeof(string),
            because: "the raw claim value is a string; null when the token has no flow marker.");
        prop.CanWrite.Should().BeFalse(
            because: "claim accessors are read-only — the token is the source of truth.");
    }

    /// <summary>
    /// M.22.4 deliverable — the middleware admin-gate logic. Source
    /// must reference the load-bearing markers: the flow accessor
    /// (<c>EntraFlow</c>) and the pre-seed column
    /// (<c>pre_seeded_at</c>). A refactor that silently drops either
    /// marker breaks the invariant.
    /// </summary>
    [Fact]
    public void UserProvisioningMiddleware_source_references_admin_gate_markers()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain("EntraFlow",
            because: "M.22.4 gates lazy provisioning on the flow marker. If the middleware " +
                     "stops reading EntraFlow, it will treat admin-flow tokens the same as " +
                     "guest-flow tokens and lazy-provision unknown-email admin sign-ins.");
        text.Should().Contain("pre_seeded_at",
            because: "M.22.4 links the incoming oid to the pre-seeded row IFF pre_seeded_at " +
                     "is not null (email-hit path). If the middleware stops reading the " +
                     "column, admin-flow email-hit paths silently link any Entra token to " +
                     "any user row — a real auth-boundary regression.");
    }

    /// <summary>
    /// M.22.4 deliverable — the schema half of the pre-seed shape.
    /// A migration file must add the nullable <c>pre_seeded_at</c>
    /// column to <c>identity.users</c>. Column name is authoritative
    /// per plan §3 — changes require a new arch fact.
    /// </summary>
    [Fact]
    public void Users_pre_seeded_at_column_is_declared_in_a_migration()
    {
        var migrationsDir = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations");
        Directory.Exists(migrationsDir).Should().BeTrue(migrationsDir);

        var migrationSources = Directory
            .EnumerateFiles(migrationsDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(p => !p.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        migrationSources.Should().Contain(
            src => src.Contains("pre_seeded_at", StringComparison.Ordinal),
            because: "M.22.4 adds the pre_seeded_at timestamp column to identity.users so " +
                     "the middleware can distinguish 'operator vouched' from 'random signup' " +
                     "per plan §3. Missing column = the middleware admin-gate can't gate on " +
                     "the correct signal; renaming the column requires updating this fact.");
    }
}
