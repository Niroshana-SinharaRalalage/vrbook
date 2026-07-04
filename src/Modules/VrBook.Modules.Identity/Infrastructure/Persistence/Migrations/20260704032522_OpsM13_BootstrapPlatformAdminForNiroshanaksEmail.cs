using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.13.6 walk fix — emergency operator bootstrap via direct SQL.
    ///
    /// <para>bootstrap-operator endpoint kept returning 404 in staging despite
    /// multiple diagnostic pushes. Direct SQL via migration is the
    /// least-heroic path to unblock the walk.</para>
    ///
    /// <para>Grants <c>is_platform_admin = true</c> to every active
    /// <c>identity.users</c> row whose email contains <c>niroshanaks</c>
    /// (case-insensitive) — covers real-Entra sign-ins, pre-fix
    /// <c>@unknown.local</c> synthetic emails, and any case variants from the
    /// M.13.4 backfill. Idempotent (WHERE ... = FALSE) — re-run is a no-op.</para>
    /// </summary>
    public partial class OpsM13_BootstrapPlatformAdminForNiroshanaksEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
WITH promoted AS (
    UPDATE identity.users
       SET is_platform_admin = TRUE,
           updated_at = NOW()
     WHERE deleted_at IS NULL
       AND is_platform_admin = FALSE
       AND lower(email) LIKE '%niroshanaks%'
    RETURNING ""Id""
)
INSERT INTO identity.migration_audit
    (""Id"", migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(),
       'OpsM13_BootstrapPlatformAdminForNiroshanaksEmail',
       'grant_platform_admin',
       (SELECT COUNT(*)::int FROM promoted),
       'Bootstrap operator PA grant for niroshanaks (case-insensitive substring match). Idempotent.',
       NOW();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op down. Operator bootstrap, not a schema change; reverting
            // would surprise-drop the operator's PA in a way migrations aren't
            // supposed to do. If it needs to be reverted, do it manually.
        }
    }
}
