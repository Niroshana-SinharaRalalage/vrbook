using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Messaging_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 12-13 + §3.4 (D9 naming).
            migrationBuilder.EnableRlsTenantIsolation("messaging", "threads");
            migrationBuilder.EnableRlsTenantIsolation("messaging", "messages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("messaging", "messages");
            migrationBuilder.DropRlsTenantIsolation("messaging", "threads");
        }
    }
}
