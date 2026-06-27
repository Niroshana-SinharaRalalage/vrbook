using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Booking_TenantIdNotNull : Migration
    {
        // OPS.M.3c — raw SQL SET NOT NULL across all 3 booking tables. Snapshot
        // was incrementally regenerated through 2 attempts so EF only detected
        // bookings as a change in this generation; this hand-written migration
        // covers all 3 tables explicitly. No permanent DEFAULT clause.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE booking.bookings ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE booking.booking_holds ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE booking.availability_blocks ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE booking.availability_blocks ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE booking.booking_holds ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE booking.bookings ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
