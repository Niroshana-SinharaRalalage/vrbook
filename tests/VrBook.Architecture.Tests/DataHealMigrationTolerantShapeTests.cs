using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.7.7 (post-mortem for the `163229d` migrator
/// failure) — data-heal migrations MUST NOT use <c>RAISE EXCEPTION</c>
/// to hard-block a deploy on end-state assumptions that CI can't
/// validate. The `163229d` catalog migration had a precondition guard
/// that aborted the deploy when a survivor lookup returned NOT EXISTS;
/// diagnosis showed the shape it depended on had been altered by
/// F11.7.6.4's dedup. No captured logs. Blast radius: full deploy
/// blocked, next-slice work stalled while a human debugged.
///
/// <para>Rule: any data-heal migration under
/// <c>*/Persistence/Migrations/*.cs</c> that runs raw SQL via
/// <c>MigrationBuilder.Sql</c> MUST NOT contain the string literal
/// <c>RAISE EXCEPTION</c>. Use <c>RAISE WARNING</c> or <c>RAISE NOTICE</c>
/// for observability; make the SQL best-effort (WHERE-guarded UPDATE that
/// skips rows lacking preconditions) rather than pre-flight-throwing.
/// Schema DDL migrations are exempt (nothing to heal there).</para>
///
/// <para>The architect's post-mortem: "Data-heal migrations should be
/// idempotent + tolerant of 'already partially healed' state, not abort
/// on it. If you want a safety net, emit RAISE WARNING when properties
/// remain owned by a DevAuth persona post-run."</para>
/// </summary>
public sealed class DataHealMigrationTolerantShapeTests
{
    private static readonly string[] MigrationGlobs =
    {
        "src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Catalog/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Pricing/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Reviews/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Messaging/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Sync/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Loyalty/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Payment/Infrastructure/Persistence/Migrations",
        "src/Modules/VrBook.Modules.Notifications/Infrastructure/Persistence/Migrations",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can enumerate migration source files.");
        return dir!.FullName;
    }

    [Fact]
    public void No_data_heal_migration_uses_RAISE_EXCEPTION_to_hard_block_deploy()
    {
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var relPath in MigrationGlobs)
        {
            var dir = Path.Combine(root, relPath);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                // Skip auto-generated designers.
                var name = Path.GetFileName(file);
                if (name.EndsWith(".Designer.cs", StringComparison.Ordinal))
                {
                    continue;
                }
                if (name == "IdentityDbContextModelSnapshot.cs")
                {
                    continue;
                }
                if (name.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (!text.Contains("MigrationBuilder.Sql", StringComparison.Ordinal))
                {
                    continue;
                }

                // Scan for RAISE EXCEPTION only INSIDE verbatim string literals
                // (@"...") passed to MigrationBuilder.Sql. C# doc-comments,
                // regular comments, and normal string literals are exempt so
                // the test can reference the phrase in narrative prose.
                var sqlBlocks = Regex.Matches(
                    text,
                    @"MigrationBuilder\.Sql\s*\(\s*@""(?<sql>(?:[^""]|"""")*)""\s*\)",
                    RegexOptions.Singleline);
                foreach (Match block in sqlBlocks)
                {
                    if (Regex.IsMatch(block.Groups["sql"].Value, @"RAISE\s+EXCEPTION"))
                    {
                        offenders.Add(Path.GetRelativePath(root, file));
                        break;
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            because: "F11.7.7 post-mortem: raw-SQL data-heal migrations that RAISE EXCEPTION become non-observable deploy blockers when Log Analytics misses the short-lived migrator job's stdout. Use RAISE WARNING or RAISE NOTICE + a best-effort UPDATE (WHERE-guarded skip) instead. If a genuine deploy-blocking invariant is needed, put it in a DDL migration or a health-check startup task where the failure surface is observable.");
    }
}
