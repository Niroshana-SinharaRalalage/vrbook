using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Modules.Payment.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A5 PaymentIntent + Refund aggregate. Covers Stripe state
/// transitions, idempotency-by-refund, event emission, and validation of inputs.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaymentIntentAggregateTests
{
    private static PaymentIntent NewPi(decimal amount = 100m, PaymentStatus initialStatus = PaymentStatus.RequiresPaymentMethod) =>
        PaymentIntent.Create(new Guid("00000000-0000-0000-0000-000000000001"),
            bookingId: Guid.NewGuid(),
            stripePaymentIntentId: $"pi_test_{Guid.NewGuid():N}"[..20],
            clientSecret: "pi_test_secret_xyz",
            amount: amount,
            currency: "USD",
            captureMethod: "manual",
            initialStatus: initialStatus);

    [Fact]
    public void Create_initializes_fields_and_raises_PaymentAuthorized()
    {
        var pi = NewPi();

        pi.Status.Should().Be(PaymentStatus.RequiresPaymentMethod);
        pi.Currency.Should().Be("USD");
        pi.CaptureMethod.Should().Be("manual");
        pi.Refunds.Should().BeEmpty();
        pi.AuthorizedAt.Should().BeNull();
        pi.CapturedAt.Should().BeNull();
        pi.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PaymentAuthorized>();
    }

    [Fact]
    public void Create_uppercases_currency()
    {
        var pi = PaymentIntent.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "pi_x", "sec", 100m, "usd", "manual", PaymentStatus.RequiresPaymentMethod);
        pi.Currency.Should().Be("USD");
    }

    [Fact]
    public void Create_throws_on_zero_amount()
    {
        Action act = () => PaymentIntent.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "pi_x", "sec", 0m, "USD", "manual", PaymentStatus.RequiresPaymentMethod);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_throws_on_blank_stripe_id()
    {
        Action act = () => PaymentIntent.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "", "sec", 100m, "USD", "manual", PaymentStatus.RequiresPaymentMethod);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateStatus_to_RequiresCapture_sets_AuthorizedAt_once()
    {
        var pi = NewPi();
        pi.DequeueEvents();

        pi.UpdateStatus(PaymentStatus.RequiresCapture);
        var firstAuth = pi.AuthorizedAt;
        firstAuth.Should().NotBeNull();

        // Calling again should NOT overwrite the first authorization timestamp.
        pi.UpdateStatus(PaymentStatus.RequiresCapture);
        pi.AuthorizedAt.Should().Be(firstAuth);
    }

    [Fact]
    public void UpdateStatus_to_Succeeded_sets_CapturedAt_and_raises_PaymentCaptured()
    {
        var pi = NewPi();
        pi.DequeueEvents();

        pi.UpdateStatus(PaymentStatus.Succeeded, stripeChargeId: "ch_abc");

        pi.Status.Should().Be(PaymentStatus.Succeeded);
        pi.StripeChargeId.Should().Be("ch_abc");
        pi.CapturedAt.Should().NotBeNull();
        pi.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PaymentCaptured>();
    }

    [Fact]
    public void UpdateStatus_to_Succeeded_twice_does_not_overwrite_CapturedAt()
    {
        var pi = NewPi();
        pi.UpdateStatus(PaymentStatus.Succeeded);
        var first = pi.CapturedAt;

        pi.UpdateStatus(PaymentStatus.Succeeded);

        pi.CapturedAt.Should().Be(first);
    }

    [Fact]
    public void MarkFailed_sets_status_records_error_and_raises_event()
    {
        var pi = NewPi();
        pi.DequeueEvents();

        pi.MarkFailed("card_declined");

        pi.Status.Should().Be(PaymentStatus.Failed);
        pi.LastError.Should().Be("card_declined");
        pi.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<PaymentFailed>();
    }

    [Fact]
    public void AddRefund_appends_to_Refunds_and_raises_RefundIssued()
    {
        var pi = NewPi();
        pi.DequeueEvents();

        var refund = pi.AddRefund("re_test_1", 50m, "guest cancelled");

        pi.Refunds.Should().ContainSingle().Which.Should().Be(refund);
        refund.Amount.Should().Be(50m);
        refund.Currency.Should().Be("USD");
        refund.StripeRefundId.Should().Be("re_test_1");
        refund.Status.Should().Be(RefundStatus.Pending);

        var ev = pi.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<RefundIssued>().Subject;
        ev.Amount.Should().Be(50m);
        ev.Reason.Should().Be("guest cancelled");
    }

    [Fact]
    public void AddRefund_with_null_reason_emits_empty_string_in_event()
    {
        var pi = NewPi();
        pi.DequeueEvents();
        pi.AddRefund("re_x", 10m, null);
        var ev = (RefundIssued)pi.DequeueEvents().Single();
        ev.Reason.Should().BeEmpty();
    }

    [Fact]
    public void AddRefund_supports_multiple_partial_refunds()
    {
        var pi = NewPi(amount: 200m);
        pi.AddRefund("re_1", 75m, "partial 1");
        pi.AddRefund("re_2", 50m, "partial 2");

        pi.Refunds.Should().HaveCount(2);
        pi.Refunds.Sum(r => r.Amount).Should().Be(125m);
    }

    [Fact]
    public void Refund_throws_on_blank_stripe_refund_id_via_AddRefund()
    {
        var pi = NewPi();
        Action act = () => pi.AddRefund("", 10m, "x");
        act.Should().Throw<ArgumentException>();
    }
}
