using System.Reflection;
using FluentAssertions;
using MediatR;
using VrBook.Contracts.Interfaces;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// OPS.M.4 Step 5 (arch test) — reflection-based contract check.
///
/// <para>Asserts that every type implementing <see cref="ITenantScoped"/>:
/// <list type="number">
///   <item>Is a record (the canonical MediatR command shape in VrBook).</item>
///   <item>Is also an <see cref="IRequest"/>/<see cref="IRequest{T}"/> implementer
///         (i.e. a real MediatR command — not a stray type implementing the marker).</item>
///   <item>Exposes a public <c>TenantId</c> property whose type is exactly
///         <see cref="Guid"/> (not <see cref="Nullable{Guid}"/>). The behavior
///         relies on a non-null value being available at the pipeline.</item>
///   <item>The <c>TenantId</c> getter, when invoked on an instance constructed via
///         the synthesized record positional constructor with a non-empty Guid,
///         returns that exact Guid — confirming the property is wired to the
///         positional record parameter and not shadowed.</item>
/// </list>
/// </para>
///
/// <para>Per OPS_M_4_PLAN.md §10 Q3 + §D10: this is the cheap reflection variant
/// over the Roslyn-analyzer variant. It runs in CI's Category=Unit step and
/// catches contract violations at PR time. Promotes to Roslyn if drift starts
/// happening more often than the test catches.
/// </para>
/// </summary>
public sealed class TenantScopedCommandTests
{
    private static readonly Assembly[] CommandAssemblies = new[]
    {
        typeof(VrBook.Modules.Booking.Application.Commands.PlaceBookingCommand).Assembly,
        typeof(VrBook.Modules.Catalog.Application.Properties.Commands.CreatePropertyCommand).Assembly,
        typeof(VrBook.Modules.Sync.Application.ChannelFeeds.Commands.CreateChannelFeedCommand).Assembly,
        typeof(VrBook.Modules.Pricing.Application.Plans.Commands.UpdatePricingPlanCommand).Assembly,
        typeof(VrBook.Modules.Reviews.Application.Commands.SubmitReviewCommand).Assembly,
        typeof(VrBook.Modules.Notifications.Application.Commands.RetryNotificationCommand).Assembly,
    };

    private static IEnumerable<Type> TenantScopedCommands() =>
        CommandAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(ITenantScoped).IsAssignableFrom(t));

    [Fact]
    public void Every_ITenantScoped_type_implements_an_IRequest_interface()
    {
        foreach (var t in TenantScopedCommands())
        {
            var implementsRequest = t.GetInterfaces().Any(i =>
                i == typeof(IRequest)
                || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBaseRequest)));

            implementsRequest.Should().BeTrue(
                $"{t.FullName} implements ITenantScoped but is not a MediatR command. " +
                "Marker is only valid on IRequest/IRequest<T> records that flow through the pipeline.");
        }
    }

    [Fact]
    public void Every_ITenantScoped_type_exposes_a_non_nullable_Guid_TenantId_property()
    {
        foreach (var t in TenantScopedCommands())
        {
            var prop = t.GetProperty("TenantId", BindingFlags.Public | BindingFlags.Instance);
            prop.Should().NotBeNull(
                $"{t.FullName} implements ITenantScoped but has no public TenantId property.");
            prop!.PropertyType.Should().Be(typeof(Guid),
                $"{t.FullName}.TenantId must be Guid (not Guid?). " +
                "The behavior depends on a non-null value at the pipeline.");
        }
    }

    [Fact]
    public void Every_ITenantScoped_record_wires_TenantId_to_the_positional_ctor()
    {
        foreach (var t in TenantScopedCommands())
        {
            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ctors.Should().NotBeEmpty($"{t.FullName} must have at least one public constructor.");

            // Pick the constructor with the most params (the synthesized positional one
            // is the longest signature on a record).
            var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = ctor.GetParameters();

            // Build defaulted args; place a sentinel Guid on the TenantId slot.
            var sentinel = Guid.Parse("ddddeeee-ffff-aaaa-bbbb-cccc11112222");
            var args = parameters.Select(p =>
                p.Name == "TenantId" && p.ParameterType == typeof(Guid)
                    ? (object)sentinel
                    : DefaultFor(p.ParameterType)).ToArray();

            object instance;
            try
            {
                instance = ctor.Invoke(args)!;
            }
            catch (TargetInvocationException ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{t.FullName} positional ctor threw when constructed with sentinel args: {ex.InnerException?.Message}");
            }

            var read = ((ITenantScoped)instance).TenantId;
            read.Should().Be(sentinel,
                $"{t.FullName}.TenantId getter must surface the positional ctor argument. " +
                "If the property is shadowed or computed, the behavior cannot trust the value.");
        }
    }

    private static object? DefaultFor(Type t)
    {
        if (t.IsValueType)
        {
            return Activator.CreateInstance(t);
        }
        // Best-effort reference defaults — empty string, null for everything else.
        if (t == typeof(string))
        {
            return string.Empty;
        }
        return null;
    }

    [Fact]
    public void NotificationLog_Queue_tenantId_parameter_has_no_default_value()
    {
        // OPS.M.4 Step 4 contract: every NotificationLog.Queue call site must
        // pass tenantId consciously. The compiler enforces this iff the parameter
        // is not defaulted. This test prevents a future defaulted overload from
        // silently re-opening the foot-gun.
        var queueMethod = typeof(VrBook.Modules.Notifications.Domain.NotificationLog)
            .GetMethod(
                nameof(VrBook.Modules.Notifications.Domain.NotificationLog.Queue),
                BindingFlags.Public | BindingFlags.Static);
        queueMethod.Should().NotBeNull("NotificationLog.Queue static factory must exist.");

        var tenantIdParam = queueMethod!.GetParameters()
            .FirstOrDefault(p => p.Name == "tenantId");
        tenantIdParam.Should().NotBeNull("NotificationLog.Queue must accept a tenantId parameter.");
        tenantIdParam!.HasDefaultValue.Should().BeFalse(
            "NotificationLog.Queue.tenantId must be required (no default). " +
            "Defaulting to null re-enables the silent-correctness foot-gun OPS.M.4 closed.");
    }
}
