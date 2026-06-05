using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Seeds the canonical amenity lookup. Codes are stable; the UI joins on
    /// these codes for filter chips. New amenities land via follow-up migrations.
    /// </summary>
    public partial class SeedAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var seedAt = System.DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);
            var seed = new (string Id, string Code, string Name, string Icon, string Category)[]
            {
                ("11111111-1111-1111-1111-000000000001", "wifi",              "Wi-Fi",                       "wifi",             "Essentials"),
                ("11111111-1111-1111-1111-000000000002", "kitchen",           "Kitchen",                     "utensils",         "Essentials"),
                ("11111111-1111-1111-1111-000000000003", "washer",            "Washer",                      "washing-machine",  "Essentials"),
                ("11111111-1111-1111-1111-000000000004", "dryer",             "Dryer",                       "washing-machine",  "Essentials"),
                ("11111111-1111-1111-1111-000000000005", "ac",                "Air conditioning",            "snowflake",        "Essentials"),
                ("11111111-1111-1111-1111-000000000006", "heating",           "Heating",                     "flame",            "Essentials"),
                ("11111111-1111-1111-1111-000000000007", "tv",                "TV",                          "tv",               "Entertainment"),
                ("11111111-1111-1111-1111-000000000008", "workspace",         "Dedicated workspace",         "monitor",          "Work"),
                ("11111111-1111-1111-1111-000000000009", "free_parking",      "Free parking on premises",    "parking-circle",   "Parking"),
                ("11111111-1111-1111-1111-00000000000a", "paid_parking",      "Paid parking on premises",    "parking-circle",   "Parking"),
                ("11111111-1111-1111-1111-00000000000b", "pool",              "Pool",                        "waves",            "Outdoor"),
                ("11111111-1111-1111-1111-00000000000c", "hot_tub",           "Hot tub",                     "bath",             "Outdoor"),
                ("11111111-1111-1111-1111-00000000000d", "patio",             "Patio or balcony",            "trees",            "Outdoor"),
                ("11111111-1111-1111-1111-00000000000e", "bbq",               "BBQ grill",                   "utensils-crossed", "Outdoor"),
                ("11111111-1111-1111-1111-00000000000f", "pets_allowed",      "Pets allowed",                "dog",              "Family"),
                ("11111111-1111-1111-1111-000000000010", "crib",              "Crib",                        "baby",             "Family"),
                ("11111111-1111-1111-1111-000000000011", "self_check_in",     "Self check-in",               "key-round",        "Check-in"),
                ("11111111-1111-1111-1111-000000000012", "lockbox",           "Lockbox",                     "lock",             "Check-in"),
                ("11111111-1111-1111-1111-000000000013", "smoke_alarm",       "Smoke alarm",                 "siren",            "Safety"),
                ("11111111-1111-1111-1111-000000000014", "co_alarm",          "Carbon monoxide alarm",       "siren",            "Safety"),
                ("11111111-1111-1111-1111-000000000015", "first_aid_kit",     "First aid kit",               "first-aid",        "Safety"),
                ("11111111-1111-1111-1111-000000000016", "fire_extinguisher", "Fire extinguisher",           "extinguisher",     "Safety"),
                ("11111111-1111-1111-1111-000000000017", "ev_charger",        "EV charger",                  "plug",             "Parking"),
                ("11111111-1111-1111-1111-000000000018", "ocean_view",        "Ocean view",                  "waves",            "Views"),
                ("11111111-1111-1111-1111-000000000019", "mountain_view",     "Mountain view",               "mountain",         "Views"),
            };

            foreach (var (id, code, name, icon, category) in seed)
            {
                migrationBuilder.InsertData(
                    schema: "catalog",
                    table: "amenities",
                    columns: new[] { "Id", "code", "name", "icon", "category", "row_version", "created_at", "updated_at" },
                    values: new object[]
                    {
                        new System.Guid(id),
                        code,
                        name,
                        icon,
                        category,
                        0L,
                        seedAt,
                        seedAt,
                    });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM catalog.amenities");
        }
    }
}
