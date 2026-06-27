using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Notifications_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "notifications",
                table: "notification_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE notifications.notification_log
                ADD CONSTRAINT fk_notification_log_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_notification_log_tenant_id",
                schema: "notifications",
                table: "notification_log",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notification_log_tenant_id",
                schema: "notifications",
                table: "notification_log");

            migrationBuilder.Sql("ALTER TABLE notifications.notification_log DROP CONSTRAINT fk_notification_log_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "notifications",
                table: "notification_log");
        }
    }
}
