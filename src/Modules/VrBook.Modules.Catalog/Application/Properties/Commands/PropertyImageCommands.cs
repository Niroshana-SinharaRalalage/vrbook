using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

// VRB-101 — image management. Each command carries the caller's TenantId
// (stamped by the controller from ICurrentUser.TenantId); TenantAuthorizationBehavior
// gates it, and the handler re-checks the property's tenant for defense in depth.

public sealed record UploadPropertyImageCommand(
    Guid PropertyId,
    Guid TenantId,
    Stream Content,
    string ContentType,
    long SizeBytes,
    string FileName,
    string? Caption) : IRequest<PropertyImageDto>, ITenantScoped;

public sealed record ReorderPropertyImagesCommand(
    Guid PropertyId,
    Guid TenantId,
    IReadOnlyList<Guid> OrderedImageIds) : IRequest<IReadOnlyList<PropertyImageDto>>, ITenantScoped;

public sealed record DeletePropertyImageCommand(
    Guid PropertyId,
    Guid TenantId,
    Guid ImageId) : IRequest, ITenantScoped;
