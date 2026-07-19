using FluentAssertions;
using VrBook.Modules.Catalog.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-212 — the pure property-activation gate (<see cref="Property.CheckActivation"/>),
/// the single source of truth enforced on the publish transition so a listing can't go
/// live while its tenant isn't Stripe-ready (closes the ExecuteUpdate bypass). No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PropertyActivationGateTests
{
    [Fact]
    public void PaymentReady_WithImage_CanActivate()
    {
        Property.CheckActivation("Active", tenantChargesEnabled: true, tenantPayoutsEnabled: true, imageCount: 1)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("PendingOnboarding")]
    [InlineData("Suspended")]
    [InlineData("Closed")]
    public void NonActiveTenantStatus_Blocked(string status)
    {
        var block = Property.CheckActivation(status, true, true, 1);

        block.Should().NotBeNull();
        block!.Code.Should().Be("property.tenant_not_payment_ready");
    }

    [Theory]
    [InlineData(false, true)]  // charges off
    [InlineData(true, false)]  // payouts off
    [InlineData(false, false)]
    public void ChargesOrPayoutsDisabled_Blocked(bool charges, bool payouts)
    {
        Property.CheckActivation("Active", charges, payouts, 1)!.Code
            .Should().Be("property.tenant_not_payment_ready");
    }

    [Fact]
    public void NoImages_Blocked_WithImageReason()
    {
        Property.CheckActivation("Active", true, true, imageCount: 0)!.Code
            .Should().Be("property.requires_image");
    }
}
