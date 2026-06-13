using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants", IdentityDbContext.SchemaName);
        b.HasKey(t => t.Id);

        b.Property(t => t.Slug).HasColumnName("slug").HasMaxLength(64).IsRequired();
        b.HasIndex(t => t.Slug).IsUnique();

        b.Property(t => t.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();

        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.CreatedBy).HasColumnName("created_by");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        b.Property(t => t.UpdatedBy).HasColumnName("updated_by");
        b.Property(t => t.RowVersion).HasColumnName("row_version");
        b.Property(t => t.DeletedAt).HasColumnName("deleted_at");
        b.Property(t => t.DeletedBy).HasColumnName("deleted_by");
    }
}
