using MediatR;
using Microsoft.EntityFrameworkCore;
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

        // OPS.M.3 — derive tenant via cross-schema lookup; default-tenant fallback
        // until Catalog/Booking 3b backfills.
        var tenantId = await ResolveTenantIdAsync(db, cmd.BookingId, cancellationToken);

        var pi = PaymentIntent.Create(
            tenantId,
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

#pragma warning disable EF1002
    private static async Task<Guid> ResolveTenantIdAsync(PaymentDbContext db, Guid bookingId, CancellationToken ct)
    {
        // Cross-schema lookup booking → property → tenant. Raw SQL with controlled
        // GUID is safe; suppression matches the TenantClaimWiringTests test-seed
        // pattern. After Booking 3c lands the lookup will be a normal load.
        var raw = await db.Database
            .SqlQueryRaw<Guid?>(
                $"SELECT p.tenant_id AS \"Value\" FROM booking.bookings b JOIN catalog.properties p ON b.property_id = p.\"Id\" WHERE b.\"Id\" = '{bookingId}'")
            .FirstOrDefaultAsync(ct);
        return raw ?? new Guid("00000000-0000-0000-0000-000000000001");
    }
#pragma warning restore EF1002

    private static PaymentIntentDto Map(PaymentIntent pi) => new(
        Id: pi.Id,
        BookingId: pi.BookingId,
        StripePaymentIntentId: pi.StripePaymentIntentId,
        Amount: new Money(pi.Amount, pi.Currency),
        Status: pi.Status,
        CaptureMethod: pi.CaptureMethod,
        CreatedAt: pi.CreatedAt);
}
