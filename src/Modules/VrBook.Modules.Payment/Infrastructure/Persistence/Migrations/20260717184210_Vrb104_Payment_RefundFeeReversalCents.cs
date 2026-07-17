using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Vrb104_Payment_RefundFeeReversalCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "fee_reversal_cents",
                schema: "payment",
                table: "refunds",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fee_reversal_cents",
                schema: "payment",
                table: "refunds");
        }
    }
}
