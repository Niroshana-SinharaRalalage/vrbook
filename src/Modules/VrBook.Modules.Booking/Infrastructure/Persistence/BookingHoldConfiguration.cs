using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Booking.Domain;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingHoldConfiguration : IEntityTypeConfiguration<BookingHold>
{
    public void Configure(EntityTypeBuilder<BookingHold> builder)
    {
        builder.ToTable("booking_holds", BookingDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3a — tenant_id, nullable until 3c.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.Checkin).HasColumnName("checkin").IsRequired();
        builder.Property(x => x.Checkout).HasColumnName("checkout").IsRequired();
        builder.Property(x => x.Guests).HasColumnName("guests").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(x => x.ReleasedAt).HasColumnName("released_at");

        // Fast lookup for the cleanup pass + audit queries.
        builder.HasIndex(x => new { x.PropertyId, x.ExpiresAt })
            .HasDatabaseName("ix_booking_holds_property_expires");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_booking_holds_status");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
