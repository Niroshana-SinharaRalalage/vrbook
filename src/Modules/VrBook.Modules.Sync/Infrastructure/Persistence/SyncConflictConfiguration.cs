using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

internal sealed class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("sync_conflicts", SyncDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.ExternalReservationId).HasColumnName("external_reservation_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.Resolution).HasColumnName("resolution").HasConversion<int>().IsRequired();
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes");
        builder.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        // Each (booking, external_reservation) pair only conflicts ONCE — workflows
        // re-running on the same pair should update, not insert.
        builder.HasIndex(x => new { x.BookingId, x.ExternalReservationId })
            .IsUnique()
            .HasDatabaseName("ix_sync_conflicts_booking_external");

        // Dashboard scan: unresolved conflicts per property.
        builder.HasIndex(x => new { x.PropertyId, x.Resolution })
            .HasDatabaseName("ix_sync_conflicts_property_resolution");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
