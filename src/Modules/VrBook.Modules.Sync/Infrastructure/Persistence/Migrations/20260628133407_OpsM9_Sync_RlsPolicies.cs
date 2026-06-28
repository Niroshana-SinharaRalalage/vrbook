using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Sync_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 15-18 + §3.4 (D9 naming).
            migrationBuilder.EnableRlsTenantIsolation("sync", "channel_feeds");
            migrationBuilder.EnableRlsTenantIsolation("sync", "external_reservations");
            migrationBuilder.EnableRlsTenantIsolation("sync", "sync_conflicts");
            migrationBuilder.EnableRlsTenantIsolation("sync", "sync_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("sync", "sync_runs");
            migrationBuilder.DropRlsTenantIsolation("sync", "sync_conflicts");
            migrationBuilder.DropRlsTenantIsolation("sync", "external_reservations");
            migrationBuilder.DropRlsTenantIsolation("sync", "channel_feeds");
        }
    }
}
