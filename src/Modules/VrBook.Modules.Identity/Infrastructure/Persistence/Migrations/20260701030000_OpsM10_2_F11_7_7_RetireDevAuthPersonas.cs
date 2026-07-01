using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.10.2 F11.7.7 fast-track (retry after `163229d` reverted).
    /// Retire the three DevAuth persona rows from <c>identity.users</c>
    /// and transfer any properties they currently own to a real-Entra
    /// tenant-admin survivor of the same tenant.
    ///
    /// <para><b>Design change vs the reverted `163229d`</b>: the previous
    /// attempt split the work across TWO migrations (catalog transfer +
    /// identity soft-delete) with a hard-blocking precondition guard that
    /// aborted the deploy if any property had no survivor. The guard
    /// fired at deploy time (architect diagnosis: F11.7.6.4 dedup likely
    /// altered a tenant_admin membership shape the guard depended on) and
    /// the deploy failed with no captured logs.</para>
    ///
    /// <para><b>New shape</b>: single Identity-schema migration doing DML
    /// only (no DDL — one-schema-per-module invariant still holds; DML is
    /// data heal, not schema ownership). Best-effort behavior: transfer
    /// what has survivors, leave what doesn't, and soft-delete only those
    /// DevAuth persona rows that no longer own live properties. Data-heal
    /// migrations should be idempotent + tolerant, not abort on end-state
    /// assumptions that CI can't validate. Emits <c>RAISE NOTICE</c> with
    /// concrete counts so future triage doesn't rely on Log Analytics
    /// picking up the container-app job's stdout.</para>
    ///
    /// <para>EF Core wraps <c>MigrationBuilder.Sql</c> in a transaction by
    /// default, so no explicit <c>BEGIN/COMMIT</c> is needed. A failure
    /// anywhere rolls back the whole statement block.</para>
    ///
    /// <para><b>Bypasses <c>User.Deactivate()</c></b> intentionally, same
    /// rationale as F11.7.6.4: no <c>UserDeactivated</c> handler is
    /// registered; Deactivate requires non-nullable actorId; system-
    /// initiated heal has no actor.</para>
    /// </summary>
    public partial class OpsM10_2_F11_7_7_RetireDevAuthPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$
DECLARE
    transferred_properties INT := 0;
    retired_personas       INT := 0;
    retired_memberships    INT := 0;
    still_owning           INT := 0;
BEGIN
    -- (1) Transfer property ownership from DevAuth personas to a
    --     real-Entra tenant-admin survivor of the same tenant.
    --     WHERE clause is best-effort: no survivor -> property is
    --     skipped (owner_user_id stays pointing at the DevAuth row).
    WITH transfers AS (
        UPDATE catalog.properties p
           SET owner_user_id = x.survivor_id,
               updated_at    = NOW()
          FROM (
            SELECT p2.""Id"" AS pid,
                   (SELECT survivor.""Id""
                      FROM identity.tenant_memberships tm
                      JOIN identity.users survivor
                        ON survivor.""Id"" = tm.user_id
                     WHERE tm.tenant_id = p2.tenant_id
                       AND tm.role = 'tenant_admin'
                       AND tm.deleted_at IS NULL
                       AND survivor.deleted_at IS NULL
                       AND survivor.b2c_object_id NOT IN
                         ('dev-owner-00000000',
                          'dev-guest-00000001',
                          'dev-admin-00000002')
                     ORDER BY survivor.created_at ASC
                     LIMIT 1) AS survivor_id
              FROM catalog.properties p2
              JOIN identity.users d ON d.""Id"" = p2.owner_user_id
             WHERE p2.deleted_at IS NULL
               AND d.b2c_object_id IN
                 ('dev-owner-00000000',
                  'dev-guest-00000001',
                  'dev-admin-00000002')
          ) x
         WHERE p.""Id"" = x.pid AND x.survivor_id IS NOT NULL
        RETURNING 1
    )
    SELECT COUNT(*) INTO transferred_properties FROM transfers;

    -- (2) Soft-delete DevAuth persona rows that no longer own any live
    --     property. If a persona still owns a property (because no
    --     survivor existed above), it stays active so /admin/properties
    --     can still resolve owner_user_id.
    WITH retired AS (
        UPDATE identity.users u
           SET deleted_at = NOW(),
               deleted_by = NULL,
               updated_at = NOW()
         WHERE u.b2c_object_id IN
           ('dev-owner-00000000',
            'dev-guest-00000001',
            'dev-admin-00000002')
           AND u.deleted_at IS NULL
           AND NOT EXISTS (
             SELECT 1 FROM catalog.properties p
              WHERE p.owner_user_id = u.""Id""
                AND p.deleted_at IS NULL
           )
        RETURNING 1
    )
    SELECT COUNT(*) INTO retired_personas FROM retired;

    -- (3) Soft-delete matching tenant_memberships (only for personas
    --     that were actually soft-deleted in step 2).
    WITH cleared AS (
        UPDATE identity.tenant_memberships tm
           SET deleted_at = NOW(),
               updated_at = NOW()
          FROM identity.users u
         WHERE tm.user_id = u.""Id""
           AND u.b2c_object_id IN
             ('dev-owner-00000000',
              'dev-guest-00000001',
              'dev-admin-00000002')
           AND u.deleted_at IS NOT NULL
           AND tm.deleted_at IS NULL
        RETURNING 1
    )
    SELECT COUNT(*) INTO retired_memberships FROM cleared;

    -- (4) Warning if any DevAuth persona is still active because it
    --     owns properties without a survivor. Not fatal (matches the
    --     ""tolerant data-heal"" design) but visible in the migrator log.
    SELECT COUNT(*) INTO still_owning
      FROM identity.users u
     WHERE u.b2c_object_id IN
       ('dev-owner-00000000',
        'dev-guest-00000001',
        'dev-admin-00000002')
       AND u.deleted_at IS NULL;

    RAISE NOTICE
      'F11.7.7 fast-track: transferred=% retired_personas=% retired_memberships=% still_owning=%',
      transferred_properties, retired_personas, retired_memberships, still_owning;

    IF still_owning > 0 THEN
        RAISE WARNING
          'F11.7.7 fast-track: % DevAuth persona row(s) still active because they own properties with no real-Entra tenant-admin survivor in the tenant. Follow-up needed via admin UI.',
          still_owning;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reverse: soft-deletes + property transfers lose the
            // pre-migration owner_user_id + deleted_at state. Reversibility
            // for staging is a pre-deploy pg_dump of identity.users,
            // identity.tenant_memberships, catalog.properties.
        }
    }
}
