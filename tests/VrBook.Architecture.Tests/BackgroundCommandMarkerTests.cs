using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Interfaces;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.6 §3.1 (D1) + Step 1 — pins the <see cref="IBackgroundCommand"/>
/// marker contract.
///
/// <para>The marker labels commands that originate from a background worker
/// (no <c>ICurrentUser</c>). The MediatR <c>TenantAuthorizationBehavior</c>
/// early-returns for any <c>IBackgroundCommand</c>; <c>ITenantScoped</c> still
/// applies because the worker MUST stamp <c>TenantId</c> from the row it's
/// processing.</para>
///
/// <para>The arch invariant: <i>every</i> <c>IBackgroundCommand</c> must also
/// implement <c>ITenantScoped</c>. A future regression where someone marks a
/// command as background-only without a tenant gate would silently leak across
/// tenants on the worker side; this test prevents that.</para>
/// </summary>
public sealed class BackgroundCommandMarkerTests
{
    private static readonly Assembly[] CommandAssemblies = new[]
    {
        typeof(VrBook.Modules.Booking.Application.Commands.PlaceBookingCommand).Assembly,
        typeof(VrBook.Modules.Catalog.Application.Properties.Commands.CreatePropertyCommand).Assembly,
        typeof(VrBook.Modules.Sync.Application.ChannelFeeds.Commands.CreateChannelFeedCommand).Assembly,
        typeof(VrBook.Modules.Pricing.Application.Plans.Commands.UpdatePricingPlanCommand).Assembly,
        typeof(VrBook.Modules.Reviews.Application.Commands.SubmitReviewCommand).Assembly,
        typeof(VrBook.Modules.Notifications.Application.Commands.RetryNotificationCommand).Assembly,
        typeof(VrBook.Modules.Identity.Application.Tenants.Commands.OnboardTenantStripeCommand).Assembly,
    };

    [Fact]
    public void IBackgroundCommand_marker_exists_in_VrBook_Contracts_Interfaces()
    {
        var marker = typeof(IBackgroundCommand);
        marker.Namespace.Should().Be("VrBook.Contracts.Interfaces");
        marker.IsInterface.Should().BeTrue();
    }

    [Fact]
    public void Every_IBackgroundCommand_also_implements_ITenantScoped()
    {
        var offenders = CommandAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IBackgroundCommand).IsAssignableFrom(t))
            .Where(t => !typeof(ITenantScoped).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty(
            because: "OPS.M.6 §3.1 — workers stamp TenantId from the row they process; " +
                     "every IBackgroundCommand must also be ITenantScoped or it leaks across tenants.");
    }
}
