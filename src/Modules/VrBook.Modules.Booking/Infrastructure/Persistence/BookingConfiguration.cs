using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<DomainBooking>
{
    public void Configure(EntityTypeBuilder<DomainBooking> b)
    {
        b.ToTable("bookings", BookingDbContext.SchemaName);
        b.HasKey(x => x.Id);

        // OPS.M.3c — NOT NULL after Wave B backfill.
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        b.HasIndex(x => x.TenantId);

        b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Reference).IsUnique();
        b.Property(x => x.PropertyId).HasColumnName("property_id").IsRequired();
        b.HasIndex(x => x.PropertyId);
        b.Property(x => x.PropertyTitle).HasColumnName("property_title").HasMaxLength(200).IsRequired();
        b.Property(x => x.GuestUserId).HasColumnName("guest_user_id").IsRequired();
        b.HasIndex(x => x.GuestUserId);
        b.Property(x => x.GuestDisplayName).HasColumnName("guest_display_name").HasMaxLength(200).IsRequired();

        b.OwnsOne(x => x.Stay, s =>
        {
            s.Property(p => p.CheckinDate).HasColumnName("checkin_date").IsRequired();
            s.Property(p => p.CheckoutDate).HasColumnName("checkout_date").IsRequired();
        });

        b.Property(x => x.GuestCount).HasColumnName("guest_count").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Status);
        b.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        b.Property(x => x.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(x => x.Fees).HasColumnName("fees").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(x => x.Taxes).HasColumnName("taxes").HasColumnType("numeric(12,2)").IsRequired();
        b.Property(x => x.Discount).HasColumnName("discount").HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        b.Property(x => x.Total).HasColumnName("total").HasColumnType("numeric(12,2)").IsRequired();

        b.Property(x => x.CancellationPolicy).HasColumnName("cancellation_policy")
            .HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.TentativeUntil).HasColumnName("tentative_until");
        b.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        b.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        b.Property(x => x.CheckedInAt).HasColumnName("checked_in_at");
        b.Property(x => x.CheckedOutAt).HasColumnName("checked_out_at");
        b.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(500);
        b.Property(x => x.SpecialRequests).HasColumnName("special_requests").HasMaxLength(2000);

        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.RowVersion).HasColumnName("row_version");
        b.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        b.HasMany(x => x.LineItems)
            .WithOne()
            .HasForeignKey(li => li.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Guests)
            .WithOne()
            .HasForeignKey(g => g.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
