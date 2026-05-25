using VrBook.Contracts.Common;
using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

/// <summary>Full booking detail. See proposal §6.3 for the canonical example.</summary>
public sealed record BookingDto(
    Guid Id,
    string Reference,
    Guid PropertyId,
    string PropertyTitle,
    Guid GuestUserId,
    string GuestDisplayName,
    DateOnly CheckinDate,
    DateOnly CheckoutDate,
    int GuestCount,
    BookingStatus Status,
    BookingSource Source,
    BookingTotalsDto Totals,
    IReadOnlyList<BookingLineItemDto> LineItems,
    CancellationPolicyCode CancellationPolicy,
    Guid? PaymentIntentId,
    DateTimeOffset? TentativeUntil,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    LoyaltyTier? LoyaltyTierAtBooking,
    decimal? LoyaltyDiscountPct,
    IReadOnlyList<BookingGuestDto> Guests,
    string? SpecialRequests,
    DateTimeOffset CreatedAt);

public sealed record BookingSummaryDto(
    Guid Id,
    string Reference,
    Guid PropertyId,
    string PropertyTitle,
    DateOnly CheckinDate,
    DateOnly CheckoutDate,
    BookingStatus Status,
    BookingSource Source,
    Money Total,
    DateTimeOffset CreatedAt);

public sealed record BookingTotalsDto(
    Money Subtotal,
    Money Fees,
    Money Taxes,
    Money Discount,
    Money Total);

public sealed record BookingLineItemDto(
    string Label,
    string Kind,
    int Quantity,
    Money UnitAmount,
    Money Total);

public sealed record BookingGuestDto(
    string FullName,
    bool IsPrimary = false);

public sealed record CreateHoldRequest(
    Guid PropertyId,
    DateOnly Checkin,
    DateOnly Checkout,
    int Guests);

public sealed record HoldDto(
    Guid Id,
    Guid PropertyId,
    DateOnly Checkin,
    DateOnly Checkout,
    DateTimeOffset ExpiresAt);

public sealed record PlaceBookingRequest(
    Guid PropertyId,
    Guid HoldId,
    DateOnly CheckinDate,
    DateOnly CheckoutDate,
    int GuestCount,
    IReadOnlyList<BookingGuestDto> Guests,
    string? SpecialRequests,
    bool AgreedToHouseRules,
    bool ApplyLoyaltyDiscount);

public sealed record PlaceBookingResponse(
    BookingDto Booking,
    PaymentIntentClientReference Payment);

public sealed record PaymentIntentClientReference(
    string ClientSecret,
    string PublishableKey);

public sealed record CancelBookingRequest(string Reason);

public sealed record RejectBookingRequest(string Reason);

public sealed record ManualBookingRequest(
    Guid PropertyId,
    DateOnly CheckinDate,
    DateOnly CheckoutDate,
    int GuestCount,
    string GuestEmail,
    string GuestDisplayName,
    Money? TotalOverride,
    string? Notes);
