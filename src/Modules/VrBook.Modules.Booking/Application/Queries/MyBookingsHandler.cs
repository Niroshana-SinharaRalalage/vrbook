using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

internal sealed class MyBookingsHandler(
    ICurrentUser currentUser,
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
        var page = await bookings.ListForGuestAsync(currentUser.UserId.Value, skip, limit, cancellationToken);
        var items = page.Select(b => b.ToSummary()).ToArray();
        var nextCursor = items.Length == limit ? (skip + items.Length).ToString() : null;
        return new PagedResult<BookingSummaryDto>(items, nextCursor, items.Length);
    }
}
