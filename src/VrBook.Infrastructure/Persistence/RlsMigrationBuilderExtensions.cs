using Microsoft.EntityFrameworkCore.Migrations;

namespace VrBook.Infrastructure.Persistence;

/// <summary>
/// Slice OPS.M.9 §3.4 + §4.9 (D9) — migration helpers that emit the
/// canonical RLS policy SQL for a tenant-scoped table. Each call enables
/// row-level security, forces it (so the table owner isn't exempt), and
/// creates the policy
/// <c>rls_{schema}_{table}_tenant_isolation</c>.
///
/// <para>For nullable-<c>tenant_id</c> tables (e.g.
/// <c>payment.webhook_events</c>, <c>identity.audit_log</c>,
/// <c>notifications.notification_log</c>) the policy gains an
/// <c>tenant_id IS NULL</c> branch so the bootstrap/orphan rows aren't
/// blocked.</para>
/// </summary>
public static class RlsMigrationBuilderExtensions
{
    /// <summary>
    /// Apply the §3.4 RLS template to <c>schema.table</c>. Idempotent at
    /// the migration-replay level: each statement uses <c>IF EXISTS</c>
    /// guards on the down path; the up path runs once per migration.
    /// </summary>
    public static MigrationBuilder EnableRlsTenantIsolation(
        this MigrationBuilder mb, string schema, string table, bool nullable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var qualified = $"\"{schema}\".\"{table}\"";
        var policyName = $"rls_{schema}_{table}_tenant_isolation";

        mb.Sql($"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;");
        mb.Sql($"ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY;");

        var nullClause = nullable ? "tenant_id IS NULL OR " : string.Empty;
        mb.Sql($@"
            CREATE POLICY {policyName} ON {qualified}
                USING (
                    {nullClause}tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    OR current_setting('app.is_platform_admin', true) = 'true'
                )
                WITH CHECK (
                    {nullClause}tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    OR current_setting('app.is_platform_admin', true) = 'true'
                );
        ");

        return mb;
    }

    /// <summary>
    /// Reverse of <see cref="EnableRlsTenantIsolation"/> for the migration
    /// <c>Down</c> path. Drops the policy and disables (not unforced) RLS.
    /// </summary>
    public static MigrationBuilder DropRlsTenantIsolation(
        this MigrationBuilder mb, string schema, string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var qualified = $"\"{schema}\".\"{table}\"";
        var policyName = $"rls_{schema}_{table}_tenant_isolation";

        mb.Sql($"DROP POLICY IF EXISTS {policyName} ON {qualified};");
        mb.Sql($"ALTER TABLE {qualified} DISABLE ROW LEVEL SECURITY;");
        return mb;
    }
}
