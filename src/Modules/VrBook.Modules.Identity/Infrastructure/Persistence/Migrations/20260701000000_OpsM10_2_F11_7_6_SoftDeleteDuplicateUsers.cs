using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.10.2 F11.7.6.4 — one-shot data-heal that soft-deletes
    /// non-survivor duplicate user rows sharing an email.
    ///
    /// <para>Root cause: <c>identity.users</c> does NOT enforce email
    /// uniqueness (Slice 4 <c>DropEmailUnique</c>). Provisioning was keyed
    /// on <c>b2c_object_id</c>. Distinct sign-in paths (DevAuth persona
    /// oids, real-Entra oids) for the same human produced multiple rows
    /// sharing an email. F11.7.6.1+.2+.3 fixed the provisioning side.
    /// This migration heals existing multi-row state.</para>
    ///
    /// <para>Survivor precedence: <c>is_platform_admin DESC</c> →
    /// active membership count <c>DESC</c> → <c>created_at ASC</c>. The
    /// survivor keeps its oid; non-survivors are soft-deleted so their
    /// FK-shaped references (bookings.guest_user_id, reviews,
    /// messaging.threads, audit_log) still resolve as uuid.</para>
    ///
    /// <para>Bypasses <c>User.Deactivate()</c> intentionally — no
    /// <c>UserDeactivated</c> handlers exist in <c>src/Modules</c>, and
    /// <c>Deactivate</c> requires a non-nullable actorId. Raw SQL is
    /// correct for the one-shot heal. See F11.7.6 doc §F11.7.6.4.</para>
    /// </summary>
    public partial class OpsM10_2_F11_7_6_SoftDeleteDuplicateUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
WITH ranked AS (
    SELECT
        u.""Id"",
        ROW_NUMBER() OVER (
            PARTITION BY u.email
            ORDER BY u.is_platform_admin DESC,
                     (SELECT COUNT(*) FROM identity.tenant_memberships tm
                        WHERE tm.user_id = u.""Id"" AND tm.deleted_at IS NULL) DESC,
                     u.created_at ASC
        ) AS rn
    FROM identity.users u
    WHERE u.deleted_at IS NULL
)
UPDATE identity.users u
   SET deleted_at = NOW(),
       deleted_by = NULL,
       updated_at = NOW()
  FROM ranked r
 WHERE u.""Id"" = r.""Id"" AND r.rn > 1;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reverse: survivor merge is destructive at the
            // tenant-membership layer if per-tenant deduplication is ever
            // added. Reversibility strategy for staging is a pre-migration
            // pg_dump of identity.users + tenant_memberships. See F11.7.6
            // doc §F11.7.6.4 Down migration note.
        }
    }
}
