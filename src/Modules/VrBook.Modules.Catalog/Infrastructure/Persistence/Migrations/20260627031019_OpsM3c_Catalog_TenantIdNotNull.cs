using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Catalog_TenantIdNotNull : Migration
    {
        // OPS.M.3c — flip tenant_id NOT NULL. Wave B already backfilled every
        // pre-existing row, so this ALTER won't reject any data. Raw SQL is
        // used to avoid EF's defensive `defaultValue` which would leave a
        // permanent DEFAULT clause on the column — we always provide the value
        // from the aggregate factory and never want the DB to invent one.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE catalog.properties ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE catalog.property_images ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE catalog.property_images ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE catalog.properties ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
