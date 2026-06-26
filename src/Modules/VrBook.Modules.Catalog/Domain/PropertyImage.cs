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

    /// <summary>
    /// Denormalised tenant id (inherits from <c>Property.TenantId</c>). Per
    /// `docs/OPS_M_3_PLAN.md` §1 — denorm lives so OPS.M.9 RLS doesn't have
    /// to join Catalog at every read. Nullable during 3a/3b per the EF
    /// constraint flagged in `Property.TenantId`'s doc.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public string BlobPath { get; private set; } = default!;
    public string? Caption { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    private PropertyImage() { } // EF

    internal PropertyImage(Guid tenantId, Guid propertyId, string blobPath, string? caption, int sortOrder, bool isPrimary)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);
        Id = Guid.NewGuid();
        TenantId = tenantId;
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
