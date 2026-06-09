using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A2.2 — adds is_active column to amenities + expands the catalog from
    /// 25 to ~70 amenities across 12 industry-standard categories. Existing
    /// amenity Ids are preserved (the 11111111-... GUIDs from the original seed)
    /// so existing property_amenities rows remain valid.
    /// </summary>
    public partial class A2_2_AmenityIsActive_AndExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- 1. Add is_active column (defaults true) ---------------------
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "catalog",
                table: "amenities",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // --- 2. Seed expansion ------------------------------------------
            // Categories aligned with Airbnb/VRBO/Booking.com convention:
            //   Essentials, Kitchen & Dining, Bathroom, Bedroom & Laundry,
            //   Entertainment, Family, Heating & Cooling, Internet & Office,
            //   Parking & Facilities, Outdoor, Services, Accessibility,
            //   Safety, Views.
            // GUIDs use the 22222222-... namespace to make A2.2 additions
            // distinguishable from the original 11111111-... A2 seed.
            var rows = new (string id, string code, string name, string icon, string category)[]
            {
                // Kitchen & Dining
                ("22222222-2222-2222-2222-000000000001", "microwave",         "Microwave",                  "microwave",          "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000002", "dishwasher",        "Dishwasher",                 "utensils",           "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000003", "refrigerator",      "Refrigerator",               "refrigerator",       "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000004", "coffee_maker",      "Coffee maker",               "coffee",             "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000005", "oven",              "Oven",                       "oven",               "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000006", "stove",             "Stove",                      "flame",              "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000007", "dishes_silverware", "Dishes & silverware",        "utensils",           "Kitchen & Dining"),
                ("22222222-2222-2222-2222-000000000008", "toaster",           "Toaster",                    "toast",              "Kitchen & Dining"),

                // Bathroom
                ("22222222-2222-2222-2222-000000000010", "shampoo",           "Shampoo",                    "droplet",            "Bathroom"),
                ("22222222-2222-2222-2222-000000000011", "body_wash",         "Body wash",                  "droplet",            "Bathroom"),
                ("22222222-2222-2222-2222-000000000012", "hair_dryer",        "Hair dryer",                 "wind",               "Bathroom"),
                ("22222222-2222-2222-2222-000000000013", "towels",            "Towels",                     "shower-head",        "Bathroom"),
                ("22222222-2222-2222-2222-000000000014", "hot_water",         "Hot water",                  "thermometer",        "Bathroom"),

                // Bedroom & Laundry
                ("22222222-2222-2222-2222-000000000020", "bed_linens",        "Bed linens",                 "bed",                "Bedroom & Laundry"),
                ("22222222-2222-2222-2222-000000000021", "extra_pillows",     "Extra pillows & blankets",   "bed",                "Bedroom & Laundry"),
                ("22222222-2222-2222-2222-000000000022", "room_darkening",    "Room-darkening shades",      "moon",               "Bedroom & Laundry"),
                ("22222222-2222-2222-2222-000000000023", "hangers",           "Hangers",                    "shirt",              "Bedroom & Laundry"),
                ("22222222-2222-2222-2222-000000000024", "iron",              "Iron",                       "shirt",              "Bedroom & Laundry"),

                // Entertainment (beyond TV which is already in the original seed)
                ("22222222-2222-2222-2222-000000000030", "smart_tv",          "Smart TV with streaming",    "tv",                 "Entertainment"),
                ("22222222-2222-2222-2222-000000000031", "sound_system",      "Sound system",               "speaker",            "Entertainment"),
                ("22222222-2222-2222-2222-000000000032", "books",             "Books",                      "book",               "Entertainment"),
                ("22222222-2222-2222-2222-000000000033", "board_games",       "Board games",                "dices",              "Entertainment"),
                ("22222222-2222-2222-2222-000000000034", "game_console",      "Game console",               "gamepad",            "Entertainment"),

                // Family
                ("22222222-2222-2222-2222-000000000040", "high_chair",        "High chair",                 "baby",               "Family"),
                ("22222222-2222-2222-2222-000000000041", "pack_n_play",       "Pack 'n play / travel crib", "baby",               "Family"),
                ("22222222-2222-2222-2222-000000000042", "baby_gate",         "Baby safety gate",           "baby",               "Family"),
                ("22222222-2222-2222-2222-000000000043", "children_toys",     "Children's books & toys",    "blocks",             "Family"),

                // Heating & Cooling (extends existing AC + heating)
                ("22222222-2222-2222-2222-000000000050", "ceiling_fan",       "Ceiling fan",                "fan",                "Heating & Cooling"),
                ("22222222-2222-2222-2222-000000000051", "portable_fan",      "Portable fan",               "fan",                "Heating & Cooling"),
                ("22222222-2222-2222-2222-000000000052", "indoor_fireplace",  "Indoor fireplace",           "flame",              "Heating & Cooling"),

                // Internet & Office (extends existing Wi-Fi + Workspace)
                ("22222222-2222-2222-2222-000000000060", "fast_wifi",         "Fast Wi-Fi (50+ Mbps)",      "wifi",               "Internet & Office"),
                ("22222222-2222-2222-2222-000000000061", "ethernet",          "Ethernet connection",        "cable",              "Internet & Office"),
                ("22222222-2222-2222-2222-000000000062", "printer",           "Printer",                    "printer",            "Internet & Office"),

                // Parking & Facilities (extends Free/Paid parking + EV charger)
                ("22222222-2222-2222-2222-000000000070", "garage_parking",    "Garage parking",             "warehouse",          "Parking & Facilities"),
                ("22222222-2222-2222-2222-000000000071", "street_parking",    "Street parking",             "parking-circle",     "Parking & Facilities"),
                ("22222222-2222-2222-2222-000000000072", "gym",               "Gym",                        "dumbbell",           "Parking & Facilities"),
                ("22222222-2222-2222-2222-000000000073", "sauna",             "Sauna",                      "thermometer",        "Parking & Facilities"),
                ("22222222-2222-2222-2222-000000000074", "elevator",          "Elevator",                   "arrows-up-down",     "Parking & Facilities"),

                // Outdoor (extends pool, hot_tub, patio, bbq, ocean_view, mountain_view)
                ("22222222-2222-2222-2222-000000000080", "garden",            "Garden / backyard",          "trees",              "Outdoor"),
                ("22222222-2222-2222-2222-000000000081", "outdoor_furniture", "Outdoor furniture",          "armchair",           "Outdoor"),
                ("22222222-2222-2222-2222-000000000082", "outdoor_dining",    "Outdoor dining area",        "utensils",           "Outdoor"),
                ("22222222-2222-2222-2222-000000000083", "fire_pit",          "Fire pit",                   "flame",              "Outdoor"),
                ("22222222-2222-2222-2222-000000000084", "beach_access",      "Beach access",               "waves",              "Outdoor"),
                ("22222222-2222-2222-2222-000000000085", "lake_access",       "Lake access",                "waves",              "Outdoor"),
                ("22222222-2222-2222-2222-000000000086", "ski_in_out",        "Ski-in / ski-out",           "mountain",           "Outdoor"),

                // Services
                ("22222222-2222-2222-2222-000000000090", "self_checkin",      "Self check-in",              "key",                "Services"),
                ("22222222-2222-2222-2222-000000000091", "long_term_stays",   "Long-term stays allowed",    "calendar-days",      "Services"),
                ("22222222-2222-2222-2222-000000000092", "cleaning_included", "Cleaning before checkout",   "broom",              "Services"),
                ("22222222-2222-2222-2222-000000000093", "luggage_dropoff",   "Luggage drop-off allowed",   "luggage",            "Services"),

                // Accessibility
                ("22222222-2222-2222-2222-0000000000a0", "step_free",         "Step-free path to entrance", "footprints",         "Accessibility"),
                ("22222222-2222-2222-2222-0000000000a1", "wide_doorways",     "Wide doorways (>32in)",      "door-open",          "Accessibility"),
                ("22222222-2222-2222-2222-0000000000a2", "accessible_bath",   "Accessible bathroom",        "accessibility",      "Accessibility"),
                ("22222222-2222-2222-2222-0000000000a3", "wheelchair_park",   "Wheelchair-accessible parking","accessibility",    "Accessibility"),

                // Safety extensions
                ("22222222-2222-2222-2222-0000000000b0", "security_cameras",  "Security cameras (disclosed)","camera",            "Safety"),
                ("22222222-2222-2222-2222-0000000000b1", "pool_fence",        "Pool / hot tub safety fence","fence",              "Safety"),

                // Views (extends ocean + mountain)
                ("22222222-2222-2222-2222-0000000000c0", "lake_view",         "Lake view",                  "waves",              "Views"),
                ("22222222-2222-2222-2222-0000000000c1", "city_view",         "City view",                  "building",           "Views"),
                ("22222222-2222-2222-2222-0000000000c2", "garden_view",       "Garden view",                "trees",              "Views"),
                ("22222222-2222-2222-2222-0000000000c3", "panoramic_view",    "Panoramic view",             "expand",             "Views"),
            };

            var now = new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero);
            foreach (var (id, code, name, icon, category) in rows)
            {
                migrationBuilder.InsertData(
                    schema: "catalog",
                    table: "amenities",
                    columns: new[] { "Id", "code", "name", "icon", "category", "is_active", "row_version", "created_at", "updated_at" },
                    values: new object[] { id, code, name, icon, category, true, 0L, now, now });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the new seed rows (catch any FK references first; for staging we just delete).
            migrationBuilder.Sql("DELETE FROM catalog.amenities WHERE \"Id\"::text LIKE '22222222-%';");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "catalog",
                table: "amenities");
        }
    }
}
