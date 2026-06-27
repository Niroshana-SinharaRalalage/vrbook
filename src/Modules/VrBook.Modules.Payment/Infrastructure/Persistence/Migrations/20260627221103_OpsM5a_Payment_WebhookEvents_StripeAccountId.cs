using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM5a_Payment_WebhookEvents_StripeAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_events_stripe_event_id",
                schema: "payment",
                table: "webhook_events");

            migrationBuilder.AddColumn<string>(
                name: "stripe_account_id",
                schema: "payment",
                table: "webhook_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_account_event",
                schema: "payment",
                table: "webhook_events",
                columns: new[] { "stripe_event_id", "stripe_account_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_events_account_event",
                schema: "payment",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "stripe_account_id",
                schema: "payment",
                table: "webhook_events");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_stripe_event_id",
                schema: "payment",
                table: "webhook_events",
                column: "stripe_event_id",
                unique: true);
        }
    }
}
