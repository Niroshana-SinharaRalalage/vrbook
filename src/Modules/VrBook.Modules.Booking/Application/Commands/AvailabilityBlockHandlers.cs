using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class CreateAvailabilityBlockHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    BookingDbContext db) : IRequestHandler<CreateAvailabilityBlockCommand, AvailabilityBlockDto>
{
    public async Task<AvailabilityBlockDto> Handle(CreateAvailabilityBlockCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var r = request.Request ?? throw new ArgumentException("Request body required.", nameof(request));

        var property = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        if (property.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the property owner can block dates.");
        }

        if (r.EndDate <= r.StartDate)
        {
            throw new BusinessRuleViolationException(
                "availability_block.dates",
                "End date must be after start date.");
        }

        var overlappingBooking = await db.Bookings
            .AsNoTracking()
            .Where(b => b.PropertyId == request.PropertyId
                && b.Status != BookingStatus.Cancelled
                && b.Status != BookingStatus.Rejected
                && b.Status != BookingStatus.Refunded
                && b.Stay.CheckinDate < r.EndDate
                && r.StartDate < b.Stay.CheckoutDate)
            .Select(b => b.Reference)
            .FirstOrDefaultAsync(cancellationToken);
        if (overlappingBooking is not null)
        {
            throw new ConflictException(
                $"These dates overlap an existing booking ({overlappingBooking}). Cancel or move the booking first.");
        }

        var block = AvailabilityBlock.Create(
            propertyId: request.PropertyId,
            startDate: r.StartDate,
            endDate: r.EndDate,
            reason: r.Reason);

        db.AvailabilityBlocks.Add(block);
        await db.SaveChangesAsync(cancellationToken);

        return new AvailabilityBlockDto(
            block.Id,
            block.PropertyId,
            block.StartDate,
            block.EndDate,
            block.Reason,
            block.CreatedAt);
    }
}

internal sealed class DeleteAvailabilityBlockHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    BookingDbContext db) : IRequestHandler<DeleteAvailabilityBlockCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAvailabilityBlockCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var property = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        if (property.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the property owner can remove blocks.");
        }

        var block = await db.AvailabilityBlocks
            .FirstOrDefaultAsync(x => x.Id == request.BlockId && x.PropertyId == request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("AvailabilityBlock", request.BlockId);

        db.AvailabilityBlocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
