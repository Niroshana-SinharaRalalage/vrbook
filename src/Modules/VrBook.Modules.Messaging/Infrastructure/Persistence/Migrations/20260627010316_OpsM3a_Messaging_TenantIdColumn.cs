using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Messaging_TenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "messaging",
                table: "threads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "messaging",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE messaging.threads
                ADD CONSTRAINT fk_threads_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE messaging.messages
                ADD CONSTRAINT fk_messages_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_threads_tenant_id",
                schema: "messaging",
                table: "threads",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_messages_tenant_id",
                schema: "messaging",
                table: "messages",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_threads_tenant_id",
                schema: "messaging",
                table: "threads");

            migrationBuilder.DropIndex(
                name: "IX_messages_tenant_id",
                schema: "messaging",
                table: "messages");

            migrationBuilder.Sql("ALTER TABLE messaging.messages DROP CONSTRAINT fk_messages_tenant;");
            migrationBuilder.Sql("ALTER TABLE messaging.threads DROP CONSTRAINT fk_threads_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "messaging",
                table: "threads");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "messaging",
                table: "messages");
        }
    }
}
