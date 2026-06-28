using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Behaviors;
using VrBook.Modules.Sync.Application.Behaviors;
using Xunit;

namespace VrBook.Api.IntegrationTests.Sync;

// Castle.DynamicProxy used by NSubstitute needs ILogger<X<...>>'s generic args
// to live in publicly-visible types. Hoisted out of the test class.
public sealed record TestBgCommand(Guid TenantId)
    : IRequest<Unit>, IBackgroundCommand, ITenantScoped;

public sealed record TestForegroundTenantCommand(Guid TenantId)
    : IRequest<Unit>, ITenantScoped;

/// <summary>
/// Slice OPS.M.6 §3.1 (D1) — pins the two-behavior pair that makes background
/// worker commands safe:
/// <list type="bullet">
/// <item><c>TenantAuthorizationBehavior</c> must early-return for
///       <see cref="IBackgroundCommand"/> (no <c>ICurrentUser</c> available).</item>
/// <item><c>BackgroundCommandTenantScopeBehavior</c> must reject an unstamped
///       (TenantId == Guid.Empty) command before the handler runs, and must
///       push the tenant id into the logging scope.</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public sealed class BackgroundCommandTenantScopeBehaviorTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    // --- TenantAuthorizationBehavior early-return ---

    [Fact]
    public async Task TenantAuthorizationBehavior_early_returns_when_request_is_IBackgroundCommand()
    {
        // Anonymous user — would throw ForbiddenException for a foreground
        // ITenantScoped command. For an IBackgroundCommand it must pass through.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var behavior = new TenantAuthorizationBehavior<TestBgCommand, Unit>(
            currentUser,
            NullLogger<TenantAuthorizationBehavior<TestBgCommand, Unit>>.Instance);

        var called = false;
        RequestHandlerDelegate<Unit> next = () => { called = true; return Task.FromResult(Unit.Value); };

        await behavior.Handle(new TestBgCommand(TenantA), next, CancellationToken.None);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task TenantAuthorizationBehavior_still_gates_foreground_ITenantScoped_commands()
    {
        // Sanity: the early-return path is specific to IBackgroundCommand. A
        // plain ITenantScoped request from an anonymous caller still 403s.
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(false);

        var behavior = new TenantAuthorizationBehavior<TestForegroundTenantCommand, Unit>(
            currentUser,
            NullLogger<TenantAuthorizationBehavior<TestForegroundTenantCommand, Unit>>.Instance);

        Func<Task> act = () => behavior.Handle(
            new TestForegroundTenantCommand(TenantA),
            () => Task.FromResult(Unit.Value),
            CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // --- BackgroundCommandTenantScopeBehavior ---

    [Fact]
    public async Task BackgroundCommandTenantScopeBehavior_throws_when_TenantId_is_empty()
    {
        var logger = NullLogger<BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>>.Instance;
        var behavior = new BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>(logger);

        Func<Task> act = () => behavior.Handle(
            new TestBgCommand(Guid.Empty),
            () => Task.FromResult(Unit.Value),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Rule.Should().Be("sync.background_command_unstamped");
    }

    [Fact]
    public async Task BackgroundCommandTenantScopeBehavior_passes_through_when_TenantId_is_stamped()
    {
        var logger = NullLogger<BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>>.Instance;
        var behavior = new BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>(logger);

        var called = false;
        RequestHandlerDelegate<Unit> next = () => { called = true; return Task.FromResult(Unit.Value); };
        await behavior.Handle(new TestBgCommand(TenantA), next, CancellationToken.None);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task BackgroundCommandTenantScopeBehavior_pushes_tenant_id_into_logging_scope()
    {
        var logger = Substitute.For<ILogger<BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>>>();
        var behavior = new BackgroundCommandTenantScopeBehavior<TestBgCommand, Unit>(logger);

        await behavior.Handle(
            new TestBgCommand(TenantA),
            () => Task.FromResult(Unit.Value),
            CancellationToken.None);

        // The behavior wraps next() in a BeginScope so downstream logs carry tenant_id.
        logger.Received(1).BeginScope(Arg.Is<IDictionary<string, object>>(d =>
            d.ContainsKey("tenant_id") && (Guid)d["tenant_id"] == TenantA));
    }

    [Fact]
    public async Task BackgroundCommandTenantScopeBehavior_skips_non_background_requests()
    {
        // Sanity: the behavior is registered for ALL requests (open-generic);
        // it must be a no-op for non-IBackgroundCommand types.
        var logger = NullLogger<BackgroundCommandTenantScopeBehavior<TestForegroundTenantCommand, Unit>>.Instance;
        var behavior = new BackgroundCommandTenantScopeBehavior<TestForegroundTenantCommand, Unit>(logger);

        var called = false;
        await behavior.Handle(
            new TestForegroundTenantCommand(TenantA),
            () => { called = true; return Task.FromResult(Unit.Value); },
            CancellationToken.None);
        called.Should().BeTrue();
    }
}
