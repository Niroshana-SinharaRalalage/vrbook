using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "amenities",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
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
                    table.PrimaryKey("PK_amenities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    property_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    state = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    country = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    max_guests = table.Column<int>(type: "integer", nullable: false),
                    bedrooms = table.Column<int>(type: "integer", nullable: false),
                    bathrooms = table.Column<int>(type: "integer", nullable: false),
                    beds = table.Column<int>(type: "integer", nullable: false),
                    checkin_from = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    checkin_to = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    checkout_by = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    reviews_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    dynamic_pricing_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    messaging_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    rating_avg = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                    rating_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
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
                    table.PrimaryKey("PK_properties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "house_rules",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_text = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_house_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_house_rules_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "catalog",
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_amenities",
                schema: "catalog",
                columns: table => new
                {
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amenity_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_amenities", x => new { x.property_id, x.amenity_id });
                    table.ForeignKey(
                        name: "FK_property_amenities_amenities_amenity_id",
                        column: x => x.amenity_id,
                        principalSchema: "catalog",
                        principalTable: "amenities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_property_amenities_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "catalog",
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "property_images",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    caption = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_property_images_properties_property_id",
                        column: x => x.property_id,
                        principalSchema: "catalog",
                        principalTable: "properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_amenities_code",
                schema: "catalog",
                table: "amenities",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_house_rules_property_id_sort_order",
                schema: "catalog",
                table: "house_rules",
                columns: new[] { "property_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_properties_owner_user_id",
                schema: "catalog",
                table: "properties",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_properties_slug",
                schema: "catalog",
                table: "properties",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_properties_is_active",
                schema: "catalog",
                table: "properties",
                column: "is_active",
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_property_amenities_amenity_id",
                schema: "catalog",
                table: "property_amenities",
                column: "amenity_id");

            migrationBuilder.CreateIndex(
                name: "IX_property_images_property_id_sort_order",
                schema: "catalog",
                table: "property_images",
                columns: new[] { "property_id", "sort_order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "house_rules",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "property_amenities",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "property_images",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "amenities",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "properties",
                schema: "catalog");
        }
    }
}
