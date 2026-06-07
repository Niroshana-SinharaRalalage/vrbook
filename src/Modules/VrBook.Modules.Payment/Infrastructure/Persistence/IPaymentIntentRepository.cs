using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Payment.Domain;

namespace VrBook.Modules.Payment.Infrastructure.Persistence;

public interface IPaymentIntentRepository
{
    Task<PaymentIntent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task<PaymentIntent?> GetByStripeIdAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentIntent intent, CancellationToken cancellationToken = default);
}

internal sealed class PaymentIntentRepository(PaymentDbContext db) : IPaymentIntentRepository
{
    public Task<PaymentIntent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PaymentIntents.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<PaymentIntent?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
        db.PaymentIntents.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.BookingId == bookingId, cancellationToken);

    public Task<PaymentIntent?> GetByStripeIdAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default) =>
        db.PaymentIntents.Include(p => p.Refunds).FirstOrDefaultAsync(p => p.StripePaymentIntentId == stripePaymentIntentId, cancellationToken);

    public Task AddAsync(PaymentIntent intent, CancellationToken cancellationToken = default)
    {
        db.PaymentIntents.Add(intent);
        return Task.CompletedTask;
    }
}
