using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

internal sealed class MyBookingsHandler(
    ICurrentUser currentUser,
    IGuestTenantResolver guestTenant,
    IBookingRepository bookings) : IRequestHandler<MyBookingsQuery, PagedResult<BookingSummaryDto>>
{
    public async Task<PagedResult<BookingSummaryDto>> Handle(MyBookingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }
        var limit = Math.Clamp(request.Limit, 1, 100);
        var skip = 0;
        if (!string.IsNullOrWhiteSpace(request.Cursor) && int.TryParse(request.Cursor, out var s) && s > 0)
        {
            skip = s;
        }

        // Slice OPS.M.9.1 F6d — closes audit #11 (MyBookings sub-path).
        // Per OPS.M.9.1 §1.4 (architect-prescribed): MyBookings has no
        // resource id in the URL, so we resolve the DISTINCT set of tenant
        // ids the guest has any bookings under, iterate per-tenant, merge.
        //
        // Pagination caveat (§6.1 risk #2): we fetch (skip+limit) per
        // tenant and apply skip+limit in memory after merging. For
        // guests in a single tenant the result is identical to the
        // pre-fix behavior. For guests in multiple tenants the per-page
        // ordering across tenants is approximate — acceptable for Phase
        // 1.5 (≤3 hosts per guest is the expected cardinality).
        var tenantIds = await guestTenant.ResolveTenantsForGuestUserAsync(
            currentUser.UserId.Value, cancellationToken);
        if (tenantIds.Count == 0)
        {
            return new PagedResult<BookingSummaryDto>(Array.Empty<BookingSummaryDto>(), NextCursor: null, Total: 0);
        }

        var merged = new List<BookingSummaryDto>();
        foreach (var tenantId in tenantIds)
        {
            using var tenantScope = BackgroundTenantScope.Enter(tenantId);
            var slice = await bookings.ListForGuestAsync(
                currentUser.UserId.Value, skip: 0, take: skip + limit, cancellationToken);
            merged.AddRange(slice.Select(b => b.ToSummary()));
        }

        // ListForGuestAsync orders by CreatedAt DESC; preserve that across
        // the merged set, then apply the final skip+take.
        var items = merged
            .OrderByDescending(b => b.CreatedAt)
            .Skip(skip)
            .Take(limit)
            .ToArray();
        var nextCursor = items.Length == limit ? (skip + items.Length).ToString() : null;
        return new PagedResult<BookingSummaryDto>(items, nextCursor, items.Length);
    }
}
