using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Payment.Application.Commands;

/// <summary>
/// Called by the Booking module right after a booking is persisted. Creates a Stripe
/// PaymentIntent with manual capture and stores its id alongside the booking.
/// If Stripe is not configured the handler returns null and Booking continues
/// without payment - this lets staging run without keys.
///
/// <para>OPS.M.5 — <c>TenantId</c> is passed by the Booking caller (which already
/// has <c>Booking.TenantId</c> after OPS.M.3 Wave C). The handler uses it to
/// look up the tenant's Stripe Connect routing context.</para>
/// </summary>
public sealed record CreatePaymentIntentForBookingCommand(
    Guid BookingId,
    Money Amount,
    Guid TenantId) : IRequest<PaymentIntentDto?>;
