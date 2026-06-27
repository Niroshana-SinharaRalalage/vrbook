using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3b_Catalog_TenantIdBackfill : Migration
    {
        // OPS.M.3b — backfill all pre-existing rows in catalog.properties and
        // catalog.property_images to the default tenant from OPS.M.1's seed.
        // Idempotent via `WHERE tenant_id IS NULL`. Wave C's NOT NULL flip
        // is gated on a CI assertion that this UPDATE leaves zero null rows.
        // Per docs/OPS_M_3_PLAN.md §4.1.

        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE catalog.properties
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE catalog.property_images
                   SET tenant_id = '{DefaultTenantId}'::uuid
                 WHERE tenant_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE catalog.property_images
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");

            migrationBuilder.Sql($@"
                UPDATE catalog.properties
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
