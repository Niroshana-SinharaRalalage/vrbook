using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

public sealed record CapturePaymentIntentForBookingCommand(Guid BookingId) : IRequest<bool>;

internal sealed class CapturePaymentIntentForBookingHandler(
    IStripeGateway stripe,
    IPaymentIntentRepository repo,
    PaymentDbContext db,
    ILogger<CapturePaymentIntentForBookingHandler> logger)
    : IRequestHandler<CapturePaymentIntentForBookingCommand, bool>
{
    public async Task<bool> Handle(CapturePaymentIntentForBookingCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning("Stripe not configured; capture is a no-op for booking {BookingId}.", cmd.BookingId);
            return false;
        }
        var pi = await repo.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (pi is null)
        {
            throw new NotFoundException("PaymentIntent", cmd.BookingId);
        }
        var result = await stripe.CapturePaymentIntentAsync(pi.StripePaymentIntentId, cancellationToken);
        pi.UpdateStatus(result.Status, result.ChargeId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
