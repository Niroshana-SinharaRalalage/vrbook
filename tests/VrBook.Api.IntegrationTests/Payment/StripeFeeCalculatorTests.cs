using FluentAssertions;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 3 RED — pins the application-fee math per
/// `docs/OPS_M_5_PLAN.md` §3.6.
///
/// <para>Wrong rounding (especially when the .5 case falls into the platform's
/// favor) drains tenant balances over time. Banker's rounding
/// (<see cref="MidpointRounding.ToEven"/>) ensures no systematic bias.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeFeeCalculatorTests
{
    [Fact]
    public void ApplicationFeeCents_15_percent_of_100_dollars_is_1500_cents()
    {
        StripeFeeCalculator.ApplicationFeeCents(capturedAmount: 100.00m, platformFeeBps: 1500)
            .Should().Be(1500);
    }

    [Fact]
    public void ApplicationFeeCents_15_percent_of_33_33_uses_bankers_rounding()
    {
        // 33.33 × 1500 / 10000 = 4.9995 → rounded to 2 dp banker's = 5.00 → 500 cents.
        StripeFeeCalculator.ApplicationFeeCents(capturedAmount: 33.33m, platformFeeBps: 1500)
            .Should().Be(500);
    }

    [Fact]
    public void ApplicationFeeCents_uses_banker_rounding_on_exact_midpoint()
    {
        // 0.05 × 1000 / 10000 = 0.005 → midpoint, banker's rounds to 0.00 (nearest even) → 0 cents.
        StripeFeeCalculator.ApplicationFeeCents(capturedAmount: 0.05m, platformFeeBps: 1000)
            .Should().Be(0);
    }

    [Fact]
    public void ApplicationFeeCents_zero_amount_is_zero()
    {
        StripeFeeCalculator.ApplicationFeeCents(capturedAmount: 0m, platformFeeBps: 1500)
            .Should().Be(0);
    }

    [Fact]
    public void ProportionalFeeReversal_returns_null_for_full_refund()
    {
        // Full refund → caller passes refund_application_fee=true alone; no explicit cents.
        StripeFeeCalculator.ProportionalFeeReversalCents(
            refundAmount: 100m, capturedAmount: 100m, platformFeeBps: 1500)
            .Should().BeNull();
    }

    [Fact]
    public void ProportionalFeeReversal_partial_refund_uses_proportional_share()
    {
        // 25% partial refund of a 100 capture → reverse 25% of the 15-bps fee = 375 cents.
        StripeFeeCalculator.ProportionalFeeReversalCents(
            refundAmount: 25m, capturedAmount: 100m, platformFeeBps: 1500)
            .Should().Be(375);
    }
}
