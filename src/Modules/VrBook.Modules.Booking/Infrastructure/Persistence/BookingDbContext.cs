using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Domain;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

public sealed class BookingDbContext(
    DbContextOptions<BookingDbContext> options,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : BaseDbContext(options, currentUser, clock)
{
    public const string SchemaName = "booking";
    protected override string Schema => SchemaName;

    public DbSet<DomainBooking> Bookings => Set<DomainBooking>();
    public DbSet<BookingLineItem> LineItems => Set<BookingLineItem>();
    public DbSet<BookingGuestEntry> Guests => Set<BookingGuestEntry>();
    public DbSet<BookingHold> BookingHolds => Set<BookingHold>();
    public DbSet<AvailabilityBlock> AvailabilityBlocks => Set<AvailabilityBlock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingDbContext).Assembly);
    }
}
