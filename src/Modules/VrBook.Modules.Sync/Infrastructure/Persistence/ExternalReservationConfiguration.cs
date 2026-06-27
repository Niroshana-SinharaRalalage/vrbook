using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

internal sealed class ExternalReservationConfiguration : IEntityTypeConfiguration<ExternalReservation>
{
    public void Configure(EntityTypeBuilder<ExternalReservation> builder)
    {
        builder.ToTable("external_reservations", SyncDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3a — denorm tenant_id, nullable until 3c.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.ChannelFeedId).HasColumnName("channel_feed_id").IsRequired();
        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.ICalUid).HasColumnName("ical_uid").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Checkin).HasColumnName("checkin").IsRequired();
        builder.Property(x => x.Checkout).HasColumnName("checkout").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(512);
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("text").IsRequired();
        builder.Property(x => x.ImportedAt).HasColumnName("imported_at").IsRequired();
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");

        // Upsert key: (channel_feed_id, ical_uid).
        builder.HasIndex(x => new { x.ChannelFeedId, x.ICalUid })
            .IsUnique()
            .HasDatabaseName("ix_external_reservations_feed_uid");

        // Overlap query target — scan active rows for a property in a date range.
        builder.HasIndex(x => new { x.PropertyId, x.CancelledAt, x.Checkin, x.Checkout })
            .HasDatabaseName("ix_external_reservations_overlap");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
