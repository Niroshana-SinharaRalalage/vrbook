using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Messaging.Domain;

namespace VrBook.Modules.Messaging.Infrastructure.Persistence;

public sealed class MessagingDbContext(
    DbContextOptions<MessagingDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "messaging";
    protected override string Schema => SchemaName;

    public DbSet<MessageThread> Threads => Set<MessageThread>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
    }
}

internal sealed class MessageThreadConfiguration : IEntityTypeConfiguration<MessageThread>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<MessageThread> builder)
    {
        builder.ToTable("threads", MessagingDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3c — NOT NULL after Wave B backfill.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        builder.HasIndex(x => x.BookingId).IsUnique();
        builder.Property(x => x.BookingReference).HasColumnName("booking_reference").HasMaxLength(40).IsRequired();
        builder.Property(x => x.GuestUserId).HasColumnName("guest_user_id").IsRequired();
        builder.Property(x => x.GuestDisplayName).HasColumnName("guest_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        builder.Property(x => x.OwnerDisplayName).HasColumnName("owner_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.LastMessageAt).HasColumnName("last_message_at");
        builder.Property(x => x.LastMessagePreview).HasColumnName("last_message_preview").HasMaxLength(120);

        builder.HasIndex(x => new { x.GuestUserId, x.LastMessageAt }).HasDatabaseName("ix_threads_guest_last");
        builder.HasIndex(x => new { x.OwnerUserId, x.LastMessageAt }).HasDatabaseName("ix_threads_owner_last");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", MessagingDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3c — denorm tenant_id NOT NULL after Wave B backfill.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.ThreadId).HasColumnName("thread_id").IsRequired();
        builder.Property(x => x.SenderUserId).HasColumnName("sender_user_id").IsRequired();
        builder.Property(x => x.SenderDisplayName).HasColumnName("sender_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SentAt).HasColumnName("sent_at").IsRequired();
        builder.Property(x => x.ReadAt).HasColumnName("read_at");

        builder.HasIndex(x => new { x.ThreadId, x.SentAt }).HasDatabaseName("ix_messages_thread_sent");
        builder.HasIndex(x => new { x.RecipientUserId, x.ReadAt }).HasDatabaseName("ix_messages_unread");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
