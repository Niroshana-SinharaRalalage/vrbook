using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Sync_TenantIdColumn : Migration
    {
        // OPS.M.3a — adds nullable tenant_id to all 4 sync tables, cross-schema
        // FK to identity.tenants("Id") ON DELETE RESTRICT, and an index per
        // table. Hand-written because the snapshot got polluted by an aborted
        // first generation (per docs/OPS_M_3_PLAN §6 snapshot-pollution note).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "sync",
                table: "channel_feeds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "sync",
                table: "external_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "sync",
                table: "sync_conflicts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "sync",
                table: "sync_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE sync.channel_feeds
                ADD CONSTRAINT fk_channel_feeds_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sync.external_reservations
                ADD CONSTRAINT fk_external_reservations_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sync.sync_conflicts
                ADD CONSTRAINT fk_sync_conflicts_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE sync.sync_runs
                ADD CONSTRAINT fk_sync_runs_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_channel_feeds_tenant_id",
                schema: "sync",
                table: "channel_feeds",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_reservations_tenant_id",
                schema: "sync",
                table: "external_reservations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sync_conflicts_tenant_id",
                schema: "sync",
                table: "sync_conflicts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_sync_runs_tenant_id",
                schema: "sync",
                table: "sync_runs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sync_runs_tenant_id",
                schema: "sync",
                table: "sync_runs");

            migrationBuilder.DropIndex(
                name: "IX_sync_conflicts_tenant_id",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.DropIndex(
                name: "IX_external_reservations_tenant_id",
                schema: "sync",
                table: "external_reservations");

            migrationBuilder.DropIndex(
                name: "IX_channel_feeds_tenant_id",
                schema: "sync",
                table: "channel_feeds");

            migrationBuilder.Sql("ALTER TABLE sync.sync_runs DROP CONSTRAINT fk_sync_runs_tenant;");
            migrationBuilder.Sql("ALTER TABLE sync.sync_conflicts DROP CONSTRAINT fk_sync_conflicts_tenant;");
            migrationBuilder.Sql("ALTER TABLE sync.external_reservations DROP CONSTRAINT fk_external_reservations_tenant;");
            migrationBuilder.Sql("ALTER TABLE sync.channel_feeds DROP CONSTRAINT fk_channel_feeds_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "sync",
                table: "sync_runs");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "sync",
                table: "external_reservations");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "sync",
                table: "channel_feeds");
        }
    }
}
