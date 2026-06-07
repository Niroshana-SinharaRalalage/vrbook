using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Payment.Application.Commands;

/// <summary>
/// Called by the Booking module right after a booking is persisted. Creates a Stripe
/// PaymentIntent with manual capture and stores its id alongside the booking.
/// If Stripe is not configured the handler returns null and Booking continues
/// without payment - this lets staging run without keys.
/// </summary>
public sealed record CreatePaymentIntentForBookingCommand(
    Guid BookingId,
    Money Amount) : IRequest<PaymentIntentDto?>;
