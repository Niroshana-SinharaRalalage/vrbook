using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.10.2 F11.7.7 fast-track (catalog side) — transfer
    /// ownership of any <c>catalog.properties</c> row currently owned by
    /// a DevAuth persona (oid IN dev-owner-*, dev-guest-*, dev-admin-*)
    /// to the real-Entra tenant-admin of the same tenant.
    ///
    /// <para><b>Why this runs FIRST</b> (before the identity-side soft-
    /// delete): the identity migration soft-deletes the DevAuth persona
    /// rows. If a property still pointed at one of those rows, the
    /// <c>owner_user_id</c> would resolve to a soft-deleted user →
    /// <c>/admin/properties</c> "my properties" would exclude Beach Villa
    /// from the real-Entra owner's view. Transfer first, then soft-delete.</para>
    ///
    /// <para><b>Survivor selection</b>: a user row is the survivor iff (a)
    /// they hold an active <c>tenant_admin</c> membership for the
    /// property's tenant, (b) their oid is NOT a DevAuth persona oid, and
    /// (c) they are not soft-deleted. Post-F11.7.5.10's widened bootstrap,
    /// the real-Entra <c>niroshanaks@gmail.com</c> row is a tenant-admin
    /// of tenant <c>…0001</c>, so Beach Villa (which belongs to that
    /// tenant, owned today by <c>dev-owner-00000000</c>) transfers cleanly.</para>
    ///
    /// <para><b>Precondition guard</b>: a <c>DO $$ RAISE EXCEPTION</c>
    /// block runs first; if any DevAuth-owned property has NO qualifying
    /// survivor (i.e. no real-Entra tenant-admin in its tenant), the
    /// migration aborts with a clear message. Prevents silent orphaning.</para>
    ///
    /// <para><b>Bypass domain</b>: the property aggregate has no
    /// <c>TransferOwnership</c> method. This is a one-shot data heal, not
    /// a domain operation. Raw SQL matches the F11.7.6.4 + F11.7.7.10a
    /// pattern.</para>
    /// </summary>
    public partial class OpsM10_2_F11_7_7_TransferDevAuthOwnedProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Precondition: every DevAuth-owned property must have a qualifying
-- real-Entra tenant-admin survivor in the same tenant.
DO $$
DECLARE
    orphaned INT;
BEGIN
    SELECT COUNT(*) INTO orphaned
      FROM catalog.properties p
      JOIN identity.users devauth ON devauth.""Id"" = p.owner_user_id
     WHERE devauth.b2c_object_id IN
       ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
       AND p.deleted_at IS NULL
       AND NOT EXISTS (
         SELECT 1
           FROM identity.tenant_memberships tm
           JOIN identity.users survivor ON survivor.""Id"" = tm.user_id
          WHERE tm.tenant_id = p.tenant_id
            AND tm.role = 'tenant_admin'
            AND tm.deleted_at IS NULL
            AND survivor.deleted_at IS NULL
            AND survivor.b2c_object_id NOT IN
              ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
       );
    IF orphaned > 0 THEN
        RAISE EXCEPTION
          'F11.7.7 fast-track blocked: % catalog.properties row(s) owned by a DevAuth persona have no real-Entra tenant-admin survivor in their tenant. Sign in as the operator against staging (real-Entra) so UserProvisioningMiddleware provisions the row, ensure their tenant_memberships row exists with role=tenant_admin, then re-deploy.',
          orphaned;
    END IF;
END $$;

-- Transfer. For each DevAuth-owned property, pick the FIRST qualifying
-- tenant-admin survivor of the property's tenant (ORDER BY oldest
-- CreatedAt for deterministic pick if multiple tenant-admins exist).
UPDATE catalog.properties p
   SET owner_user_id = (
         SELECT survivor.""Id""
           FROM identity.tenant_memberships tm
           JOIN identity.users survivor ON survivor.""Id"" = tm.user_id
          WHERE tm.tenant_id = p.tenant_id
            AND tm.role = 'tenant_admin'
            AND tm.deleted_at IS NULL
            AND survivor.deleted_at IS NULL
            AND survivor.b2c_object_id NOT IN
              ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
          ORDER BY survivor.created_at ASC
          LIMIT 1
       ),
       updated_at = NOW()
 WHERE p.deleted_at IS NULL
   AND EXISTS (
     SELECT 1 FROM identity.users devauth
      WHERE devauth.""Id"" = p.owner_user_id
        AND devauth.b2c_object_id IN
          ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reverse: original owner_user_id is not stored. The
            // reversibility strategy for staging is pg_dump of catalog
            // BEFORE the migration runs; restore from that dump if a
            // rollback is needed.
        }
    }
}
