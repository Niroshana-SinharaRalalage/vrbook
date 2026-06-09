using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("sync_runs", SyncDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChannelFeedId).HasColumnName("channel_feed_id").IsRequired();
        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.EventsSeen).HasColumnName("events_seen").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.EventsNew).HasColumnName("events_new").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.EventsUpdated).HasColumnName("events_updated").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.EventsCancelled).HasColumnName("events_cancelled").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Error).HasColumnName("error");

        // Common dashboard: recent runs per feed, newest first.
        builder.HasIndex(x => new { x.ChannelFeedId, x.StartedAt })
            .HasDatabaseName("ix_sync_runs_feed_started");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
