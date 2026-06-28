using FluentAssertions;
using VrBook.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Rls;

/// <summary>
/// Slice OPS.M.9 §4.5 (D5) Step 3 — pins the AsyncLocal stack semantics
/// of <see cref="BackgroundTenantScope"/>. The OPS.M.6
/// <c>BackgroundCommandTenantScopeBehavior</c> calls
/// <see cref="BackgroundTenantScope.Enter"/> at the start of every
/// background-command handler; the OPS.M.9 interceptor reads
/// <see cref="BackgroundTenantScope.CurrentTenantId"/> as the fallback
/// when <c>ICurrentUser.TenantId</c> is null.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BackgroundTenantScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void CurrentTenantId_is_null_by_default()
    {
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void CurrentTenantId_inside_Enter_block_returns_the_pushed_value()
    {
        using (BackgroundTenantScope.Enter(TenantA))
        {
            BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA);
        }
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void Nested_scopes_return_the_innermost_value()
    {
        using (BackgroundTenantScope.Enter(TenantA))
        {
            using (BackgroundTenantScope.Enter(TenantB))
            {
                BackgroundTenantScope.CurrentTenantId.Should().Be(TenantB);
            }
            BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA,
                "inner dispose pops the inner frame; outer is still on the stack.");
        }
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public void Enter_with_Guid_Empty_throws()
    {
        var act = () => BackgroundTenantScope.Enter(Guid.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Double_Dispose_is_a_no_op()
    {
        var scope = BackgroundTenantScope.Enter(TenantA);
        BackgroundTenantScope.CurrentTenantId.Should().Be(TenantA);
        scope.Dispose();
        scope.Dispose();
        BackgroundTenantScope.CurrentTenantId.Should().BeNull();
    }
}
