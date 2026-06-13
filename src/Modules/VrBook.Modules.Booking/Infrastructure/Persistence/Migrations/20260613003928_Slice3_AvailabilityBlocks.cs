using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice3_AvailabilityBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "availability_blocks",
                schema: "booking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_availability_blocks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_availability_blocks_property_id",
                schema: "booking",
                table: "availability_blocks",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_blocks_property_dates",
                schema: "booking",
                table: "availability_blocks",
                columns: new[] { "property_id", "start_date", "end_date" });

            // REPLAN.md §10.1 forward-compat policy: cross-schema FK to
            // identity.tenants(id). Declared as raw SQL because EF doesn't model
            // FKs across DbContexts (each module owns its own schema). Stays
            // ON DELETE RESTRICT — OPS.M.3b will backfill rows before any tenant
            // can be deleted; closure happens in OPS.M.
            migrationBuilder.Sql("""
                ALTER TABLE booking.availability_blocks
                ADD CONSTRAINT fk_availability_blocks_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants (id)
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_availability_blocks_tenant_id",
                schema: "booking",
                table: "availability_blocks",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_blocks",
                schema: "booking");
        }
    }
}
