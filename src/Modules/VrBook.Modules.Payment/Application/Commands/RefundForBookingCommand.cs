using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

/// <summary>
/// Issue a refund tied to a booking. Amount=null means "platform-policy refund" -
/// applies <see cref="RefundOptions.ServiceFeePercent"/> to the captured total.
/// Caller passing an explicit Amount overrides the policy (admin manual refund).
///
/// <para>OPS.M.5 §3.6 (D6) — when the tenant has a Connect account the refund
/// is routed with proportional application-fee reversal; the negative-balance
/// guard prevents pushing the connected balance below zero.</para>
/// </summary>
/// <summary>
/// OPS.M.10.2 C4 (#2 High) — implements <see cref="ITenantScoped"/> so the
/// M.4 <c>TenantAuthorizationBehavior</c> gates the command BEFORE the
/// handler runs. Sole defense pre-fix was M.9 RLS on
/// <c>payment.payment_intents</c>; if RLS regressed, OwnerA could refund
/// OwnerB's booking by guessing the bookingId.
/// </summary>
public sealed record RefundForBookingCommand(
    Guid BookingId,
    decimal? Amount,
    string Reason,
    Guid TenantId) : IRequest<bool>, ITenantScoped;

internal sealed class RefundForBookingHandler(
    IStripeGateway stripe,
    ITenantStripeContextLookup tenantStripe,
    IPaymentIntentRepository repo,
    IUnitOfWork uow,
    IOptions<RefundOptions> refundOptions,
    IConfiguration configuration,
    ILogger<RefundForBookingHandler> logger)
    : IRequestHandler<RefundForBookingCommand, bool>
{
    private const string AllowPlatformFallbackKey = "Payment:AllowPlatformFallback";

    public async Task<bool> Handle(RefundForBookingCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning(
                "Stripe not configured; refund is a no-op for booking {BookingId}.", cmd.BookingId);
            return false;
        }
        var pi = await repo.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (pi is null)
        {
            return false; // No payment was taken; nothing to refund.
        }
        // OPS.M.10.2 C4 (#2 High) — defense-in-depth row-level tenant check.
        // The M.4 behavior already gated the command via ITenantScoped; if
        // RLS lets a row through (e.g. a future bypass-scope leak) this
        // belt-and-braces check still prevents a cross-tenant refund.
        if (pi.TenantId != cmd.TenantId)
        {
            return false;
        }
        if (pi.Status != PaymentStatus.Succeeded)
        {
            var cancelled = await stripe.CancelPaymentIntentAsync(pi.StripePaymentIntentId, cancellationToken);
            pi.UpdateStatus(cancelled.Status, cancelled.ChargeId);
            await uow.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Cancelled uncaptured PI {Pi} for booking {BookingId} (was {Was})",
                pi.StripePaymentIntentId, cmd.BookingId, pi.Status);
            return true;
        }

        var refundAmount = cmd.Amount ?? ComputeRefundAmount(pi.Amount, refundOptions.Value.ServiceFeePercent);

        // OPS.M.5 §3.6 (D6) — over-refund guard. Sum prior refunds and reject if
        // (this + prior) exceeds the captured total.
        var sumPriorRefunds = pi.Refunds.Sum(r => r.Amount);
        if (refundAmount + sumPriorRefunds > pi.Amount)
        {
            throw new BusinessRuleViolationException(
                "payment.over_refund",
                $"Refund {refundAmount:F2} plus prior {sumPriorRefunds:F2} exceeds captured {pi.Amount:F2}.");
        }

        // OPS.M.5 §3.4 (D4) — tenant Stripe context for fee math + Connect routing.
        var ctx = await tenantStripe.GetAsync(pi.TenantId, cancellationToken);
        var fallbackAllowed = configuration.GetValue<bool>(AllowPlatformFallbackKey, false);
        var hasConnectAccount = ctx?.StripeAccountId is not null;
        bool refundApplicationFee = false;
        long? feeReversalCents = null;
        if (hasConnectAccount)
        {
            // Negative-balance guard per §3.6. Sufficient condition: after this
            // refund + reversal, remaining connected balance ≥ 0. Algebraically,
            // (capturedAmount - applicationFeeAmount) - sum(refundedNet) ≥ thisRefundNet.
            var bps = ctx!.PlatformFeeBps;
            var applicationFeeAmount = StripeFeeCalculator.ApplicationFeeCents(pi.Amount, bps) / 100m;
            // Sum prior NET refunds to the tenant = priorRefunds - priorFeeReversals.
            // We don't persist the prior reversal cents on Refund yet (§3.6 follow-up),
            // so approximate: assume each prior refund used the proportional reversal.
            var priorTenantNet = sumPriorRefunds
                - sumPriorRefunds * bps / 10_000m;
            var thisRefundNet = refundAmount - refundAmount * bps / 10_000m;
            var availableConnectedBalance = (pi.Amount - applicationFeeAmount) - priorTenantNet;
            if (thisRefundNet > availableConnectedBalance)
            {
                throw new NegativeBalanceRefundException(
                    pi.Id, refundAmount, availableConnectedBalance);
            }

            var isFullRefund = refundAmount + sumPriorRefunds == pi.Amount;
            refundApplicationFee = true;
            feeReversalCents = isFullRefund
                ? null
                : StripeFeeCalculator.ProportionalFeeReversalCents(refundAmount, pi.Amount, bps);
        }
        else if (!fallbackAllowed)
        {
            // No Connect account + no staging fallback → legacy refund path is
            // an audit-trail leak. Reject loudly.
            throw new BusinessRuleViolationException(
                "payment.connect_account_missing",
                $"Tenant {pi.TenantId:D} has no Connect account. Refund cannot route the fee reversal correctly.");
        }

        logger.LogInformation(
            "Refunding {RefundAmount} of {Captured} for booking {BookingId} reverse_fee={Reverse} reversal_cents={Cents}",
            refundAmount, pi.Amount, cmd.BookingId, refundApplicationFee, feeReversalCents);

        // Generate the refund id up-front so we can pass it as the idempotency key
        // anchor + tag the Refund aggregate row with it.
        var refundId = Guid.NewGuid();
        var stripeRefund = hasConnectAccount
            ? await stripe.RefundAsync(
                pi.StripePaymentIntentId, refundAmount,
                idempotencyKey: StripeIdempotency.ForRefund(refundId),
                reason: cmd.Reason,
                refundApplicationFee: refundApplicationFee,
                applicationFeeRefundCents: feeReversalCents,
                cancellationToken: cancellationToken)
            : await stripe.RefundAsync(
                pi.StripePaymentIntentId, refundAmount,
                idempotencyKey: StripeIdempotency.ForRefund(refundId),
                reason: cmd.Reason,
                cancellationToken: cancellationToken);
        pi.AddRefund(stripeRefund.Id, stripeRefund.Amount, cmd.Reason);
        await uow.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static decimal ComputeRefundAmount(decimal captured, decimal serviceFeePct)
    {
        var pct = Math.Clamp(serviceFeePct, 0m, 100m);
        var refund = captured * (1m - pct / 100m);
        return decimal.Round(refund, 2, MidpointRounding.AwayFromZero);
    }
}
