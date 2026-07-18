using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Vrb102_Booking_CancellationSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cancellation_final_cutoff_hours",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cancellation_first_tier_days",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cancellation_middle_tier_refund_pct",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_model",
                schema: "booking",
                table: "bookings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cancellation_second_tier_days",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cancellation_tier_version",
                schema: "booking",
                table: "bookings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refundable_upgrade_price_amount",
                schema: "booking",
                table: "bookings",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refundable_upgrade_price_currency",
                schema: "booking",
                table: "bookings",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "refundable_upgrade_purchased",
                schema: "booking",
                table: "bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_final_cutoff_hours",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancellation_first_tier_days",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancellation_middle_tier_refund_pct",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancellation_model",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancellation_second_tier_days",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "cancellation_tier_version",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refundable_upgrade_price_amount",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refundable_upgrade_price_currency",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "refundable_upgrade_purchased",
                schema: "booking",
                table: "bookings");
        }
    }
}
