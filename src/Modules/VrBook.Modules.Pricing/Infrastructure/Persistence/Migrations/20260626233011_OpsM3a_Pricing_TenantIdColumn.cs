using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Pricing_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "pricing",
                table: "pricing_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "pricing",
                table: "pricing_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE pricing.pricing_plans
                ADD CONSTRAINT fk_pricing_plans_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE pricing.pricing_rules
                ADD CONSTRAINT fk_pricing_rules_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_tenant_id",
                schema: "pricing",
                table: "pricing_rules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_plans_tenant_id",
                schema: "pricing",
                table: "pricing_plans",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pricing_rules_tenant_id",
                schema: "pricing",
                table: "pricing_rules");

            migrationBuilder.DropIndex(
                name: "IX_pricing_plans_tenant_id",
                schema: "pricing",
                table: "pricing_plans");

            migrationBuilder.Sql("ALTER TABLE pricing.pricing_rules DROP CONSTRAINT fk_pricing_rules_tenant;");
            migrationBuilder.Sql("ALTER TABLE pricing.pricing_plans DROP CONSTRAINT fk_pricing_plans_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "pricing",
                table: "pricing_rules");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "pricing",
                table: "pricing_plans");
        }
    }
}
