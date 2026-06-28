using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Booking_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 3-5 + §3.4 (D9 naming).
            // booking.line_items + booking.guests inherit RLS via the
            // parent bookings policy + the FK relationship (per §3.2).
            migrationBuilder.EnableRlsTenantIsolation("booking", "bookings");
            migrationBuilder.EnableRlsTenantIsolation("booking", "booking_holds");
            migrationBuilder.EnableRlsTenantIsolation("booking", "availability_blocks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("booking", "availability_blocks");
            migrationBuilder.DropRlsTenantIsolation("booking", "booking_holds");
            migrationBuilder.DropRlsTenantIsolation("booking", "bookings");
        }
    }
}
