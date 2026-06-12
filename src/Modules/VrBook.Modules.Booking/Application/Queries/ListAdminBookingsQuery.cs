using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

/// <summary>
/// Slice 2 — admin/owner bookings list.
///   * Admins see all bookings.
///   * Owners see bookings on properties they own (via IPropertyOwnerLookup).
/// Optional status filter for the dashboard's "pending tentative" pill and the
/// admin queue's status tabs.
/// </summary>
public sealed record ListAdminBookingsQuery(BookingStatus? Status) : IRequest<IReadOnlyList<AdminBookingSummaryDto>>;

internal sealed class ListAdminBookingsHandler(
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup,
    BookingDbContext db) : IRequestHandler<ListAdminBookingsQuery, IReadOnlyList<AdminBookingSummaryDto>>
{
    public async Task<IReadOnlyList<AdminBookingSummaryDto>> Handle(
        ListAdminBookingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        IQueryable<Domain.Booking> q = db.Bookings.AsNoTracking();

        if (!currentUser.IsAdmin)
        {
            var ownedPropertyIds = await ownerLookup.ListPropertyIdsOwnedByAsync(currentUser.UserId.Value, cancellationToken);
            if (ownedPropertyIds.Count == 0)
            {
                return Array.Empty<AdminBookingSummaryDto>();
            }
            var idSet = ownedPropertyIds.ToHashSet();
            q = q.Where(b => idSet.Contains(b.PropertyId));
        }

        if (request.Status is { } status)
        {
            q = q.Where(b => b.Status == status);
        }

        // Tentative-first for the queue. After that newest first.
        var rows = await q
            .OrderBy(b => b.Status == BookingStatus.Tentative ? 0 : 1)
            .ThenByDescending(b => b.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return rows.Select(b => new AdminBookingSummaryDto(
            b.Id,
            b.Reference,
            b.PropertyId,
            b.PropertyTitle,
            b.GuestUserId,
            b.GuestDisplayName,
            b.Stay.CheckinDate,
            b.Stay.CheckoutDate,
            b.GuestCount,
            b.Status,
            b.Total,
            b.Currency,
            b.TentativeUntil,
            b.CreatedAt))
            .ToArray();
    }
}
