using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice5_Tenant_Membership_Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "default_currency",
                schema: "identity",
                table: "tenants",
                type: "char(3)",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "default_timezone",
                schema: "identity",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<int>(
                name: "platform_fee_bps",
                schema: "identity",
                table: "tenants",
                type: "integer",
                nullable: false,
                defaultValue: 1500);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "identity",
                table: "tenants",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PendingOnboarding");

            migrationBuilder.AddColumn<string>(
                name: "stripe_account_id",
                schema: "identity",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_account_status",
                schema: "identity",
                table: "tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "support_email",
                schema: "identity",
                table: "tenants",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "support@vrbook.example.com");

            migrationBuilder.AddColumn<string>(
                name: "suspended_reason",
                schema: "identity",
                table: "tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenant_memberships",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_memberships", x => x.Id);
                    table.CheckConstraint("ck_tenant_memberships_role", "role IN ('tenant_admin','tenant_member')");
                    table.ForeignKey(
                        name: "FK_tenant_memberships_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "identity",
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tenant_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenants_status",
                schema: "identity",
                table: "tenants",
                sql: "status IN ('PendingOnboarding','Active','Suspended','Closed')");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_memberships_tenant_id",
                schema: "identity",
                table: "tenant_memberships",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_memberships_user",
                schema: "identity",
                table: "tenant_memberships",
                column: "user_id",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_memberships_user_tenant",
                schema: "identity",
                table: "tenant_memberships",
                columns: new[] { "user_id", "tenant_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            // OPS.M.1 — seed the "VrBook Default" tenant row that OPS.M.3b will
            // backfill all existing module rows against. Deterministic UUID per
            // docs/OPS_M_1_PLAN.md §2.4 — staging + prod converge on the same
            // value, migration scripts stay portable, the literal is grep-able
            // and rollback is unambiguous. ON CONFLICT makes re-runs idempotent.
            migrationBuilder.Sql(@"
                INSERT INTO identity.tenants
                    (""Id"", slug, display_name, status,
                     default_currency, default_timezone, support_email,
                     platform_fee_bps,
                     created_at, updated_at, row_version)
                VALUES
                    ('00000000-0000-0000-0000-000000000001',
                     'default', 'VrBook Default', 'Active',
                     'USD', 'UTC', 'support@vrbook.example.com',
                     1500,
                     NOW(), NOW(), 0)
                ON CONFLICT (""Id"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse of the OPS.M.1 seed - drop the default tenant row first so
            // the column drops below don't trip on it. If anyone else inserted
            // a row referencing it via the cross-schema FK (booking.availability
            // _blocks.tenant_id from Slice 3), the FK is RESTRICT-on-delete and
            // this will fail loudly - desired, since OPS.M.3b is the only thing
            // that should ever point at this row, and rolling back this
            // migration with M.3 in place is a sequencing bug.
            migrationBuilder.Sql(@"
                DELETE FROM identity.tenants
                 WHERE ""Id"" = '00000000-0000-0000-0000-000000000001';
            ");

            migrationBuilder.DropTable(
                name: "tenant_memberships",
                schema: "identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenants_status",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "default_currency",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "default_timezone",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "platform_fee_bps",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "stripe_account_id",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "stripe_account_status",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "support_email",
                schema: "identity",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "suspended_reason",
                schema: "identity",
                table: "tenants");
        }
    }
}
