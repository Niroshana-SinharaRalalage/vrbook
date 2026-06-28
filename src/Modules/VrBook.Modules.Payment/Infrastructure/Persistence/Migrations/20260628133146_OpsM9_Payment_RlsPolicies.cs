using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Payment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Payment_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 rows 6-8 + §3.4 (D9 naming).
            // payment.webhook_events has nullable tenant_id (OPS.M.5
            // orphan-row path for unknown stripe_account_id) — use
            // the nullable policy variant per §4.12 (D12).
            migrationBuilder.EnableRlsTenantIsolation("payment", "payment_intents");
            migrationBuilder.EnableRlsTenantIsolation("payment", "refunds");
            migrationBuilder.EnableRlsTenantIsolation("payment", "webhook_events", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("payment", "webhook_events");
            migrationBuilder.DropRlsTenantIsolation("payment", "refunds");
            migrationBuilder.DropRlsTenantIsolation("payment", "payment_intents");
        }
    }
}
