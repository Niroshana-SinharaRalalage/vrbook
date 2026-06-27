using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Application.Commands;

// OPS.M.4 — owner-driven transitions gain ITenantScoped + a server-stamped
// TenantId from currentUser.TenantId at the controller. Per OPS_M_4_PLAN
// section 3.1 + 3.2 the behavior validates currentUser.TenantId == TenantId.
//
// CancelBookingCommand stays UN-marked because it is open to authenticated
// guests (no [Authorize(Roles)] gate on the controller); guests are
// tenant-less per MTOP section 1 so the M.4 gate cannot enforce equality.
// Cross-tenant protection for the guest cancel path lands in Slice OPS.M.9 RLS.

public sealed record CancelBookingCommand(Guid Id, string Reason) : IRequest<BookingDto>;

public sealed record ConfirmBookingCommand(Guid Id, Guid TenantId) : IRequest<BookingDto>, ITenantScoped;

public sealed record RejectBookingCommand(Guid Id, string Reason, Guid TenantId) : IRequest<BookingDto>, ITenantScoped;

public sealed record CheckInBookingCommand(Guid Id, Guid TenantId) : IRequest<BookingDto>, ITenantScoped;

public sealed record CheckOutBookingCommand(Guid Id, Guid TenantId) : IRequest<BookingDto>, ITenantScoped;
