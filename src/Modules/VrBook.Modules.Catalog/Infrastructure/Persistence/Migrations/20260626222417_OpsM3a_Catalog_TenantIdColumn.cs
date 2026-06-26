using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Catalog_TenantIdColumn : Migration
    {
        // OPS.M.3a — adds nullable tenant_id columns to catalog.properties and
        // catalog.property_images, cross-schema FK to identity.tenants("Id"),
        // and an index per column. Per docs/OPS_M_3_PLAN.md §6 and the Slice 3
        // pattern in 20260613003928_Slice3_AvailabilityBlocks.cs:50-69.
        //
        // The FK is ON DELETE RESTRICT — OPS.M.3b backfills rows before any
        // tenant can be deleted. ON CASCADE would let a tenant delete cascade-
        // wipe their catalog; we want that to fail loudly so deletion happens
        // through a deliberate operator path (OPS.M.8 Super Admin console
        // eventually).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "properties",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "catalog",
                table: "property_images",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE catalog.properties
                ADD CONSTRAINT fk_properties_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE catalog.property_images
                ADD CONSTRAINT fk_property_images_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_properties_tenant_id",
                schema: "catalog",
                table: "properties",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_property_images_tenant_id",
                schema: "catalog",
                table: "property_images",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_property_images_tenant_id",
                schema: "catalog",
                table: "property_images");

            migrationBuilder.DropIndex(
                name: "IX_properties_tenant_id",
                schema: "catalog",
                table: "properties");

            migrationBuilder.Sql("ALTER TABLE catalog.property_images DROP CONSTRAINT fk_property_images_tenant;");
            migrationBuilder.Sql("ALTER TABLE catalog.properties DROP CONSTRAINT fk_properties_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "property_images");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "catalog",
                table: "properties");
        }
    }
}
