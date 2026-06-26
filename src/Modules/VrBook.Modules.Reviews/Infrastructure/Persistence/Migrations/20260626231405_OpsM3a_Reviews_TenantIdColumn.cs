using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3a_Reviews_TenantIdColumn : Migration
    {
        // OPS.M.3a — nullable tenant_id + cross-schema FK to identity.tenants("Id")
        // + index. FK is ON DELETE RESTRICT (per Slice 3 pattern); 3b backfills,
        // 3c flips NOT NULL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                schema: "reviews",
                table: "reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE reviews.reviews
                ADD CONSTRAINT fk_reviews_tenant
                FOREIGN KEY (tenant_id)
                REFERENCES identity.tenants ("Id")
                ON DELETE RESTRICT;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_reviews_tenant_id",
                schema: "reviews",
                table: "reviews",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_reviews_tenant_id",
                schema: "reviews",
                table: "reviews");

            migrationBuilder.Sql("ALTER TABLE reviews.reviews DROP CONSTRAINT fk_reviews_tenant;");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                schema: "reviews",
                table: "reviews");
        }
    }
}
