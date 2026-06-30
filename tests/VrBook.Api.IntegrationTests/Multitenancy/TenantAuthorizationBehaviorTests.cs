using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Identity.Application.Behaviors;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// OPS.M.4 Step 5 — pipeline-level rejection scenarios for the
/// <see cref="TenantAuthorizationBehavior{TRequest,TResponse}"/>.
///
/// <para>The architect's plan §6 prescribed a full HTTP-driven
/// CrossTenantWriteRejectionTests pack with ~12 scenarios per module. The
/// unit-level cuts below exercise the same contract surface without booting
/// the Postgres testcontainer + WebApplicationFactory + DevAuth — they prove
/// the gate, not the wire. The HTTP-driven equivalents land in
/// Slice OPS.M.10's cross-tenant isolation test pack alongside the RLS
/// integration coverage from Slice OPS.M.9.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantAuthorizationBehaviorTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private sealed record ScopedCommand(Guid TenantId) : IRequest<string>, ITenantScoped;
    private sealed record UnscopedCommand() : IRequest<string>;

    private static TenantAuthorizationBehavior<TRequest, string> NewBehavior<TRequest>(ICurrentUser user)
        where TRequest : notnull
        => new(user, NullLogger<TenantAuthorizationBehavior<TRequest, string>>.Instance);

    private static RequestHandlerDelegate<string> OkDelegate() => () => Task.FromResult("ok");

    private static ICurrentUser FakeUser(bool authenticated, Guid? tenantId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsAuthenticated.Returns(authenticated);
        u.TenantId.Returns(tenantId);
        return u;
    }

    [Fact]
    public async Task Unscoped_command_passes_through_regardless_of_caller()
    {
        var sut = NewBehavior<UnscopedCommand>(FakeUser(authenticated: false, tenantId: null));
        var result = await sut.Handle(new UnscopedCommand(), OkDelegate(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Unauthenticated_caller_with_scoped_command_is_rejected()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: false, tenantId: null));
        var act = () => sut.Handle(new ScopedCommand(TenantA), OkDelegate(), default);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("Sign-in required.");
    }

    [Fact]
    public async Task Authenticated_caller_with_no_tenant_membership_is_rejected()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: null));
        var act = () => sut.Handle(new ScopedCommand(TenantA), OkDelegate(), default);
        await act.Should().ThrowAsync<CrossTenantAccessException>()
            .Where(ex => ex.AttemptedTenantId == TenantA && ex.ActualTenantId == null);
    }

    [Fact]
    public async Task Tenant_A_caller_targeting_tenant_B_command_is_rejected()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: TenantA));
        var act = () => sut.Handle(new ScopedCommand(TenantB), OkDelegate(), default);
        await act.Should().ThrowAsync<CrossTenantAccessException>()
            .Where(ex => ex.AttemptedTenantId == TenantB && ex.ActualTenantId == TenantA);
    }

    [Fact]
    public async Task Tenant_A_caller_targeting_their_own_tenant_passes()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: TenantA));
        var result = await sut.Handle(new ScopedCommand(TenantA), OkDelegate(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public void CrossTenantAccessException_subclasses_ForbiddenException()
    {
        // Verifies the RFC 7807 mapping continues to work polymorphically -
        // Hellang.Middleware.ProblemDetails' Map<ForbiddenException> uses
        // "is T" so subclasses inherit the 403 mapping per OPS_M_4_PLAN §3.3.
        typeof(CrossTenantAccessException).IsSubclassOf(typeof(ForbiddenException))
            .Should().BeTrue();
    }

    [Fact]
    public void CrossTenantAccessException_message_includes_both_tenant_ids()
    {
        var ex = new CrossTenantAccessException(TenantA, TenantB);
        ex.Message.Should().Contain(TenantA.ToString("D"));
        ex.Message.Should().Contain(TenantB.ToString("D"));
    }

    // ---- Slice OPS.M.10.2 F11.7.5.1 — BackgroundTenantScope fallback ----
    //
    // Scenario: a tenant-less caller (e.g. a guest cancelling their own
    // booking) needs to dispatch a sub-command that IS ITenantScoped (the
    // refund) without their own TenantId. The handler opens a
    // BackgroundTenantScope with the row-resolved booking tenant; the
    // behavior must consult the scope when ICurrentUser.TenantId is null.
    //
    // The guest-cancel flow is the canonical case (CancelBookingHandler
    // line 36 opens the scope before dispatching RefundForBookingCommand)
    // — without this bypass the F11.7 walk hit a 403 panel on every
    // guest cancel + a half-success state where the booking row had been
    // marked Cancelled but the refund had been rejected upstream.

    [Fact]
    public async Task Tenant_less_caller_with_scope_matching_command_tenant_passes()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: null));
        using var scope = BackgroundTenantScope.Enter(TenantA);
        var result = await sut.Handle(new ScopedCommand(TenantA), OkDelegate(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task Tenant_less_caller_with_scope_mismatching_command_tenant_is_rejected()
    {
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: null));
        using var scope = BackgroundTenantScope.Enter(TenantA);
        var act = () => sut.Handle(new ScopedCommand(TenantB), OkDelegate(), default);
        await act.Should().ThrowAsync<CrossTenantAccessException>()
            .Where(ex => ex.AttemptedTenantId == TenantB && ex.ActualTenantId == TenantA);
    }

    [Fact]
    public async Task Tenant_less_caller_with_no_scope_active_is_still_rejected()
    {
        // Regression net for the no-scope case — guard against the bypass
        // accidentally promoting null-tenant + no-scope into a free pass.
        var sut = NewBehavior<ScopedCommand>(FakeUser(authenticated: true, tenantId: null));
        BackgroundTenantScope.CurrentTenantId.Should().BeNull(
            because: "no scope is active at the test entry point.");
        var act = () => sut.Handle(new ScopedCommand(TenantA), OkDelegate(), default);
        await act.Should().ThrowAsync<CrossTenantAccessException>()
            .Where(ex => ex.AttemptedTenantId == TenantA && ex.ActualTenantId == null);
    }
}
