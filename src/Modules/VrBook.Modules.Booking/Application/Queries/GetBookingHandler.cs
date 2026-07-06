using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

internal sealed class GetBookingHandler(
    ICurrentUser currentUser,
    IGuestTenantResolver guestTenant,
    IBookingRepository bookings) : IRequestHandler<GetBookingQuery, BookingDto>
{
    public async Task<BookingDto> Handle(GetBookingQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        // Slice OPS.M.9.1 F6d — closes audit #11 (Get sub-path). Guest
        // persona has no ICurrentUser.TenantId; without a scope, RLS
        // denies the booking lookup. Resolve from BookingId (via the
        // resolver's own bypass), then run the lookup under the scope.
        var tenantId = await guestTenant.ResolveFromBookingIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Booking", request.Id);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

        var booking = await bookings.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Booking", request.Id);

        // A4 v1 authZ: the guest who booked OR any tenant_admin of the
        // booking's tenant. Owner visibility lands when we add the Catalog
        // cross-check (next iteration).
        //
        // Slice OPS.M.15.5 — legacy IsAdmin reader replaced with tenant-
        // scoped role check.
        var isTenantAdmin = currentUser.HasTenantRole(booking.TenantId, "tenant_admin");
        if (booking.GuestUserId != currentUser.UserId.Value && !isTenantAdmin)
        {
            throw new ForbiddenException("Not allowed to view this booking.");
        }
        return booking.ToDto();
    }
}
