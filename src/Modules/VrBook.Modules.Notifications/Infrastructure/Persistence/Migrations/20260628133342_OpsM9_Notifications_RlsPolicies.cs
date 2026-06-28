using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Notifications_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 row 14 + §3.4 (D9 naming) + §4.12 (D12 nullable).
            // notifications.notification_log carries nullable tenant_id for
            // platform-level operator notifications.
            migrationBuilder.EnableRlsTenantIsolation("notifications", "notification_log", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("notifications", "notification_log");
        }
    }
}
