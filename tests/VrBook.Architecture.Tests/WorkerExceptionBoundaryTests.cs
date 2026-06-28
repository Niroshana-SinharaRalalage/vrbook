using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.6 §3.6 + §3.7 (D6/D7) Step 6 — pins the sync worker's
/// shape via source-scan. The worker has no DI seam we can mock, so a
/// few well-chosen substrings are the cheapest way to lock the contract.
/// </summary>
public sealed class WorkerExceptionBoundaryTests
{
    private static string LocateProgramCs()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VrBook.sln"))
            && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull();
        var p = Path.Combine(dir!.FullName, "src", "Workers", "VrBook.Workers.Sync", "Program.cs");
        File.Exists(p).Should().BeTrue();
        return p;
    }

    private static string Source => File.ReadAllText(LocateProgramCs());

    [Fact]
    public void Worker_does_NOT_use_Thread_Sleep()
    {
        Source.Should().NotContain("Thread.Sleep",
            because: "OPS.M.6 §9 #3 — no blocking sleeps in the worker (use PeriodicTimer if you must poll).");
    }

    [Fact]
    public void Worker_does_NOT_use_Task_Delay_in_a_polling_loop()
    {
        // We don't ban Task.Delay outright (it's fine for back-off in catches);
        // we ban it inside `while`/`for` loops — that's the polling smell.
        var src = Source;
        var hasWhile = src.Contains("while", StringComparison.Ordinal)
            || src.Contains("for (;", StringComparison.Ordinal);
        var hasDelayInsideLoop = src.Contains("Task.Delay", StringComparison.Ordinal) && hasWhile;
        hasDelayInsideLoop.Should().BeFalse(
            because: "OPS.M.6 §9 #3 — the worker is a one-shot Container App Job; no polling loops.");
    }

    [Fact]
    public void Worker_does_NOT_use_PeriodicTimer()
    {
        Source.Should().NotContain("PeriodicTimer",
            because: "OPS.M.6 §3.6 (D6) — keep the Container App Job model; no in-process scheduler.");
    }

    [Fact]
    public void Worker_per_feed_iteration_is_try_caught()
    {
        var src = Source;
        var foreachIdx = src.IndexOf("foreach (var feed in due)", StringComparison.Ordinal);
        foreachIdx.Should().BeGreaterThan(-1,
            because: "the worker's main pass over due feeds is the contract surface.");
        var tail = src[foreachIdx..];
        tail.Should().Contain("try");
        tail.Should().Contain("catch (Exception");
    }

    [Fact]
    public void Worker_logs_tenant_id_and_feed_id_as_structured_fields()
    {
        var src = Source;
        // Either explicit template tokens OR a Serilog.Context.LogContext push
        // satisfies the structured-field requirement.
        var hasTokens = src.Contains("{TenantId}", StringComparison.Ordinal)
            || src.Contains("{FeedId}", StringComparison.Ordinal);
        var hasLogContext = src.Contains("LogContext.PushProperty", StringComparison.Ordinal)
            && src.Contains("tenant_id", StringComparison.Ordinal)
            && src.Contains("channel_feed_id", StringComparison.Ordinal);
        (hasTokens || hasLogContext).Should().BeTrue(
            because: "OPS.M.6 §9 #2 — tenant_id + channel_feed_id MUST be in structured fields, not interpolated.");
    }
}
