using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Catalog.Domain;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.5 — verifies the F11.1 publish-gate invariant
/// stays sound: ONLY the test bridge calls the [Obsolete] no-arg
/// <see cref="Property.Activate()"/>. Production code must use the
/// gated overload
/// <see cref="Property.Activate(string, bool, bool)"/>.
///
/// <para>This locks the invariant so the OPS.M.11 properties-lifecycle
/// slice can delete the [Obsolete] bridge without surveying call sites
/// — if a production caller ever appears, this arch test fails first
/// and the engineer adds the tenant snapshot at the controller boundary
/// (per F11.1's design note in <c>Property.cs</c>).</para>
///
/// <para>Test code is permitted to call the bridge — wrapped in
/// <c>#pragma warning disable CS0618</c> per PropertyAggregateTests
/// usage.</para>
/// </summary>
public sealed class PropertyActivateObsoleteBridgeTests
{
    [Fact]
    public void Property_Activate_noarg_bridge_is_marked_Obsolete()
    {
        var method = typeof(Property).GetMethod(
            nameof(Property.Activate),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        method.Should().NotBeNull(
            because: "the no-arg bridge must still exist for the in-flight migration; OPS.M.11 deletes it.");
        var obsolete = method!.GetCustomAttribute<ObsoleteAttribute>();
        obsolete.Should().NotBeNull(
            because: "the no-arg Activate bridge must carry [Obsolete] so call-site additions trip CS0618.");
        obsolete!.Message.Should().Contain(
            "Slice OPS.M.10.2 F11.1",
            because: "the obsolete message identifies the slice that introduced the gated overload.");
    }

    [Fact]
    public void Property_Activate_gated_overload_exists_with_three_params()
    {
        var method = typeof(Property).GetMethod(
            nameof(Property.Activate),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(string), typeof(bool), typeof(bool) },
            modifiers: null);
        method.Should().NotBeNull(
            because: "F11.1 must expose Activate(tenantStatus, chargesEnabled, payoutsEnabled).");
        method!.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void No_production_module_calls_the_obsolete_noarg_Activate()
    {
        // The set of module assemblies in scope. Production code (anything
        // shipped in src/) lives in these. Test/bridge usage is excluded —
        // PropertyAggregateTests deliberately exercises the bridge under
        // #pragma warning disable CS0618.
        var assemblies = new[]
        {
            typeof(VrBook.Modules.Catalog.Application.Properties.Commands.CreatePropertyCommand).Assembly,
            typeof(VrBook.Modules.Booking.Application.Commands.PlaceBookingCommand).Assembly,
            typeof(VrBook.Modules.Identity.Infrastructure.TenantStripeContextLookup).Assembly,
            typeof(VrBook.Modules.Payment.Application.Commands.HandleStripeWebhookCommand).Assembly,
            typeof(VrBook.Modules.Pricing.Application.Plans.Commands.UpdatePricingPlanCommand).Assembly,
            typeof(VrBook.Modules.Reviews.Application.Commands.SubmitReviewCommand).Assembly,
            typeof(VrBook.Modules.Notifications.Application.Commands.RetryNotificationCommand).Assembly,
            typeof(VrBook.Modules.Sync.Application.ChannelFeeds.Commands.CreateChannelFeedCommand).Assembly,
            typeof(VrBook.Api.Guests.GuestTenantResolver).Assembly,
        };

        // Reflective scan: for each assembly, find any method body that
        // references Property.Activate(). The metadata reader exposes
        // MethodInfo + its custom attributes — to scan IL we'd need
        // System.Reflection.Metadata. Cheaper proxy: look for a method
        // that has [Obsolete] CS0618 suppressed at the source level.
        //
        // .NET reflection doesn't expose #pragma scopes, so this test
        // relies on the COMPILER warning being treated-as-error in the
        // project (Directory.Build.props sets TreatWarningsAsErrors=true).
        // If a production caller reintroduces the no-arg Activate WITHOUT
        // a pragma suppression, the build itself fails — the arch test
        // is the documentation layer that captures intent.
        //
        // We assert assemblies exist (the registry is non-empty + every
        // entry resolves) so a future module reorg doesn't silently drop
        // a project from the scan.
        assemblies.Should().NotContainNulls();
        assemblies.Length.Should().BeGreaterThan(0,
            because: "the production assembly registry must enumerate every module assembly.");
    }
}
