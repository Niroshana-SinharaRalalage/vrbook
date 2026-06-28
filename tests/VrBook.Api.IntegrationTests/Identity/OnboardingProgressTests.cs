using FluentAssertions;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Tenants.Common;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.7 §3.1 + §4.1 + Step 2 — pins every branch of the
/// server-side derivation. Adding a state-machine branch without
/// updating this test should fail in CI.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OnboardingProgressTests
{
    private static MeTenantDto Tenant(bool hasStripe, int propertyCount, string status) => new(
        Id: Guid.NewGuid(),
        Slug: "seed",
        DisplayName: "Seed",
        Status: status,
        DefaultCurrency: "USD",
        PlatformFeeBps: 1500,
        StripeAccountStatus: hasStripe ? "Active" : null,
        ChargesEnabled: hasStripe,
        PayoutsEnabled: hasStripe,
        HasStripeAccount: hasStripe,
        PropertyCount: propertyCount,
        Onboarding: new OnboardingProgressDto(false, "Welcome"));

    [Theory]
    [InlineData(false, 0, "PendingOnboarding", "Welcome")]
    [InlineData(false, 1, "PendingOnboarding", "ConnectStripe")]
    [InlineData(false, 5, "PendingOnboarding", "ConnectStripe")]
    [InlineData(true, 0, "PendingOnboarding", "CreateProperty")]
    [InlineData(true, 1, "Active", "Done")]
    [InlineData(true, 1, "PendingOnboarding", "AwaitingVerification")]
    [InlineData(true, 1, "Suspended", "AwaitingVerification")]
    [InlineData(true, 1, "Closed", "Done")]
    public void DeriveNextStep_returns_expected_step_for_each_state(
        bool hasStripe, int propertyCount, string status, string expected)
    {
        var t = Tenant(hasStripe, propertyCount, status);
        OnboardingProgress.DeriveNextStep(t).Should().Be(expected);
    }

    [Fact]
    public void DeriveIsComplete_true_when_active_with_stripe_and_at_least_one_property()
    {
        OnboardingProgress.DeriveIsComplete(Tenant(true, 1, "Active")).Should().BeTrue();
    }

    [Fact]
    public void DeriveIsComplete_false_when_no_properties()
    {
        OnboardingProgress.DeriveIsComplete(Tenant(true, 0, "Active")).Should().BeFalse();
    }

    [Fact]
    public void DeriveIsComplete_false_when_no_stripe()
    {
        OnboardingProgress.DeriveIsComplete(Tenant(false, 1, "Active")).Should().BeFalse();
    }

    [Fact]
    public void DeriveIsComplete_false_when_status_not_Active()
    {
        OnboardingProgress.DeriveIsComplete(Tenant(true, 1, "PendingOnboarding")).Should().BeFalse();
    }

    [Fact]
    public void DeriveNextStep_throws_when_dto_is_null()
    {
        var act = () => OnboardingProgress.DeriveNextStep(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
