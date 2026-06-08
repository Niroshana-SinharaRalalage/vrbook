using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Enums;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

/// <summary>
/// Issue a refund tied to a booking. Amount=null means "platform-policy refund" -
/// applies <see cref="RefundOptions.ServiceFeePercent"/> to the captured total.
/// Caller passing an explicit Amount overrides the policy (admin manual refund).
/// </summary>
public sealed record RefundForBookingCommand(Guid BookingId, decimal? Amount, string Reason) : IRequest<bool>;

internal sealed class RefundForBookingHandler(
    IStripeGateway stripe,
    IPaymentIntentRepository repo,
    PaymentDbContext db,
    IOptions<RefundOptions> refundOptions,
    ILogger<RefundForBookingHandler> logger)
    : IRequestHandler<RefundForBookingCommand, bool>
{
    public async Task<bool> Handle(RefundForBookingCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning("Stripe not configured; refund is a no-op for booking {BookingId}.", cmd.BookingId);
            return false;
        }
        var pi = await repo.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (pi is null)
        {
            return false; // No payment was taken; nothing to refund.
        }
        if (pi.Status != PaymentStatus.Succeeded && pi.Status != PaymentStatus.RequiresCapture)
        {
            // Uncaptured PI - cancel rather than refund. No fee retention (nothing was charged).
            var cancelled = await stripe.CancelPaymentIntentAsync(pi.StripePaymentIntentId, cancellationToken);
            pi.UpdateStatus(cancelled.Status, cancelled.ChargeId);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var refundAmount = cmd.Amount ?? ComputeRefundAmount(pi.Amount, refundOptions.Value.ServiceFeePercent);
        logger.LogInformation(
            "Refunding {RefundAmount} of {Captured} for booking {BookingId} (fee={FeePct}%, explicit={ExplicitAmount})",
            refundAmount, pi.Amount, cmd.BookingId, refundOptions.Value.ServiceFeePercent, cmd.Amount);

        var refund = await stripe.RefundAsync(
            pi.StripePaymentIntentId, refundAmount,
            idempotencyKey: $"booking:{cmd.BookingId:N}:refund",
            reason: cmd.Reason,
            cancellationToken: cancellationToken);
        pi.AddRefund(refund.Id, refund.Amount, cmd.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static decimal ComputeRefundAmount(decimal captured, decimal serviceFeePct)
    {
        var pct = Math.Clamp(serviceFeePct, 0m, 100m);
        var refund = captured * (1m - pct / 100m);
        return decimal.Round(refund, 2, MidpointRounding.AwayFromZero);
    }
}
