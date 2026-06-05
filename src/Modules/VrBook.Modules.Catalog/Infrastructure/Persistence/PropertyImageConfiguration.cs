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
        b.Property(i => i.PropertyId).HasColumnName("property_id").IsRequired();
        b.Property(i => i.BlobPath).HasColumnName("blob_path").HasMaxLength(400).IsRequired();
        b.Property(i => i.Caption).HasColumnName("caption").HasMaxLength(400);
        b.Property(i => i.SortOrder).HasColumnName("sort_order");
        b.Property(i => i.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        b.HasIndex(i => new { i.PropertyId, i.SortOrder });
    }
}
