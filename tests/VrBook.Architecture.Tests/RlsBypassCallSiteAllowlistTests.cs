using System.Reflection;
using FluentAssertions;
using VrBook.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.9 §7 + §8 Step 12 — pins the allow-list of types that may
/// inject <see cref="IRlsBypassDbContextFactory{TContext}"/>. The bypass
/// reveals every tenant's data; the safety review depends on the small,
/// known set of call sites being exhaustively enumerated here.
///
/// <para>If a new constructor injection appears, this test fails until the
/// engineer adds the class to the allow-list AND updates the §7 inventory
/// in <c>docs/OPS_M_9_PLAN.md</c>. New entries require an explicit
/// engineering review — the bypass is the load-bearing escape hatch for
/// PlatformAdmin reads.</para>
/// </summary>
public sealed class RlsBypassCallSiteAllowlistTests
{
    /// <summary>
    /// Allowed types that may inject <c>IRlsBypassDbContextFactory&lt;&gt;</c>.
    /// Each entry must match section 7 of the plan exactly. The format is the
    /// type's FullName (namespace + name) to avoid name collisions across
    /// modules.
    /// </summary>
    private static readonly string[] AllowedFullNames = new[]
    {
        // Identity-side cross-tenant reads
        "VrBook.Modules.Identity.Infrastructure.TenantStripeContextLookup",
        // VRB-212 — cross-tenant tenant-readiness read for the property-activation gate;
        // mirrors TenantStripeContextLookup (Catalog calls it for the property's tenant).
        "VrBook.Modules.Identity.Infrastructure.TenantStripeReadinessLookup",
        "VrBook.Modules.Identity.Application.Tenants.Queries.ListPlatformTenantsHandler",
        "VrBook.Modules.Identity.Application.Tenants.Queries.GetPlatformTenantHandler",
        // Sync worker bootstrap — injected at top-level Program.cs via
        // GetRequiredService, not constructor. Detected by absence of
        // constructor injection in the worker assembly; allow-list documents
        // the intent.

        // Slice OPS.M.9.1 §2.4 — the guest-tenant resolver is the sole
        // bypass site for anonymous-read tenant resolution. Consumer
        // handlers (ComputeQuote, GetReviewsForProperty, SubmitReview,
        // GetOutboundFeed, GetPropertyAvailability, PlaceBooking,
        // GetBooking, MyBookings, CancelBooking) DO NOT inject
        // IRlsBypassDbContextFactory<>; they inject IGuestTenantResolver.
        // Lives in VrBook.Api (host-level) because it injects concrete
        // DbContexts from Catalog/Booking/Sync — VrBook.Infrastructure
        // doesn't reference module assemblies (layering).
        "VrBook.Api.Guests.GuestTenantResolver",
    };

    private static IEnumerable<Assembly> EnumerateModuleAssemblies()
    {
        // Pull the assemblies via known public types so AppDomain ordering
        // doesn't matter. Each entry is a module's owning assembly.
        return new[]
        {
            typeof(VrBook.Modules.Identity.Infrastructure.TenantStripeContextLookup).Assembly,
            typeof(VrBook.Modules.Booking.Application.Commands.PlaceBookingCommand).Assembly,
            typeof(VrBook.Modules.Catalog.Application.Properties.Commands.CreatePropertyCommand).Assembly,
            typeof(VrBook.Modules.Payment.Application.Commands.HandleStripeWebhookCommand).Assembly,
            typeof(VrBook.Modules.Pricing.Application.Plans.Commands.UpdatePricingPlanCommand).Assembly,
            typeof(VrBook.Modules.Reviews.Application.Commands.SubmitReviewCommand).Assembly,
            typeof(VrBook.Modules.Notifications.Application.Commands.RetryNotificationCommand).Assembly,
            typeof(VrBook.Modules.Sync.Application.ChannelFeeds.Commands.CreateChannelFeedCommand).Assembly,
            // Slice OPS.M.9.1 F6a — VrBook.Api hosts the
            // IGuestTenantResolver impl (the new allow-listed bypass site).
            typeof(VrBook.Api.Guests.GuestTenantResolver).Assembly,
        };
    }

    [Fact]
    public void IRlsBypassDbContextFactory_contract_returns_BypassedDbContext_wrapper()
    {
        // Sanity-check the contract shape: CreateForBypassAsync must return
        // BypassedDbContext<TContext> (not TContext directly) so the AsyncLocal
        // scope is cleaned up on disposal.
        var method = typeof(IRlsBypassDbContextFactory<>)
            .GetMethod("CreateForBypassAsync");
        method.Should().NotBeNull();
        var returnType = method!.ReturnType;
        returnType.IsGenericType.Should().BeTrue();
        returnType.GetGenericTypeDefinition().Should().Be(typeof(Task<>));
        var inner = returnType.GetGenericArguments()[0];
        inner.GetGenericTypeDefinition().Should().Be(typeof(BypassedDbContext<>),
            because: "OPS.M.9 §4.4 (D4) — wrapper composes inner DbContext disposal with AsyncLocal pop.");
    }

    [Fact]
    public void Every_constructor_injection_of_IRlsBypassDbContextFactory_lives_in_an_allowed_class()
    {
        var bypassInjectors = EnumerateModuleAssemblies()
            .SelectMany(a => TryGetTypes(a))
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            .Where(ctor => ctor.GetParameters().Any(IsBypassFactoryParam))
            .Select(ctor => ctor.DeclaringType!.FullName!)
            .Distinct()
            .ToList();

        var offenders = bypassInjectors
            .Where(name => !AllowedFullNames.Contains(name, StringComparer.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            because: "OPS.M.9 §7 — every IRlsBypassDbContextFactory<> injection must be on the allow-list. " +
                     "Adding a new bypass call site is a deliberate design review; update both this allow-list " +
                     "and docs/OPS_M_9_PLAN.md §7.");
    }

    private static bool IsBypassFactoryParam(ParameterInfo p)
    {
        var t = p.ParameterType;
        return t.IsGenericType
               && t.GetGenericTypeDefinition() == typeof(IRlsBypassDbContextFactory<>);
    }

    private static IEnumerable<Type> TryGetTypes(Assembly a)
    {
        try
        {
            return a.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
