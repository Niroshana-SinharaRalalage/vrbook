using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Pricing_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 10-11 + §3.4 (D9 naming).
            // pricing.fees is reference data (no tenant_id) per §3.2 — carved out.
            migrationBuilder.EnableRlsTenantIsolation("pricing", "pricing_plans");
            migrationBuilder.EnableRlsTenantIsolation("pricing", "pricing_rules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("pricing", "pricing_rules");
            migrationBuilder.DropRlsTenantIsolation("pricing", "pricing_plans");
        }
    }
}
