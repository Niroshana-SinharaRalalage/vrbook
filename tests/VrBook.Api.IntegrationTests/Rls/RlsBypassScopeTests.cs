using FluentAssertions;
using VrBook.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Rls;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) Step 1 — pins the AsyncLocal stack semantics of
/// <see cref="RlsBypassScope"/>. The interceptor reads
/// <see cref="RlsBypassScope.IsActive"/> on every command; a regression on
/// the stack depth would silently expose cross-tenant data.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RlsBypassScopeTests
{
    [Fact]
    public void IsActive_is_false_by_default()
    {
        RlsBypassScope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsActive_is_true_inside_Enter_using_block()
    {
        using (RlsBypassScope.Enter())
        {
            RlsBypassScope.IsActive.Should().BeTrue();
        }
        RlsBypassScope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Nested_scopes_compose_via_depth_counter()
    {
        using (RlsBypassScope.Enter())
        {
            RlsBypassScope.IsActive.Should().BeTrue();
            using (RlsBypassScope.Enter())
            {
                RlsBypassScope.IsActive.Should().BeTrue();
            }
            RlsBypassScope.IsActive.Should().BeTrue(
                "inner dispose decrements depth but outer scope is still active.");
        }
        RlsBypassScope.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Double_Dispose_is_a_no_op()
    {
        var scope = RlsBypassScope.Enter();
        RlsBypassScope.IsActive.Should().BeTrue();
        scope.Dispose();
        scope.Dispose(); // must not pop a second time
        RlsBypassScope.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task AsyncLocal_flag_flows_across_await()
    {
        using (RlsBypassScope.Enter())
        {
            await Task.Yield();
            RlsBypassScope.IsActive.Should().BeTrue();
            await Task.Delay(1);
            RlsBypassScope.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task AsyncLocal_flag_does_not_leak_to_parallel_scope()
    {
        // Two parallel tasks: one opens a bypass scope, the other should not
        // see it. AsyncLocal flows downstream not sideways.
        bool? sideEffectBypass = null;
        await Task.WhenAll(
            Task.Run(async () =>
            {
                using (RlsBypassScope.Enter())
                {
                    await Task.Delay(10);
                }
            }),
            Task.Run(async () =>
            {
                await Task.Delay(5);
                sideEffectBypass = RlsBypassScope.IsActive;
            }));
        sideEffectBypass.Should().BeFalse();
    }
}
