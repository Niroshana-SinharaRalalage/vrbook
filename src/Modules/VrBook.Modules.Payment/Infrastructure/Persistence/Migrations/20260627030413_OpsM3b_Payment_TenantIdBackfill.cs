using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3b_Payment_TenantIdBackfill : Migration
    {
        // OPS.M.3b — backfill payment_intents + refunds to default tenant.
        // webhook_events is left alone per OPS_M_3_PLAN §1.4 — stays Guid?
        // forever; OPS.M.5 populates per-event.

        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE payment.payment_intents
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE payment.refunds
                   SET tenant_id = '{DefaultTenantId}'::uuid
                 WHERE tenant_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE payment.refunds
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");

            migrationBuilder.Sql($@"
                UPDATE payment.payment_intents
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
