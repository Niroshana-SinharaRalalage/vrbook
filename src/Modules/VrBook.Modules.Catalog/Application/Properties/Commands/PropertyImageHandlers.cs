using MediatR;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Options;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

internal sealed class UploadPropertyImageHandler(
    IPropertyRepository properties,
    CatalogDbContext db,
    IBlobStorage blobs,
    IPropertyImageUrlBuilder urls,
    IOptions<BlobOptions> blobOptions,
    IOptions<CatalogImageOptions> imageOptions) : IRequestHandler<UploadPropertyImageCommand, PropertyImageDto>
{
    public async Task<PropertyImageDto> Handle(UploadPropertyImageCommand request, CancellationToken cancellationToken)
    {
        var opts = imageOptions.Value;

        // Validate BEFORE touching blob storage — AC: a rejected upload leaves no
        // partial blob behind.
        if (!opts.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new BusinessRuleViolationException(
                "catalog.image_unsupported_type",
                $"Unsupported image type '{request.ContentType}'. Allowed: {string.Join(", ", opts.AllowedContentTypes)}.");
        }
        if (request.SizeBytes <= 0 || request.SizeBytes > opts.MaxSizeBytes)
        {
            throw new BusinessRuleViolationException(
                "catalog.image_too_large",
                $"Image must be between 1 byte and {opts.MaxSizeMb} MB (was {request.SizeBytes} bytes).");
        }

        var property = await properties.GetByIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);
        if (property.TenantId != request.TenantId)
        {
            throw new CrossTenantAccessException(property.TenantId, request.TenantId);
        }

        var imageId = Guid.NewGuid();
        var container = blobOptions.Value.PropertyImagesContainer;
        var blobPath = $"{request.TenantId}/{request.PropertyId}/{imageId}{ExtensionFor(request.ContentType)}";

        await blobs.UploadAsync(container, blobPath, request.Content, request.ContentType, cancellationToken);
        try
        {
            var img = property.AddImage(imageId, blobPath, request.Caption);
            await db.SaveChangesAsync(cancellationToken);
            return new PropertyImageDto(img.Id, urls.ToUrl(img.BlobPath), img.Caption, img.SortOrder, img.IsPrimary);
        }
        catch
        {
            // Compensate — never orphan a blob if the row write fails.
            await blobs.DeleteAsync(container, blobPath, CancellationToken.None);
            throw;
        }
    }

    private static string ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin",
    };
}

internal sealed class ReorderPropertyImagesHandler(
    IPropertyRepository properties,
    CatalogDbContext db,
    IPropertyImageUrlBuilder urls) : IRequestHandler<ReorderPropertyImagesCommand, IReadOnlyList<PropertyImageDto>>
{
    public async Task<IReadOnlyList<PropertyImageDto>> Handle(ReorderPropertyImagesCommand request, CancellationToken cancellationToken)
    {
        var property = await properties.GetByIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);
        if (property.TenantId != request.TenantId)
        {
            throw new CrossTenantAccessException(property.TenantId, request.TenantId);
        }

        property.ReorderImages(request.OrderedImageIds);
        await db.SaveChangesAsync(cancellationToken);

        return property.Images
            .OrderBy(i => i.SortOrder)
            .Select(i => new PropertyImageDto(i.Id, urls.ToUrl(i.BlobPath), i.Caption, i.SortOrder, i.IsPrimary))
            .ToArray();
    }
}

internal sealed class DeletePropertyImageHandler(
    IPropertyRepository properties,
    CatalogDbContext db,
    IBlobStorage blobs,
    IOptions<BlobOptions> blobOptions) : IRequestHandler<DeletePropertyImageCommand>
{
    public async Task Handle(DeletePropertyImageCommand request, CancellationToken cancellationToken)
    {
        var property = await properties.GetByIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);
        if (property.TenantId != request.TenantId)
        {
            throw new CrossTenantAccessException(property.TenantId, request.TenantId);
        }

        var blobPath = property.RemoveImage(request.ImageId); // throws NotFound if the image is absent
        await db.SaveChangesAsync(cancellationToken);
        // Delete the blob AFTER the row commit — no orphan on a failed SaveChanges.
        await blobs.DeleteAsync(blobOptions.Value.PropertyImagesContainer, blobPath, CancellationToken.None);
    }
}
