using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Application.Common;

internal static class BookingMapping
{
    public static BookingDto ToDto(this DomainBooking b) =>
        new(
            Id: b.Id,
            Reference: b.Reference,
            PropertyId: b.PropertyId,
            PropertyTitle: b.PropertyTitle,
            GuestUserId: b.GuestUserId,
            GuestDisplayName: b.GuestDisplayName,
            CheckinDate: b.Stay.CheckinDate,
            CheckoutDate: b.Stay.CheckoutDate,
            GuestCount: b.GuestCount,
            Status: b.Status,
            Source: b.Source,
            Totals: new BookingTotalsDto(
                Subtotal: new Money(b.Subtotal, b.Currency),
                Fees: new Money(b.Fees, b.Currency),
                Taxes: new Money(b.Taxes, b.Currency),
                Discount: new Money(b.Discount, b.Currency),
                Total: new Money(b.Total, b.Currency)),
            LineItems: b.LineItems
                .Select(li => new BookingLineItemDto(
                    Label: li.Label,
                    Kind: li.Kind,
                    Quantity: li.Quantity,
                    UnitAmount: new Money(li.UnitAmount, b.Currency),
                    Total: new Money(li.LineTotal, b.Currency)))
                .ToArray(),
            CancellationPolicy: b.CancellationPolicy,
            PaymentIntentId: null,
            TentativeUntil: b.TentativeUntil,
            ConfirmedAt: b.ConfirmedAt,
            CancelledAt: b.CancelledAt,
            CancellationReason: b.CancellationReason,
            LoyaltyTierAtBooking: null,
            LoyaltyDiscountPct: null,
            Guests: b.Guests
                .OrderByDescending(g => g.IsPrimary)
                .Select(g => new BookingGuestDto(g.FullName, g.IsPrimary))
                .ToArray(),
            SpecialRequests: b.SpecialRequests,
            CreatedAt: b.CreatedAt);

    public static BookingSummaryDto ToSummary(this DomainBooking b) =>
        new(
            Id: b.Id,
            Reference: b.Reference,
            PropertyId: b.PropertyId,
            PropertyTitle: b.PropertyTitle,
            CheckinDate: b.Stay.CheckinDate,
            CheckoutDate: b.Stay.CheckoutDate,
            Status: b.Status,
            Source: b.Source,
            Total: new Money(b.Total, b.Currency),
            CreatedAt: b.CreatedAt);
}
