using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Image attached to a property. Lives in Blob Storage; we store the blob path
/// (relative to the property-images container) and the API rewrites to a SAS URL
/// on read.
/// </summary>
public sealed class PropertyImage : Entity
{
    public Guid PropertyId { get; private set; }
    public string BlobPath { get; private set; } = default!;
    public string? Caption { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    private PropertyImage() { } // EF

    internal PropertyImage(Guid propertyId, string blobPath, string? caption, int sortOrder, bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
        Id = Guid.NewGuid();
        PropertyId = propertyId;
        BlobPath = blobPath.Trim();
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        SortOrder = sortOrder;
        IsPrimary = isPrimary;
    }

    internal void Promote(int sortOrder, bool isPrimary)
    {
        SortOrder = sortOrder;
        IsPrimary = isPrimary;
    }

    internal void UpdateCaption(string? caption) => Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
}
