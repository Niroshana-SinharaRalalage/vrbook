using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Payment_TenantIdNotNull : Migration
    {
        // OPS.M.3c — raw SQL SET NOT NULL on payment_intents + refunds.
        // webhook_events stays nullable per OPS_M_3_PLAN §1.4.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE payment.payment_intents ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE payment.refunds ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE payment.refunds ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE payment.payment_intents ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
