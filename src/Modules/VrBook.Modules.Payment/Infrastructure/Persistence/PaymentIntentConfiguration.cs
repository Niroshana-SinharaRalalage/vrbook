using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Payment.Domain;

namespace VrBook.Modules.Payment.Infrastructure.Persistence;

internal sealed class PaymentIntentConfiguration : IEntityTypeConfiguration<PaymentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentIntent> b)
    {
        b.ToTable("payment_intents", PaymentDbContext.SchemaName);
        b.HasKey(x => x.Id);
        // OPS.M.3c — NOT NULL after Wave B backfill.
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.HasIndex(x => x.BookingId).IsUnique();
        b.Property(x => x.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id").HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.StripePaymentIntentId).IsUnique();
        b.Property(x => x.StripeChargeId).HasColumnName("stripe_charge_id").HasMaxLength(120);
        b.Property(x => x.ClientSecret).HasColumnName("client_secret").HasMaxLength(300).IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        b.Property(x => x.CaptureMethod).HasColumnName("capture_method").HasMaxLength(20).IsRequired();
        b.Property(x => x.AuthorizedAt).HasColumnName("authorized_at");
        b.Property(x => x.CapturedAt).HasColumnName("captured_at");
        b.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);

        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.RowVersion).HasColumnName("row_version");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        b.HasMany(x => x.Refunds).WithOne().HasForeignKey(r => r.PaymentIntentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.ToTable("refunds", PaymentDbContext.SchemaName);
        b.HasKey(x => x.Id);
        // OPS.M.3c — denorm tenant_id NOT NULL after Wave B backfill.
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.PaymentIntentId).HasColumnName("payment_intent_id").IsRequired();
        b.Property(x => x.StripeRefundId).HasColumnName("stripe_refund_id").HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.StripeRefundId).IsUnique();
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200);
        b.HasIndex(x => x.PaymentIntentId);
    }
}

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> b)
    {
        b.ToTable("webhook_events", PaymentDbContext.SchemaName);
        b.HasKey(x => x.Id);

        // OPS.M.3a — tenant_id, nullable per §1.4 (populated by OPS.M.5's
        // webhook-routing path; platform-level events may stay null forever).
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired(false);
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.StripeEventId).HasColumnName("stripe_event_id").HasMaxLength(120).IsRequired();

        // OPS.M.5 §3.7 (D7) — composite uniqueness so platform-scope (account=null)
        // + connected-scope (account=acct_…) duplicates of the same evt_… persist
        // as distinct rows. Old single-column unique IX_webhook_events_stripe_event_id
        // is dropped by OpsM5a_Payment_WebhookEvents_StripeAccountId.
        b.Property(x => x.StripeAccountId).HasColumnName("stripe_account_id").HasMaxLength(120);
        b.HasIndex(x => new { x.StripeEventId, x.StripeAccountId })
            .HasDatabaseName("IX_webhook_events_account_event")
            .IsUnique();

        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
        b.Property(x => x.ReceivedAt).HasColumnName("received_at");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
    }
}
