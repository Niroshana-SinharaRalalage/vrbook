using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Slice5b_DevAuth_Default_Tenant_Membership : Migration
    {
        // OPS.M.2 — DB-wins precedence (per docs/OPS_M_2_PLAN.md §2.7 revised).
        // tenant_memberships is the sole source of truth for app_tenant_id;
        // DevAuthHandler does NOT stamp the claim directly. This migration
        // bridges the gap for DevAuth personas (Owner, Admin) by seeding
        // membership rows that point at the default tenant from OPS.M.1.
        //
        // The DevAuth Guest persona is NOT seeded - guests are tenant-less
        // per MULTI_TENANCY_OPS_PLAN.md §1.
        //
        // The seed uses a NOT EXISTS guard so:
        //   * It is safe to run before the dev-owner / dev-admin users have
        //     ever signed in (ProvisionUserCommand creates the users.row on
        //     first DevAuth request). When the user rows are missing, the
        //     INSERT-SELECT returns zero rows and the migration is a no-op.
        //   * Re-running this migration after the rows already exist is a
        //     no-op (the NOT EXISTS clause matches the existing live row).
        //   * Soft-deleted memberships are NOT counted by NOT EXISTS, so a
        //     re-seed after a Revoke() will re-create a fresh row.
        //
        // Prod safety: DevAuth is disabled in prod (Bicep be897bc). No users
        // with b2c_object_id like 'dev-*' will ever exist there, so the
        // INSERT-SELECT returns zero rows. The migration is a no-op in prod.

        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";
        private const string DevOwnerOid = "dev-owner-00000000";
        private const string DevAdminOid = "dev-admin-00000002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                INSERT INTO identity.tenant_memberships
                    (""Id"", user_id, tenant_id, role, is_primary,
                     created_at, updated_at, row_version)
                SELECT
                    gen_random_uuid(),
                    u.""Id"",
                    '{DefaultTenantId}'::uuid,
                    'tenant_admin', true,
                    NOW(), NOW(), 0
                  FROM identity.users u
                 WHERE u.b2c_object_id IN ('{DevOwnerOid}', '{DevAdminOid}')
                   AND NOT EXISTS (
                     SELECT 1 FROM identity.tenant_memberships m
                      WHERE m.user_id = u.""Id""
                        AND m.tenant_id = '{DefaultTenantId}'::uuid
                        AND m.deleted_at IS NULL
                   );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM identity.tenant_memberships m
                 USING identity.users u
                 WHERE m.user_id = u.""Id""
                   AND u.b2c_object_id IN ('{DevOwnerOid}', '{DevAdminOid}')
                   AND m.tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
