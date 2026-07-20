using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// VRB-103 triage / Tier-A security — re-assert the canonical two-GUC tenant-isolation
    /// policy on <c>booking.bookings</c>. The <c>RlsPolicySchemaFactPack</c> caught that the
    /// live policy's qual lacked <c>app.is_platform_admin</c> (single-GUC), so tenant isolation
    /// was not as specified. This idempotently drops whatever is there and recreates it from the
    /// canonical <see cref="RlsMigrationBuilderExtensions.EnableRlsTenantIsolation"/> template
    /// (both <c>app.tenant_id</c> AND <c>app.is_platform_admin</c> in USING + WITH CHECK).
    /// Harmless if the policy is already correct — it re-emits the same SQL.
    /// </summary>
    public partial class Vrb_BookingRls_ReassertTwoGucPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop (IF EXISTS) + recreate from the canonical template. Runs inside the
            // migration transaction, so the transient RLS-disable is never externally visible.
            migrationBuilder.DropRlsTenantIsolation("booking", "bookings");
            migrationBuilder.EnableRlsTenantIsolation("booking", "bookings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op: this migration only RE-ASSERTS the canonical tenant-isolation
            // invariant. Reverting it must never drop row-level security on booking.bookings —
            // the policy predates this migration (created by OpsM9_Booking_RlsPolicies).
        }
    }
}
