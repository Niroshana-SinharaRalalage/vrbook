using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Amenities.Commands;

internal sealed class CreateAmenityHandler(CatalogDbContext db)
    : IRequestHandler<CreateAmenityCommand, AmenityDto>
{
    public async Task<AmenityDto> Handle(CreateAmenityCommand cmd, CancellationToken cancellationToken)
    {
        var code = cmd.Code ?? string.Empty;
        var normalizedCode = code.Trim().ToLowerInvariant();
        var duplicate = await db.Amenities.AnyAsync(a => a.Code == normalizedCode, cancellationToken);
        if (duplicate)
        {
            throw new ConflictException($"Amenity code '{normalizedCode}' already exists.");
        }
        var amenity = new Amenity(Guid.NewGuid(), code, cmd.Name, cmd.Icon, cmd.Category);
        db.Amenities.Add(amenity);
        await db.SaveChangesAsync(cancellationToken);
        return amenity.ToDto();
    }
}

internal sealed class UpdateAmenityHandler(CatalogDbContext db)
    : IRequestHandler<UpdateAmenityCommand, AmenityDto>
{
    public async Task<AmenityDto> Handle(UpdateAmenityCommand cmd, CancellationToken cancellationToken)
    {
        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("Amenity", cmd.Id);
        amenity.Update(cmd.Name, cmd.Icon, cmd.Category);
        await db.SaveChangesAsync(cancellationToken);
        return amenity.ToDto();
    }
}

internal sealed class DisableAmenityHandler(CatalogDbContext db)
    : IRequestHandler<DisableAmenityCommand, AmenityDto>
{
    public async Task<AmenityDto> Handle(DisableAmenityCommand cmd, CancellationToken cancellationToken)
    {
        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("Amenity", cmd.Id);
        amenity.Disable();
        await db.SaveChangesAsync(cancellationToken);
        return amenity.ToDto();
    }
}

internal sealed class EnableAmenityHandler(CatalogDbContext db)
    : IRequestHandler<EnableAmenityCommand, AmenityDto>
{
    public async Task<AmenityDto> Handle(EnableAmenityCommand cmd, CancellationToken cancellationToken)
    {
        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("Amenity", cmd.Id);
        amenity.Enable();
        await db.SaveChangesAsync(cancellationToken);
        return amenity.ToDto();
    }
}

internal sealed class DeleteAmenityHandler(CatalogDbContext db)
    : IRequestHandler<DeleteAmenityCommand>
{
    public async Task Handle(DeleteAmenityCommand cmd, CancellationToken cancellationToken)
    {
        var amenity = await db.Amenities.FirstOrDefaultAsync(a => a.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("Amenity", cmd.Id);

        // Count properties currently attached to this amenity via the join table.
        var usage = await db.Set<Dictionary<string, object>>("property_amenities")
            .CountAsync(j => (Guid)j["amenity_id"] == cmd.Id, cancellationToken);
        if (usage > 0)
        {
            throw new ConflictException(
                $"Cannot delete amenity '{amenity.Code}' — it is attached to {usage} propert{(usage == 1 ? "y" : "ies")}. Disable it instead, or detach it from those properties first.");
        }
        db.Amenities.Remove(amenity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
