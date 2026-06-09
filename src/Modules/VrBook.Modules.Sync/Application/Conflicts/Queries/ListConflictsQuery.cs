using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Sync.Application.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.Conflicts.Queries;

public sealed record ListPendingConflictsQuery : IRequest<IReadOnlyList<SyncConflictDto>>;

internal sealed class ListPendingConflictsHandler(SyncDbContext db)
    : IRequestHandler<ListPendingConflictsQuery, IReadOnlyList<SyncConflictDto>>
{
    public async Task<IReadOnlyList<SyncConflictDto>> Handle(
        ListPendingConflictsQuery request, CancellationToken cancellationToken)
    {
        // Join in-process — the ExternalReservation lives in sync schema (same DbContext),
        // booking summary fields come from the conflict row itself (we don't cross schemas
        // here; admin UI can fetch booking details via the bookings endpoint).
        var conflicts = await db.SyncConflicts
            .Where(c => c.Resolution == VrBook.Contracts.Enums.SyncConflictResolution.Pending)
            .OrderBy(c => c.DetectedAt)
            .ToListAsync(cancellationToken);

        if (conflicts.Count == 0)
        {
            return Array.Empty<SyncConflictDto>();
        }

        var erIds = conflicts.Select(c => c.ExternalReservationId).ToArray();
        var ers = await db.ExternalReservations.Where(r => erIds.Contains(r.Id)).ToListAsync(cancellationToken);
        var erById = ers.ToDictionary(r => r.Id);

        return conflicts
            .Where(c => erById.ContainsKey(c.ExternalReservationId))
            .Select(c => c.ToDto(
                propertyTitle: string.Empty,
                er: erById[c.ExternalReservationId],
                bookingReference: c.BookingId.ToString(),
                bookingCheckin: erById[c.ExternalReservationId].Checkin,
                bookingCheckout: erById[c.ExternalReservationId].Checkout))
            .ToArray();
    }
}
