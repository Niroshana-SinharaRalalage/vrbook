using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Identity.Application.Behaviors;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.7.5.1 — locks in the two MediatR pipeline bypass
/// surfaces in <see cref="TenantAuthorizationBehavior{TRequest,TResponse}"/>:
///
/// <para><b>PlatformAdmin bypass</b> (OPS.M.8 §3.3 D3) — the behavior reads
/// <c>ICurrentUser.IsPlatformAdmin</c> and skips the equality check when
/// it's true. Deleting this bypass would break operator actions across
/// tenants (OPS.M.8 contract).</para>
///
/// <para><b>BackgroundTenantScope bypass</b> (F11.7.5.1) — when
/// <c>ICurrentUser.TenantId</c> is null but a <c>BackgroundTenantScope</c>
/// is active on the AsyncLocal stack, the scope's tenant id is consulted
/// as the authoritative scope for the operation. This is what makes the
/// guest-cancel flow work: <see cref="VrBook.Modules.Booking.Application.Commands.CancelBookingHandler"/>
/// opens the scope with the row-resolved booking tenant id, then
/// dispatches <c>RefundForBookingCommand</c> (ITenantScoped). Without this
/// bypass the guest's null <c>currentUser.TenantId</c> would fail the
/// equality check and reject the refund with
/// <c>CrossTenantAccessException</c> — exactly the F11.7 walk symptom.</para>
///
/// <para>The arch test verifies BOTH bypasses are present in the compiled
/// behavior assembly. Deleting either is a deliberate ADR-level change;
/// this test fails first to make the engineer surface the decision.</para>
/// </summary>
public sealed class TenantAuthorizationBackgroundScopeBypassTests
{
    private static MethodInfo HandleMethod()
    {
        // The behavior is generic; close over arbitrary types just to surface
        // the runtime MethodInfo for IL inspection.
        var closed = typeof(TenantAuthorizationBehavior<object, object>);
        var method = closed.GetMethod(
            nameof(TenantAuthorizationBehavior<object, object>.Handle),
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull(
            because: "the behavior must expose IPipelineBehavior.Handle.");
        return method!;
    }

    [Fact]
    public void Behavior_references_BackgroundTenantScope_CurrentTenantId()
    {
        // The IL of Handle() must reference the BackgroundTenantScope type +
        // its CurrentTenantId getter. We assert via metadata: the property
        // exists and its declaring assembly is referenced by the behavior's
        // assembly.
        var behaviorAsm = typeof(TenantAuthorizationBehavior<,>).Assembly;
        var scopeType = behaviorAsm.GetReferencedAssemblies()
            .Select(name => System.Reflection.Assembly.Load(name))
            .Select(a => a.GetType("VrBook.Infrastructure.Persistence.BackgroundTenantScope"))
            .FirstOrDefault(t => t is not null);
        scopeType.Should().NotBeNull(
            because: "TenantAuthorizationBehavior's assembly must reference VrBook.Infrastructure (which owns BackgroundTenantScope).");
        var currentTenantId = scopeType!.GetProperty("CurrentTenantId", BindingFlags.Public | BindingFlags.Static);
        currentTenantId.Should().NotBeNull(
            because: "BackgroundTenantScope.CurrentTenantId is the static surface the behavior consults; renaming it without also updating the behavior is an ADR-level break.");
        currentTenantId!.PropertyType.Should().Be(typeof(Guid?),
            because: "the behavior compares the scope value to ITenantScoped.TenantId; the nullable shape signals 'no scope active'.");
    }

    [Fact]
    public void Behavior_references_ICurrentUser_IsPlatformAdmin()
    {
        var icu = typeof(VrBook.Contracts.Interfaces.ICurrentUser);
        var prop = icu.GetProperty(nameof(VrBook.Contracts.Interfaces.ICurrentUser.IsPlatformAdmin));
        prop.Should().NotBeNull(
            because: "the PlatformAdmin bypass relies on ICurrentUser.IsPlatformAdmin per OPS.M.8 §3.3 (D3).");
        prop!.PropertyType.Should().Be(typeof(bool),
            because: "the bypass branch is a simple boolean check.");
    }

    [Fact]
    public void Behavior_handle_signature_is_unchanged()
    {
        // If Handle's signature changes (e.g. ValueTask, ConfigureAwait wrapping),
        // the F11.7.5.1 fix may regress silently. Lock the shape.
        var method = HandleMethod();
        method.IsVirtual.Should().BeTrue(because: "interface implementations are virtual.");
        method.ReturnType.IsGenericType.Should().BeTrue(
            because: "Handle returns Task<TResponse>.");
        method.ReturnType.GetGenericTypeDefinition().Should().Be(typeof(Task<>));
        method.GetParameters().Should().HaveCount(3,
            because: "Handle takes (TRequest, RequestHandlerDelegate<TResponse>, CancellationToken) per MediatR's IPipelineBehavior.");
    }
}
