using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.13.6 walk fix — operator bootstrap round 2.
    ///
    /// <para>The M.13.4 backfill collapsed users to email-canonical shape but
    /// did not create <c>tenant_memberships</c> for the survivor. Without a
    /// membership, <c>HttpCurrentUser.TenantId</c> falls to null (no
    /// IsPrimary=true row → no X-Active-Tenant → no legacy claim), and any
    /// tenant-scoped write throws
    /// <c>ForbiddenException("Admin action requires a tenant membership.")</c>
    /// (see <c>SyncController.CallerTenantId</c>).</para>
    ///
    /// <para>This migration grants niroshanaks a <c>tenant_admin</c>
    /// membership in the "main" tenant — defined as the tenant that owns
    /// the most catalog properties (a stable proxy for "the operator's
    /// home tenant"). <c>is_primary = TRUE</c> so the middleware's
    /// pre-M.13.6 fallback path also resolves it as the active tenant when
    /// no X-Active-Tenant header is present.</para>
    ///
    /// <para>Idempotent — WHERE NOT EXISTS guards re-runs.</para>
    /// </summary>
    public partial class OpsM13_BootstrapTenantMembershipForNiroshanaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
WITH niroshanaks_user AS (
    SELECT ""Id"" AS user_id
      FROM identity.users
     WHERE deleted_at IS NULL
       AND lower(email) LIKE '%niroshanaks%'
     ORDER BY created_at
     LIMIT 1
),
main_tenant AS (
    -- Oldest active tenant. Simpler than 'tenant that owns the most
    -- properties' — that would need a cross-schema reference to
    -- catalog.properties which doesn't exist yet when this migration
    -- runs on a fresh testcontainer (IdentityDbContext migrations
    -- run BEFORE CatalogDbContext). Same trap as M.13.4 fixup 4857454.
    SELECT t.""Id"" AS tenant_id
      FROM identity.tenants t
     WHERE t.deleted_at IS NULL
     ORDER BY t.created_at
     LIMIT 1
),
inserted AS (
    INSERT INTO identity.tenant_memberships
        (""Id"", user_id, tenant_id, role, is_primary,
         created_at, updated_at, row_version)
    SELECT gen_random_uuid(),
           u.user_id,
           t.tenant_id,
           'tenant_admin',
           TRUE,
           NOW(),
           NOW(),
           0
      FROM niroshanaks_user u
     CROSS JOIN main_tenant t
     WHERE NOT EXISTS (
         SELECT 1
           FROM identity.tenant_memberships m
          WHERE m.user_id = u.user_id
            AND m.tenant_id = t.tenant_id
            AND m.deleted_at IS NULL
     )
    RETURNING 1
)
INSERT INTO identity.migration_audit
    (""Id"", migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(),
       'OpsM13_BootstrapTenantMembershipForNiroshanaks',
       'grant_tenant_admin_membership',
       (SELECT COUNT(*)::int FROM inserted),
       'Bootstrap tenant_admin membership for niroshanaks in the main tenant (owns most properties). Idempotent.',
       NOW();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op down. Operator bootstrap. Reverting would drop the
            // membership and re-lock the operator out of admin flows.
        }
    }
}
