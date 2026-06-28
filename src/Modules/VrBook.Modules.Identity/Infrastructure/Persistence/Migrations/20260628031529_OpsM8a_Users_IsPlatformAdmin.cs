using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM8a_Users_IsPlatformAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_platform_admin",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_users_is_platform_admin",
                schema: "identity",
                table: "users",
                column: "is_platform_admin",
                filter: "\"is_platform_admin\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_is_platform_admin",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_platform_admin",
                schema: "identity",
                table: "users");
        }
    }
}
