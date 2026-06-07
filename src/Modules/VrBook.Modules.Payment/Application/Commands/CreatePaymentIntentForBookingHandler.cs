using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

internal sealed class CreatePaymentIntentForBookingHandler(
    IStripeGateway stripe,
    IPaymentIntentRepository repo,
    PaymentDbContext db,
    ILogger<CreatePaymentIntentForBookingHandler> logger)
    : IRequestHandler<CreatePaymentIntentForBookingCommand, PaymentIntentDto?>
{
    public async Task<PaymentIntentDto?> Handle(CreatePaymentIntentForBookingCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning("Stripe not configured; skipping PaymentIntent creation for booking {BookingId}.", cmd.BookingId);
            return null;
        }

        var existing = await repo.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        var created = await stripe.CreatePaymentIntentAsync(
            cmd.Amount.Amount,
            cmd.Amount.Currency,
            idempotencyKey: $"booking:{cmd.BookingId:N}:pi",
            metadata: new Dictionary<string, string> { ["booking_id"] = cmd.BookingId.ToString("D") },
            cancellationToken: cancellationToken);

        var pi = PaymentIntent.Create(
            cmd.BookingId,
            created.Id,
            created.ClientSecret,
            cmd.Amount.Amount,
            cmd.Amount.Currency,
            captureMethod: "manual",
            initialStatus: created.Status);

        await repo.AddAsync(pi, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Map(pi);
    }

    private static PaymentIntentDto Map(PaymentIntent pi) => new(
        Id: pi.Id,
        BookingId: pi.BookingId,
        StripePaymentIntentId: pi.StripePaymentIntentId,
        Amount: new Money(pi.Amount, pi.Currency),
        Status: pi.Status,
        CaptureMethod: pi.CaptureMethod,
        CreatedAt: pi.CreatedAt);
}
