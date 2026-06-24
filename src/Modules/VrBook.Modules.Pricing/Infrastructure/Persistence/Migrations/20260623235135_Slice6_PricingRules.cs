using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Pricing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice6_PricingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pricing_rules",
                schema: "pricing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    pricing_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    day_of_week_mask = table.Column<int>(type: "integer", nullable: true),
                    min_nights = table.Column<int>(type: "integer", nullable: true),
                    max_nights = table.Column<int>(type: "integer", nullable: true),
                    days_before_checkin = table.Column<int>(type: "integer", nullable: true),
                    adjustment_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    adjustment_value = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pricing_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pricing_rules_pricing_plans_pricing_plan_id",
                        column: x => x.pricing_plan_id,
                        principalSchema: "pricing",
                        principalTable: "pricing_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pricing_rules_pricing_plan_id_priority",
                schema: "pricing",
                table: "pricing_rules",
                columns: new[] { "pricing_plan_id", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pricing_rules",
                schema: "pricing");
        }
    }
}
