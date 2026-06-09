using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class AmenityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> b)
    {
        b.ToTable("amenities", CatalogDbContext.SchemaName);
        b.HasKey(a => a.Id);
        b.Property(a => a.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        b.HasIndex(a => a.Code).IsUnique();
        b.Property(a => a.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(a => a.Icon).HasColumnName("icon").HasMaxLength(80);
        b.Property(a => a.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
        b.Property(a => a.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();

        b.Property(a => a.CreatedAt).HasColumnName("created_at");
        b.Property(a => a.CreatedBy).HasColumnName("created_by");
        b.Property(a => a.UpdatedAt).HasColumnName("updated_at");
        b.Property(a => a.UpdatedBy).HasColumnName("updated_by");
        b.Property(a => a.RowVersion).HasColumnName("row_version");
        b.Property(a => a.DeletedAt).HasColumnName("deleted_at");
        b.Property(a => a.DeletedBy).HasColumnName("deleted_by");
    }
}
