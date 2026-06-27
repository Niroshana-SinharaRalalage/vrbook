using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3b_Booking_TenantIdBackfill : Migration
    {
        // OPS.M.3b — backfill bookings + booking_holds + availability_blocks.
        // availability_blocks added its tenant_id back in Slice 3 (forward-compat
        // placeholder per REPLAN.md §10.1) and still has nullable rows.

        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE booking.bookings
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE booking.booking_holds
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE booking.availability_blocks
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE booking.availability_blocks
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");

            migrationBuilder.Sql($@"
                UPDATE booking.booking_holds
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");

            migrationBuilder.Sql($@"
                UPDATE booking.bookings
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
