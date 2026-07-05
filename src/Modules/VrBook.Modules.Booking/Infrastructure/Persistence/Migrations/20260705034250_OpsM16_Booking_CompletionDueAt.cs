using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM16_Booking_CompletionDueAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completion_due_at",
                schema: "booking",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "turnover_hours_override",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_completion_due_at",
                schema: "booking",
                table: "bookings",
                column: "completion_due_at",
                filter: "status = 'CheckedOut' AND deleted_at IS NULL");

            // Slice OPS.M.16 backfill — every existing CheckedOut booking gets a
            // completion_due_at snapshot of checked_out_at + 24h (the pre-M.16
            // hardcoded delay). Backfill matches OLD sweep behavior exactly so
            // in-flight bookings don't shift under the rolling deploy window.
            //
            // No cross-schema JOIN to catalog.properties on purpose:
            // - keeps the migration same-schema (no IF EXISTS trap needed);
            // - honors §2.2 snapshot semantics — post-deploy property config
            //   changes only bind on NEW check-outs.
            migrationBuilder.Sql(@"
                UPDATE booking.bookings
                SET completion_due_at = checked_out_at + INTERVAL '24 hours'
                WHERE status = 'CheckedOut'
                  AND completion_due_at IS NULL
                  AND checked_out_at IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_completion_due_at",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "completion_due_at",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "turnover_hours_override",
                schema: "booking",
                table: "bookings");
        }
    }
}
