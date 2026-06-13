using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;

namespace VrBook.Modules.Booking.Application.Queries;

public sealed record ListAvailabilityBlocksQuery(
    Guid PropertyId,
    DateOnly? From,
    DateOnly? To) : IRequest<IReadOnlyList<AvailabilityBlockDto>>;

internal sealed class ListAvailabilityBlocksHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    BookingDbContext db) : IRequestHandler<ListAvailabilityBlocksQuery, IReadOnlyList<AvailabilityBlockDto>>
{
    public async Task<IReadOnlyList<AvailabilityBlockDto>> Handle(ListAvailabilityBlocksQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var property = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        if (property.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the property owner can view blocks.");
        }

        var q = db.AvailabilityBlocks
            .AsNoTracking()
            .Where(x => x.PropertyId == request.PropertyId);

        if (request.From is { } from)
        {
            q = q.Where(x => x.EndDate > from);
        }
        if (request.To is { } to)
        {
            q = q.Where(x => x.StartDate < to);
        }

        return await q
            .OrderBy(x => x.StartDate)
            .Select(x => new AvailabilityBlockDto(
                x.Id, x.PropertyId, x.StartDate, x.EndDate, x.Reason, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
