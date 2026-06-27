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
    IPropertyOwnerLookup propertyOwners,
    BookingDbContext db) : IRequestHandler<CreateAvailabilityBlockCommand, AvailabilityBlockDto>
{
    public async Task<AvailabilityBlockDto> Handle(CreateAvailabilityBlockCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var r = request.Request ?? throw new ArgumentException("Request body required.", nameof(request));

        // OPS.M.4 Step 3 — owner-equality check deleted. TenantAuthorizationBehavior
        // rejects the command if currentUser.TenantId != command.TenantId; the
        // controller stamps TenantId from currentUser.TenantId, so any caller
        // whose tenant does not own the property is rejected at the pipeline.
        // RBAC ("only Owner role can hit this endpoint") is enforced by the
        // controller's [Authorize(Roles="Owner,Admin")] attribute.
        //
        // Property existence is still validated to return 404 if the property is
        // gone (separate concern from authorization).
        _ = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

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

        // OPS.M.3c — derive tenant from the property's owner snapshot. Wave B
        // backfilled all rows, so the cross-schema lookup will always find one.
        var ownerSnapshot = await propertyOwners.GetAsync(request.PropertyId, cancellationToken);
        var blockTenantId = ownerSnapshot!.TenantId;

        var block = AvailabilityBlock.Create(
            tenantId: blockTenantId,
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

        // OPS.M.4 Step 3 — owner-equality check deleted; see CreateAvailabilityBlockHandler above.
        // Property existence is still validated for the 404 contract.
        _ = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        var block = await db.AvailabilityBlocks
            .FirstOrDefaultAsync(x => x.Id == request.BlockId && x.PropertyId == request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("AvailabilityBlock", request.BlockId);

        db.AvailabilityBlocks.Remove(block);
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
