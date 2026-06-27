using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Identity_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "identity",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            // FK is same-schema (identity.audit_log -> identity.tenants) - no need
            // for raw SQL since both tables live in the same DbContext + schema.
            // EF's standard FK with HasOne could model this but the audit table
            // has no nav property; declare the constraint via SQL like other
            // cross-table identity FKs in this module.
            migrationBuilder.Sql("""
                ALTER TABLE identity.audit_log
                ADD CONSTRAINT fk_audit_log_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_tenant_id",
                schema: "identity",
                table: "audit_log",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_log_tenant_id",
                schema: "identity",
                table: "audit_log");

            migrationBuilder.Sql("ALTER TABLE identity.audit_log DROP CONSTRAINT fk_audit_log_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "identity",
                table: "audit_log");
        }
    }
}
