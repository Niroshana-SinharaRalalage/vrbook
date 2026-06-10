using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A6_5_ConflictPendingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sync_conflicts_booking_external",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.CreateIndex(
                name: "ux_sync_conflicts_booking_external_pending",
                schema: "sync",
                table: "sync_conflicts",
                columns: new[] { "booking_id", "external_reservation_id" },
                unique: true,
                filter: "resolved_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sync_conflicts_booking_external_pending",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_booking_external",
                schema: "sync",
                table: "sync_conflicts",
                columns: new[] { "booking_id", "external_reservation_id" },
                unique: true);
        }
    }
}
