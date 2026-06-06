using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VrBook.Modules.Booking.Domain;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingGuestEntryConfiguration : IEntityTypeConfiguration<BookingGuestEntry>
{
    public void Configure(EntityTypeBuilder<BookingGuestEntry> b)
    {
        b.ToTable("booking_guests", BookingDbContext.SchemaName);
        b.HasKey(x => x.Id);
        b.Property(x => x.BookingId).HasColumnName("booking_id").IsRequired();
        b.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        b.HasIndex(x => x.BookingId);
    }
}
