using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice4_DispatchColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_started_at",
                schema: "notifications",
                table: "notification_log",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "not_before_utc",
                schema: "notifications",
                table: "notification_log",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dispatch_started_at",
                schema: "notifications",
                table: "notification_log");

            migrationBuilder.DropColumn(
                name: "not_before_utc",
                schema: "notifications",
                table: "notification_log");
        }
    }
}
