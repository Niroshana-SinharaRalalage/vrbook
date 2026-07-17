using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Admin.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VRB216_SettingsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cancellation_tiers",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    first_tier_days = table.Column<int>(type: "integer", nullable: false),
                    second_tier_days = table.Column<int>(type: "integer", nullable: false),
                    middle_tier_pct = table.Column<int>(type: "integer", nullable: false),
                    final_cutoff_hours = table.Column<int>(type: "integer", nullable: false),
                    upgrade_price_pct = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cancellation_tiers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_fee_overrides",
                schema: "admin",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_fee_bps = table.Column<int>(type: "integer", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_fee_overrides", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "tax_posture",
                schema: "admin",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    facilitator_active = table.Column<bool>(type: "boolean", nullable: false),
                    per_state_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_posture", x => x.id);
                });

            // VRB-216 — seed the singleton rows (idempotent fixed ids). Platform-admin
            // edits mutate these; the values are the Q24 seed defaults (7/2/50/48/8).
            var seedAt = new DateTimeOffset(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);
            migrationBuilder.InsertData(
                schema: "admin",
                table: "cancellation_tiers",
                columns: new[] { "id", "version", "first_tier_days", "second_tier_days", "middle_tier_pct", "final_cutoff_hours", "upgrade_price_pct", "updated_by_user_id", "updated_at" },
                values: new object[] { new Guid("cccccccc-0000-0000-0000-000000000001"), 1, 7, 2, 50, 48, 8, Guid.Empty, seedAt });
            migrationBuilder.InsertData(
                schema: "admin",
                table: "tax_posture",
                columns: new[] { "id", "facilitator_active", "per_state_json", "updated_by_user_id", "updated_at" },
                values: new object[] { new Guid("aaaaaaaa-0000-0000-0000-000000000001"), true, "{}", Guid.Empty, seedAt });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cancellation_tiers",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "platform_fee_overrides",
                schema: "admin");

            migrationBuilder.DropTable(
                name: "tax_posture",
                schema: "admin");
        }
    }
}
