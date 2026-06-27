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

        // OPS.M.3a — denorm tenant_id, nullable until 3c.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(x => x.ExternalReservationId).HasColumnName("external_reservation_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.Resolution).HasColumnName("resolution").HasConversion<int>().IsRequired();
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes");
        builder.Property(x => x.DetectedAt).HasColumnName("detected_at").IsRequired();
        builder.Property(x => x.ResolvedAt).HasColumnName("resolved_at");

        // Each (booking, external_reservation) pair only conflicts ONCE — workflows
        // re-running on the same pair should update, not insert. Plain non-unique
        // index (kept for the dedupe lookup); the unique partial index below
        // enforces "at most one PENDING conflict per pair" at the storage layer.
        builder.HasIndex(x => new { x.BookingId, x.ExternalReservationId })
            .HasDatabaseName("ix_sync_conflicts_booking_external");

        // A6 stage 5: partial unique index. Defends against the in-process race when
        // both ExternalReservationImported and BookingConfirmed handlers fire for
        // the same (booking, external_reservation) at the same time. Only applies to
        // unresolved rows so a resolved-then-recurring conflict can create a fresh row.
        builder.HasIndex(x => new { x.BookingId, x.ExternalReservationId })
            .IsUnique()
            .HasFilter("resolved_at IS NULL")
            .HasDatabaseName("ux_sync_conflicts_booking_external_pending");

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
