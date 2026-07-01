using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.10.2 F11.7.7 fast-track (identity side) — soft-delete
    /// the three DevAuth persona user rows and their tenant_memberships.
    ///
    /// <para>Runs AFTER the F11.7.7 catalog migration
    /// (<c>20260701010000_OpsM10_2_F11_7_7_TransferDevAuthOwnedProperties</c>)
    /// which transfers ownership of any DevAuth-owned properties to a
    /// real-Entra tenant-admin survivor. See that migration's summary for
    /// the ordering rationale.</para>
    ///
    /// <para><b>Bypass domain</b>: as with F11.7.6.4, the raw SQL bypasses
    /// <c>User.Deactivate(reason, actorId)</c>. Deactivate raises
    /// <c>UserDeactivated</c> (no handlers registered) and requires a
    /// non-nullable actorId (this heal has no actor). Raw SQL is the
    /// correct one-shot mechanism.</para>
    ///
    /// <para><b>What survives unchanged</b>: booking.bookings.
    /// guest_user_id, reviews.reviews.guest_user_id,
    /// messaging.threads.guest_user_id, identity.audit_log.actor_user_id
    /// deliberately keep pointing at the soft-deleted DevAuth rows. Those
    /// FKs are uuid-shaped, not enforced cross-schema, and the reader
    /// paths use <c>IgnoreQueryFilters()</c> for admin views. Historic
    /// walk-3 bookings owned by <c>dev-guest-00000001</c> remain readable
    /// as data. Historic audit rows must never be rewritten per the
    /// auditor invariant.</para>
    ///
    /// <para><b>Post-migration DB state</b> (matches user's stated end
    /// state): <c>identity.users</c> visible via default query filter
    /// contains exactly <c>niroshanaks@gmail.com</c> (real Entra, PA +
    /// tenant_admin) + <c>niroshhh@gmail.com</c> (real Entra, guest). The
    /// three DevAuth persona rows are soft-deleted (deleted_at IS NOT
    /// NULL); still visible via <c>IgnoreQueryFilters()</c>.</para>
    /// </summary>
    public partial class OpsM10_2_F11_7_7_SoftDeleteDevAuthPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Soft-delete the DevAuth persona user rows.
UPDATE identity.users
   SET deleted_at = NOW(),
       deleted_by = NULL,
       updated_at = NOW()
 WHERE b2c_object_id IN
   ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND deleted_at IS NULL;

-- Soft-delete the DevAuth-persona tenant_memberships (the Slice5b
-- DevAuth-default-tenant-membership seed + any F11.7.5.10 widening
-- adds).
UPDATE identity.tenant_memberships tm
   SET deleted_at = NOW(),
       updated_at = NOW()
  FROM identity.users u
 WHERE tm.user_id = u.""Id""
   AND u.b2c_object_id IN
     ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND tm.deleted_at IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reverse: soft-deletes lose the "which oid did the
            // row have when it was live" information. Reversibility for
            // staging is via pg_dump BEFORE running this migration.
        }
    }
}
