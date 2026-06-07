using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;
using VrBook.Modules.Payment.Application.Commands;
using VrBook.Modules.Pricing.Application.Quotes.Commands;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class PlaceBookingHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db) : IRequestHandler<PlaceBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(PlaceBookingCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required to place a booking.");
        }
        var r = request.Request ?? throw new ArgumentException("Request body required.", nameof(request));
        if (!r.AgreedToHouseRules)
        {
            throw new BusinessRuleViolationException(
                "booking.house_rules",
                "You must agree to the property's house rules before booking.");
        }

        // Cross-module read into Catalog for property basics.
        var property = await mediator.Send(new GetPropertyByIdQuery(r.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", r.PropertyId);
        if (!property.IsActive)
        {
            throw new BusinessRuleViolationException(
                "booking.property_inactive",
                "This property is not currently accepting bookings.");
        }
        if (property.OwnerUserId == currentUser.UserId.Value)
        {
            throw new BusinessRuleViolationException(
                "booking.self",
                "You can't book your own property.");
        }

        // Availability check (A2.1). Anything not Cancelled / Rejected occupies the
        // calendar. Race window vs SaveChangesAsync is acceptable for v1 - A5 will
        // harden via a transactional pre-charge guard.
        var overlaps = await bookings.FindOverlapsAsync(property.Id, r.CheckinDate, r.CheckoutDate, cancellationToken);
        if (overlaps.Count > 0)
        {
            throw new BusinessRuleViolationException(
                "booking.dates_unavailable",
                "These dates are already booked. Please choose different dates.");
        }

        // Compute the quote via Pricing (in-process MediatR; not Service Bus).
        var quoteReq = new QuoteRequest(r.CheckinDate, r.CheckoutDate, r.GuestCount, r.ApplyLoyaltyDiscount);
        var quote = await mediator.Send(new ComputeQuoteCommand(r.PropertyId, quoteReq), cancellationToken);

        var stay = new Stay(r.CheckinDate, r.CheckoutDate);
        var guestName = currentUser.Email ?? currentUser.B2CObjectId ?? "Guest";

        var lineItems = new List<(string kind, string label, int qty, decimal unit, decimal lineTotal)>();
        foreach (var n in quote.Nightly)
        {
            lineItems.Add(("Nightly", $"Night of {n.Date:yyyy-MM-dd}", 1, n.Amount.Amount, n.Amount.Amount));
        }
        foreach (var f in quote.Fees)
        {
            lineItems.Add((f.Kind.ToString(), f.Label, 1, f.Amount.Amount, f.Amount.Amount));
        }

        var booking = DomainBooking.Place(
            propertyId: property.Id,
            propertyTitle: property.Title,
            guestUserId: currentUser.UserId.Value,
            guestDisplayName: guestName,
            stay: stay,
            guestCount: r.GuestCount,
            currency: quote.Total.Currency,
            subtotal: quote.Subtotal.Amount,
            fees: quote.Fees.Sum(x => x.Amount.Amount),
            taxes: quote.Taxes.Amount,
            total: quote.Total.Amount,
            lineItems: lineItems,
            guests: (r.Guests ?? Array.Empty<BookingGuestDto>())
                .Select(g => (g.FullName, g.IsPrimary)),
            specialRequests: r.SpecialRequests);

        await bookings.AddAsync(booking, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Create the Stripe PaymentIntent (manual capture). No-op when Stripe is
        // unconfigured - the booking still persists, just without a payment path.
        // The guest can complete payment from /bookings/[id] using the client secret.
        await mediator.Send(
            new CreatePaymentIntentForBookingCommand(
                booking.Id,
                new Money(booking.Total, booking.Currency)),
            cancellationToken);

        return booking.ToDto();
    }
}
