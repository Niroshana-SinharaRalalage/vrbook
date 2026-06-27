using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Pricing_TenantIdNotNull : Migration
    {
        // OPS.M.3c — raw SQL ALTER COLUMN SET NOT NULL; no permanent DEFAULT.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE pricing.pricing_plans ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE pricing.pricing_rules ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE pricing.pricing_rules ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE pricing.pricing_plans ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
