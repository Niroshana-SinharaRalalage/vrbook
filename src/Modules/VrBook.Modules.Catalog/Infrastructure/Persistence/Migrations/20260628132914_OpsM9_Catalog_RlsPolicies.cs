using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Catalog_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 1+2 + §3.4 (D9 naming).
            migrationBuilder.EnableRlsTenantIsolation("catalog", "properties");
            migrationBuilder.EnableRlsTenantIsolation("catalog", "property_images");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("catalog", "property_images");
            migrationBuilder.DropRlsTenantIsolation("catalog", "properties");
        }
    }
}
