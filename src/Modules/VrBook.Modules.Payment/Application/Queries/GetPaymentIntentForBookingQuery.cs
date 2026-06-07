using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Queries;

/// <summary>
/// Returns the PaymentIntent for a booking plus client_secret + publishable_key so
/// the frontend Stripe Elements component can render. Returns null if Stripe is
/// not configured or no PI exists yet.
/// </summary>
public sealed record GetPaymentIntentForBookingQuery(Guid BookingId) : IRequest<CreatePaymentIntentResponse?>;

internal sealed class GetPaymentIntentForBookingHandler(
    IPaymentIntentRepository repo,
    IStripeGateway stripe) : IRequestHandler<GetPaymentIntentForBookingQuery, CreatePaymentIntentResponse?>
{
    public async Task<CreatePaymentIntentResponse?> Handle(GetPaymentIntentForBookingQuery request, CancellationToken cancellationToken)
    {
        var pi = await repo.GetByBookingIdAsync(request.BookingId, cancellationToken);
        if (pi is null)
        {
            return null;
        }
        var dto = new PaymentIntentDto(
            Id: pi.Id,
            BookingId: pi.BookingId,
            StripePaymentIntentId: pi.StripePaymentIntentId,
            Amount: new Money(pi.Amount, pi.Currency),
            Status: pi.Status,
            CaptureMethod: pi.CaptureMethod,
            CreatedAt: pi.CreatedAt);
        return new CreatePaymentIntentResponse(dto, pi.ClientSecret, stripe.PublishableKey);
    }
}
