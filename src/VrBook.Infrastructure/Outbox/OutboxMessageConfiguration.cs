using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace VrBook.Infrastructure.Outbox;

/// <summary>
/// EF mapping for <see cref="OutboxMessage"/>. Schema-agnostic — each module's
/// DbContext applies this configuration so the table lives in its own schema
/// (<c>catalog.outbox_messages</c>, <c>booking.outbox_messages</c>, …).
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.DispatchedAt).HasColumnName("dispatched_at");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error");

        // Index for the relay: scan "not dispatched yet" rows quickly.
        builder.HasIndex(x => x.DispatchedAt).HasDatabaseName("ix_outbox_messages_dispatched_at");
    }
}
