using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Notifications.Domain;

namespace VrBook.Modules.Notifications.Infrastructure.Persistence;

public sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "notifications";
    protected override string Schema => SchemaName;

    public DbSet<NotificationLog> Logs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
    }
}

internal sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_log", NotificationsDbContext.SchemaName);
        builder.HasKey(x => x.Id);

        // OPS.M.3a — tenant_id; nullable forever per OPS_M_3_PLAN §1.6 (guest-
        // facing notifications have no tenant). No 3c flip; no factory empty-guard.
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        builder.HasIndex(x => x.TenantId);

        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(300).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastError).HasColumnName("last_error");

        // Slice 4 C2
        builder.Property(x => x.NotBeforeUtc).HasColumnName("not_before_utc");
        builder.Property(x => x.DispatchStartedAt).HasColumnName("dispatch_started_at");

        builder.HasIndex(x => x.Status).HasDatabaseName("ix_notification_log_status");
        builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt }).HasDatabaseName("ix_notification_log_recipient");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
    }
}
