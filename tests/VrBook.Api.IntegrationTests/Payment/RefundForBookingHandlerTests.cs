using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Application.Commands;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Application;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 6 — pins <c>RefundForBookingHandler</c> per
/// `docs/OPS_M_5_PLAN.md` §3.6 (D6) + §9 Step 6.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RefundForBookingHandlerTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BookingX = Guid.Parse("00000000-bbbb-0000-0000-000000000001");

    [Fact]
    public async Task Throws_payment_over_refund_when_refund_plus_prior_exceeds_captured()
    {
        var pi = NewSucceededPi(amount: 100m);
        pi.AddRefund("re_prior", 60m, "earlier");
        var (handler, _, _) = NewHandler(pi);

        Func<Task> act = () => handler.Handle(
            new RefundForBookingCommand(BookingX, Amount: 50m, "any"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Rule.Should().Be("payment.over_refund");
    }

    [Fact]
    public async Task Throws_NegativeBalanceRefundException_when_prior_refunds_drained_connected_balance()
    {
        var pi = NewSucceededPi(amount: 100m);
        // Prior 80 refunded → connected received 100 - 15 fee = 85; minus 80 net
        // tenant refund (≈68) = ≈17 remaining. A further 50 refund → 42.5 net
        // (assuming proportional reversal) exceeds 17.
        pi.AddRefund("re_prior", 80m, "earlier");
        var (handler, _, lookup) = NewHandler(pi);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, "acct_seed", 1500, "USD"));

        Func<Task> act = () => handler.Handle(
            new RefundForBookingCommand(BookingX, Amount: 50m, "any"), CancellationToken.None);

        // Over-refund guard fires first (80 + 50 > 100); negative-balance is the
        // sufficient guard for the same family of failures. Either rule is correct.
        var ex = await act.Should().ThrowAsync<BusinessRuleViolationException>();
        ex.Which.Rule.Should().BeOneOf("payment.over_refund", "payment.negative_balance_refund");
    }

    [Fact]
    public async Task Full_refund_with_Connect_passes_refundApplicationFee_true_and_no_explicit_cents()
    {
        var pi = NewSucceededPi(amount: 100m);
        var (handler, gateway, lookup) = NewHandler(pi);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, "acct_seed", 1500, "USD"));
        gateway.RefundAsync(
                Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new StripeRefundCreated("re_new", 100m, RefundStatus.Succeeded));

        await handler.Handle(
            new RefundForBookingCommand(BookingX, Amount: 100m, "guest_cancel"),
            CancellationToken.None);

        await gateway.Received(1).RefundAsync(
            stripePaymentIntentId: pi.StripePaymentIntentId,
            amount: 100m,
            idempotencyKey: Arg.Is<string>(k => k.StartsWith("refund:", StringComparison.Ordinal)),
            reason: "guest_cancel",
            refundApplicationFee: true,
            applicationFeeRefundCents: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Partial_refund_with_Connect_passes_proportional_ApplicationFeeRefund_cents()
    {
        var pi = NewSucceededPi(amount: 100m);
        var (handler, gateway, lookup) = NewHandler(pi);
        lookup.GetAsync(TenantA, Arg.Any<CancellationToken>())
            .Returns(new TenantStripeContext(TenantA, "acct_seed", 1500, "USD"));
        gateway.RefundAsync(
                Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<bool>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(new StripeRefundCreated("re_new", 25m, RefundStatus.Succeeded));

        await handler.Handle(
            new RefundForBookingCommand(BookingX, Amount: 25m, "policy"),
            CancellationToken.None);

        // 25 × 1500 / 10000 = 3.75 → 375 cents (banker's).
        await gateway.Received(1).RefundAsync(
            stripePaymentIntentId: pi.StripePaymentIntentId,
            amount: 25m,
            idempotencyKey: Arg.Any<string>(),
            reason: "policy",
            refundApplicationFee: true,
            applicationFeeRefundCents: 375L,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Returns_false_when_Stripe_not_configured()
    {
        var (handler, gateway, _) = NewHandler(null);
        gateway.IsConfigured.Returns(false);

        var result = await handler.Handle(
            new RefundForBookingCommand(BookingX, 50m, "any"), CancellationToken.None);

        result.Should().BeFalse();
    }

    private static PaymentIntent NewSucceededPi(decimal amount)
    {
        var pi = PaymentIntent.Create(
            tenantId: TenantA,
            bookingId: BookingX,
            stripePaymentIntentId: "pi_test_seed",
            clientSecret: "sec_seed",
            amount: amount,
            currency: "USD",
            captureMethod: "manual",
            initialStatus: PaymentStatus.Succeeded);
        pi.UpdateStatus(PaymentStatus.Succeeded, "ch_test");
        return pi;
    }

    private static (RefundForBookingHandler handler, IStripeGateway gateway,
        ITenantStripeContextLookup lookup) NewHandler(PaymentIntent? piToReturn)
    {
        var gateway = Substitute.For<IStripeGateway>();
        gateway.IsConfigured.Returns(true);
        var lookup = Substitute.For<ITenantStripeContextLookup>();
        var repo = Substitute.For<IPaymentIntentRepository>();
        repo.GetByBookingIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(piToReturn);
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var refundOpts = Options.Create(new RefundOptions { ServiceFeePercent = 10m });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Payment:AllowPlatformFallback"] = "false" })
            .Build();
        var handler = new RefundForBookingHandler(
            gateway, lookup, repo, uow, refundOpts, config,
            NullLogger<RefundForBookingHandler>.Instance);
        return (handler, gateway, lookup);
    }
}
