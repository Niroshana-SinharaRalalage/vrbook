using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class PropertyImageConfiguration : IEntityTypeConfiguration<PropertyImage>
{
    public void Configure(EntityTypeBuilder<PropertyImage> b)
    {
        b.ToTable("property_images", CatalogDbContext.SchemaName);
        b.HasKey(i => i.Id);
        // VRB-101 — the id is client-assigned in the handler (it names the blob:
        // {tenantId}/{propertyId}/{imageId}.ext) BEFORE the row is written. Without
        // this, the Guid key is ValueGeneratedOnAdd by convention, so EF's "key is
        // set ⇒ existing entity" heuristic marks a new image added to the already-
        // tracked property graph as Modified → emits an UPDATE for a non-existent row
        // → DbUpdateConcurrencyException (0 rows affected) → 500 on every upload.
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.PropertyId).HasColumnName("property_id").IsRequired();
        b.Property(i => i.BlobPath).HasColumnName("blob_path").HasMaxLength(400).IsRequired();
        b.Property(i => i.Caption).HasColumnName("caption").HasMaxLength(400);
        b.Property(i => i.SortOrder).HasColumnName("sort_order");
        b.Property(i => i.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        b.HasIndex(i => new { i.PropertyId, i.SortOrder });

        // OPS.M.3c — denormalised tenant_id NOT NULL after Wave B. Per
        // OPS_M_3_PLAN.md §1 the denorm lives so OPS.M.9 RLS policies don't
        // have to join catalog.properties at read.
        b.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasIndex(i => i.TenantId);
    }
}
