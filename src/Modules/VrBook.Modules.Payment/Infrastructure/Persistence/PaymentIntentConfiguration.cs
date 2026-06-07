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
        b.Property(x => x.StripeEventId).HasColumnName("stripe_event_id").HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.StripeEventId).IsUnique();
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
        b.Property(x => x.ReceivedAt).HasColumnName("received_at");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
    }
}
