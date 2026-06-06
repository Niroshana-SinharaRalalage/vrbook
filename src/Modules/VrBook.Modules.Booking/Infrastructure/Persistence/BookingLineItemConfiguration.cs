using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Booking.Domain;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingLineItemConfiguration : IEntityTypeConfiguration<BookingLineItem>
{
    public void Configure(EntityTypeBuilder<BookingLineItem> b)
    {
        b.ToTable("booking_line_items", BookingDbContext.SchemaName);
        b.HasKey(x => x.Id);
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
        b.Property(x => x.Label).HasColumnName("label").HasMaxLength(120).IsRequired();
        b.Property(x => x.Quantity).HasColumnName("quantity");
        b.Property(x => x.UnitAmount).HasColumnName("unit_amount").HasColumnType("numeric(12,2)");
        b.Property(x => x.LineTotal).HasColumnName("line_total").HasColumnType("numeric(12,2)");
        b.HasIndex(x => x.BookingId);
    }
}
