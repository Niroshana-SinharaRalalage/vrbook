using FluentAssertions;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Sync.Application.SyncRuns.Commands;
using Xunit;

namespace VrBook.Api.IntegrationTests.Sync;

/// <summary>
/// Slice OPS.M.6 §3.1 + §3.5 (Step 2) — pins the
/// <see cref="RunSyncForFeedCommand"/> shape.
///
/// <para>Pre-OPS.M.6 the command was <c>(Guid ChannelFeedId)</c>: it
/// implemented neither <see cref="IBackgroundCommand"/> nor
/// <see cref="ITenantScoped"/>, so the worker called the handler with no
/// tenant gate and any future per-handler write was silently cross-tenant.
/// Step 2 closes that gap.</para>
///
/// <para>The worker call site is pinned by a source-scan test (the worker
/// has no DI seam we can mock).</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class RunSyncForFeedCommandShapeTests
{
    [Fact]
    public void Implements_IBackgroundCommand()
    {
        typeof(RunSyncForFeedCommand).GetInterfaces()
            .Should().Contain(typeof(IBackgroundCommand));
    }

    [Fact]
    public void Implements_ITenantScoped()
    {
        typeof(RunSyncForFeedCommand).GetInterfaces()
            .Should().Contain(typeof(ITenantScoped));
    }

    [Fact]
    public void Carries_non_empty_TenantId_after_construction()
    {
        var feedId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        var tenantId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var cmd = new RunSyncForFeedCommand(feedId, tenantId);
        cmd.TenantId.Should().Be(tenantId);
        cmd.ChannelFeedId.Should().Be(feedId);
    }

    [Fact]
    public void Worker_Program_passes_feed_TenantId_to_command()
    {
        // Source-scan against the worker entry point. We can't reflect the
        // top-level statements; the cheapest pin is the literal substring.
        var workerProgramPath = LocateWorkerProgramCs();
        var source = File.ReadAllText(workerProgramPath);
        source.Should().MatchRegex(
            @"new\s+RunSyncForFeedCommand\(\s*feed\.Id\s*,\s*feed\.TenantId\s*\)",
            because: "OPS.M.6 Step 2 — the worker MUST stamp the feed's tenant id; " +
                     "the BackgroundCommandTenantScopeBehavior asserts non-empty downstream.");
    }

    private static string LocateWorkerProgramCs()
    {
        // Walk up from the test bin folder to the repo root, then into the worker.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VrBook.sln"))
            && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "test must run from a directory inside the repo.");
        var candidate = Path.Combine(dir!.FullName, "src", "Workers", "VrBook.Workers.Sync", "Program.cs");
        File.Exists(candidate).Should().BeTrue(because: $"expected to find worker Program.cs at {candidate}");
        return candidate;
    }
}
