using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Payment_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "payment",
                table: "webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "payment",
                table: "refunds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "payment",
                table: "payment_intents",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE payment.payment_intents
                ADD CONSTRAINT fk_payment_intents_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payment.refunds
                ADD CONSTRAINT fk_refunds_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE payment.webhook_events
                ADD CONSTRAINT fk_webhook_events_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_tenant_id",
                schema: "payment",
                table: "webhook_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_refunds_tenant_id",
                schema: "payment",
                table: "refunds",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_payment_intents_tenant_id",
                schema: "payment",
                table: "payment_intents",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_webhook_events_tenant_id",
                schema: "payment",
                table: "webhook_events");

            migrationBuilder.DropIndex(
                name: "IX_refunds_tenant_id",
                schema: "payment",
                table: "refunds");

            migrationBuilder.DropIndex(
                name: "IX_payment_intents_tenant_id",
                schema: "payment",
                table: "payment_intents");

            migrationBuilder.Sql("ALTER TABLE payment.webhook_events DROP CONSTRAINT fk_webhook_events_tenant;");
            migrationBuilder.Sql("ALTER TABLE payment.refunds DROP CONSTRAINT fk_refunds_tenant;");
            migrationBuilder.Sql("ALTER TABLE payment.payment_intents DROP CONSTRAINT fk_payment_intents_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "payment",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "payment",
                table: "refunds");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "payment",
                table: "payment_intents");
        }
    }
}
