using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitPricingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.CreateTable(
                name: "pricing_plans",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_nightly_rate = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    weekend_rate = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    min_stay_nights = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    max_stay_nights = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    dynamic_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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
                    table.PrimaryKey("PK_pricing_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fees",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    basis = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    free_threshold = table.Column<int>(type: "integer", nullable: true),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fees_pricing_plans_pricing_plan_id",
                        column: x => x.pricing_plan_id,
                        principalSchema: "pricing",
                        principalTable: "pricing_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fees_pricing_plan_id",
                schema: "pricing",
                table: "fees",
                column: "pricing_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_pricing_plans_property_id",
                schema: "pricing",
                table: "pricing_plans",
                column: "property_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fees",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "pricing_plans",
                schema: "pricing");
        }
    }
}
