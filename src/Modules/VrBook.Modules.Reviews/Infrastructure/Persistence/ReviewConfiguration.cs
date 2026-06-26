using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Reviews.Domain;

namespace VrBook.Modules.Reviews.Infrastructure.Persistence;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.ToTable("reviews", ReviewsDbContext.SchemaName);
        b.HasKey(x => x.Id);

        // OPS.M.3a — tenant_id column; nullable until 3c. Cross-schema FK to
        // identity.tenants("Id") declared via raw SQL in the migration body.
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.HasIndex(x => x.BookingId).IsUnique();
        b.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        b.HasIndex(x => x.PropertyId);
        b.Property(x => x.GuestUserId).HasColumnName("guest_user_id").IsRequired();
        b.HasIndex(x => x.GuestUserId);
        b.Property(x => x.GuestDisplayName).HasColumnName("guest_display_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Rating).HasColumnName("rating").IsRequired();
        b.Property(x => x.Body).HasColumnName("body").HasColumnType("text").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.PublishedAt).HasColumnName("published_at");
        b.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("text");
        b.Property(x => x.ResponseAt).HasColumnName("response_at");

        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.RowVersion).HasColumnName("row_version");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
