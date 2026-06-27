using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Booking_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "booking",
                table: "bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "booking",
                table: "booking_holds",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE booking.bookings
                ADD CONSTRAINT fk_bookings_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE booking.booking_holds
                ADD CONSTRAINT fk_booking_holds_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_tenant_id",
                schema: "booking",
                table: "bookings",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_booking_holds_tenant_id",
                schema: "booking",
                table: "booking_holds",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bookings_tenant_id",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "IX_booking_holds_tenant_id",
                schema: "booking",
                table: "booking_holds");

            migrationBuilder.Sql("ALTER TABLE booking.booking_holds DROP CONSTRAINT fk_booking_holds_tenant;");
            migrationBuilder.Sql("ALTER TABLE booking.bookings DROP CONSTRAINT fk_bookings_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "booking",
                table: "booking_holds");
        }
    }
}
