using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Amenities.Commands;

/// <summary>Admin: create a new amenity (A2.2).</summary>
public sealed record CreateAmenityCommand(string Code, string Name, string? Icon, string Category)
    : IRequest<AmenityDto>;

/// <summary>Admin: edit name / icon / category. Code is immutable to keep FK + cache stable.</summary>
public sealed record UpdateAmenityCommand(Guid Id, string Name, string? Icon, string Category)
    : IRequest<AmenityDto>;

/// <summary>Admin: hide from public catalog. Existing property attachments remain.</summary>
public sealed record DisableAmenityCommand(Guid Id) : IRequest<AmenityDto>;

/// <summary>Admin: restore to public catalog.</summary>
public sealed record EnableAmenityCommand(Guid Id) : IRequest<AmenityDto>;

/// <summary>
/// Admin: hard-delete an amenity. Refused with 409 if any property is still attached
/// (would break the property_amenities FK). For "hide from catalog but keep attachments
/// valid", use <see cref="DisableAmenityCommand"/> instead.
/// </summary>
public sealed record DeleteAmenityCommand(Guid Id) : IRequest;
