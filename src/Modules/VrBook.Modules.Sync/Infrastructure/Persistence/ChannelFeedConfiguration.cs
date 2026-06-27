using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

internal sealed class ChannelFeedConfiguration : IEntityTypeConfiguration<ChannelFeed>
{
    public void Configure(EntityTypeBuilder<ChannelFeed> builder)
    {
        builder.ToTable("channel_feeds", SyncDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3a — tenant_id, nullable until 3c; cross-schema FK in migration.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>().IsRequired();
        builder.Property(x => x.InboundUrl).HasColumnName("inbound_url").HasMaxLength(1024).IsRequired();
        builder.Property(x => x.OutboundToken).HasColumnName("outbound_token").HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.OutboundToken).IsUnique();

        builder.Property(x => x.PollIntervalMinutes).HasColumnName("poll_interval_minutes").IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();

        builder.Property(x => x.LastSuccessAt).HasColumnName("last_success_at");
        builder.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at");
        builder.Property(x => x.LastError).HasColumnName("last_error");
        builder.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ETag).HasColumnName("etag").HasMaxLength(256);
        builder.Property(x => x.LastModifiedAt).HasColumnName("last_modified_at");

        // Audit + soft-delete columns owned by AggregateRoot
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        // Workers scan "due for poll" by ordering on last_attempt_at — index it.
        builder.HasIndex(x => new { x.IsEnabled, x.LastAttemptAt })
            .HasDatabaseName("ix_channel_feeds_due_for_poll");
        builder.HasIndex(x => x.PropertyId);
    }
}
