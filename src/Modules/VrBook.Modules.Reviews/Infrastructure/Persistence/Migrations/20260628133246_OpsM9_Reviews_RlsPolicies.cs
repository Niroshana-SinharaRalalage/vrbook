using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Reviews_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 row 9 + §3.4 (D9 naming).
            migrationBuilder.EnableRlsTenantIsolation("reviews", "reviews");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("reviews", "reviews");
        }
    }
}
