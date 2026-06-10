using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VrBook.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class A9_InitNotificationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "notification_log",
                schema: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("PK_notification_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "notifications",
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

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_recipient",
                schema: "notifications",
                table: "notification_log",
                columns: new[] { "recipient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_log_status",
                schema: "notifications",
                table: "notification_log",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                schema: "notifications",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dispatched_at",
                schema: "notifications",
                table: "outbox_messages",
                column: "dispatched_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_log",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "notifications");
        }
    }
}
