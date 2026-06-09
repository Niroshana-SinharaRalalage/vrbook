using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VrBook.Modules.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A6_InitSyncSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "channel_feeds",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    inbound_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    outbound_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    poll_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    etag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_channel_feeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "external_reservations",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    ical_uid = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    checkin = table.Column<DateOnly>(type: "date", nullable: false),
                    checkout = table.Column<DateOnly>(type: "date", nullable: false),
                    summary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    raw_payload = table.Column<string>(type: "text", nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_external_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "sync",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sync_conflicts",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    resolution = table.Column<int>(type: "integer", nullable: false),
                    resolution_notes = table.Column<string>(type: "text", nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_sync_conflicts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sync_runs",
                schema: "sync",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_feed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    events_seen = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    events_new = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    events_updated = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    events_cancelled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    error = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_sync_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_feeds_outbound_token",
                schema: "sync",
                table: "channel_feeds",
                column: "outbound_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_feeds_property_id",
                schema: "sync",
                table: "channel_feeds",
                column: "property_id");

            migrationBuilder.CreateIndex(
                name: "ix_channel_feeds_due_for_poll",
                schema: "sync",
                table: "channel_feeds",
                columns: new[] { "is_enabled", "last_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_external_reservations_feed_uid",
                schema: "sync",
                table: "external_reservations",
                columns: new[] { "channel_feed_id", "ical_uid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_external_reservations_overlap",
                schema: "sync",
                table: "external_reservations",
                columns: new[] { "property_id", "cancelled_at", "checkin", "checkout" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "sync",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatched_at",
                schema: "sync",
                table: "outbox_messages",
                column: "dispatched_at");

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_booking_external",
                schema: "sync",
                table: "sync_conflicts",
                columns: new[] { "booking_id", "external_reservation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_property_resolution",
                schema: "sync",
                table: "sync_conflicts",
                columns: new[] { "property_id", "resolution" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_runs_feed_started",
                schema: "sync",
                table: "sync_runs",
                columns: new[] { "channel_feed_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_feeds",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "external_reservations",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "sync_conflicts",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "sync_runs",
                schema: "sync");
        }
    }
}
