using FluentAssertions;
using VrBook.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 Step 8 — protects the OPS.M.9 AsyncLocal
/// contracts (<see cref="RlsBypassScope"/> + <see cref="BackgroundTenantScope"/>)
/// from regression patterns that would leak across awaits.
///
/// <para>The unit-level <c>RlsBypassScopeTests</c> already pins the depth
/// counter and same-thread Dispose semantics. This pack adds end-to-end
/// scenarios more representative of real handler execution:</para>
/// <list type="bullet">
///   <item>A scope opened inside a try/finally cleans up on exception</item>
///   <item>Two parallel tasks each enter their own scope; neither sees the other's flag</item>
///   <item>A nested factory pattern (rare but legal) composes correctly</item>
///   <item>A BackgroundTenantScope inside an RlsBypassScope: both AsyncLocals
///         independently maintain their state</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public sealed class AsyncLocalLeakFactPack
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-1010-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-1010-0000-0000-000000000002");

    [Fact]
    public void RlsBypassScope_Enter_in_try_finally_cleans_up_even_on_exception()
    {
        RlsBypassScope.IsActive.Should().BeFalse("clean baseline.");
        Action act = () =>
        {
            IDisposable? scope = null;
            try
            {
                scope = RlsBypassScope.Enter();
                throw new InvalidOperationException("simulated handler failure");
            }
            finally
            {
                scope?.Dispose();
            }
        };
        act.Should().Throw<InvalidOperationException>();
        RlsBypassScope.IsActive.Should().BeFalse(
            "the finally MUST pop the scope or every subsequent test would inherit a stale bypass.");
    }

    [Fact]
    public async Task RlsBypassScope_AsyncLocal_does_not_flow_sideways_into_parallel_task()
    {
        var sideTaskSawBypass = false;
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using (RlsBypassScope.Enter())
                {
                    await Task.Delay(20);
                }
            }),
            Task.Run(async () =>
            {
                await Task.Delay(10);
                sideTaskSawBypass = RlsBypassScope.IsActive;
            }));
        sideTaskSawBypass.Should().BeFalse(
            "AsyncLocal flows DOWN (continuations of the same logical chain), " +
            "not SIDEWAYS to parallel chains. A regression here would mean one " +
            "request's bypass leaks into a concurrent request's DbContext commands.");
    }

    [Fact]
    public void RlsBypassScope_nested_factory_pattern_composes_via_depth_counter()
    {
        using (RlsBypassScope.Enter())
        {
            RlsBypassScope.IsActive.Should().BeTrue();
            using (RlsBypassScope.Enter())
            {
                using (RlsBypassScope.Enter())
                {
                    RlsBypassScope.IsActive.Should().BeTrue("depth 3 — still active.");
                }
                RlsBypassScope.IsActive.Should().BeTrue("depth 2 — still active.");
            }
            RlsBypassScope.IsActive.Should().BeTrue("depth 1 — still active.");
        }
        RlsBypassScope.IsActive.Should().BeFalse("depth 0 — no longer active.");
    }

    [Fact]
    public void BackgroundTenantScope_inside_RlsBypassScope_maintains_both_AsyncLocals_independently()
    {
        // The interceptor reads BOTH AsyncLocals on every command; they
        // must not interfere with each other.
        RlsBypassScope.IsActive.Should().BeFalse();
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();

        using (RlsBypassScope.Enter())
        {
            RlsBypassScope.IsActive.Should().BeTrue();
            BackgroundTenantScope.CurrentTenantId.Should().BeNull(
                "RlsBypassScope.Enter must NOT touch BackgroundTenantScope state.");

            using (BackgroundTenantScope.Enter(TenantA))
            {
                RlsBypassScope.IsActive.Should().BeTrue();
                BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA);
            }

            BackgroundTenantScope.CurrentTenantId.Should().BeNull("BackgroundTenantScope dispose.");
            RlsBypassScope.IsActive.Should().BeTrue("Outer RlsBypassScope still active.");
        }

        RlsBypassScope.IsActive.Should().BeFalse();
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public async Task BackgroundTenantScope_innermost_value_wins_across_await()
    {
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
        using (BackgroundTenantScope.Enter(TenantA))
        {
            await Task.Yield();
            BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA);
            using (BackgroundTenantScope.Enter(TenantB))
            {
                await Task.Delay(1);
                BackgroundTenantScope.CurrentTenantId.Should().Be(TenantB,
                    "innermost frame on the stack wins; this is what the worker depends on " +
                    "when it iterates per-feed.");
            }
            BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA,
                "after inner dispose, outer is uncovered.");
        }
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }
}
