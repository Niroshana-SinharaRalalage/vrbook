using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

/// <summary>
/// Issue a refund tied to a booking. Amount=null means full refund. v1 only does
/// full refunds; cancellation-policy math (50% inside 7 days, etc.) lands in A5.1.
/// </summary>
public sealed record RefundForBookingCommand(Guid BookingId, decimal? Amount, string Reason) : IRequest<bool>;

internal sealed class RefundForBookingHandler(
    IStripeGateway stripe,
    IPaymentIntentRepository repo,
    PaymentDbContext db,
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
            // Cancelling an uncaptured PI is the right move - free the hold instead of refunding.
            var cancelled = await stripe.CancelPaymentIntentAsync(pi.StripePaymentIntentId, cancellationToken);
            pi.UpdateStatus(cancelled.Status, cancelled.ChargeId);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var refund = await stripe.RefundAsync(
            pi.StripePaymentIntentId, cmd.Amount,
            idempotencyKey: $"booking:{cmd.BookingId:N}:refund",
            reason: cmd.Reason,
            cancellationToken: cancellationToken);
        pi.AddRefund(refund.Id, refund.Amount, cmd.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
