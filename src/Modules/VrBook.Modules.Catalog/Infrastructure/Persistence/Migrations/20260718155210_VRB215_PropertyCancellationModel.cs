using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VRB215_PropertyCancellationModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cancellation_model",
                schema: "catalog",
                table: "properties",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancellation_model",
                schema: "catalog",
                table: "properties");
        }
    }
}
