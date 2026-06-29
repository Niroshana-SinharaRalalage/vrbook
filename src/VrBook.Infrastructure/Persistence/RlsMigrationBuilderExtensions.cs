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

    /// <summary>
    /// Slice OPS.M.9.1 §1.3 — public-read carve-out. Adds a SECOND
    /// PERMISSIVE policy on the table that allows SELECT for anonymous
    /// callers when <paramref name="usingPredicate"/> holds.
    ///
    /// <para>Postgres OR-combines all PERMISSIVE policies for the same
    /// command, so a row visible via EITHER the existing
    /// <c>rls_{schema}_{table}_tenant_isolation</c> policy OR this carve-out
    /// is returned. Tenant-internal callers keep seeing all their own rows
    /// (active + inactive + deleted); anonymous callers see only rows
    /// matching the carve-out predicate. The existing tenant-isolation
    /// policy is left UNCHANGED — fully backward-compatible.</para>
    ///
    /// <para>The carve-out is <c>USING</c>-only — NO <c>WITH CHECK</c>.
    /// Writes (INSERT/UPDATE/DELETE) still require the existing tenant
    /// policy's WITH CHECK to pass, so anonymous callers cannot inject
    /// rows even though they can SELECT them.</para>
    ///
    /// <para>The new policy name is <c>rls_{schema}_{table}_public_read</c>.</para>
    /// </summary>
    /// <param name="mb">The migration builder being extended.</param>
    /// <param name="schema">The Postgres schema name (e.g. <c>"catalog"</c>).</param>
    /// <param name="table">The table name within the schema.</param>
    /// <param name="usingPredicate">
    /// The raw SQL predicate (no leading <c>WHERE</c>) that determines
    /// which rows are visible publicly. The caller is responsible for SQL-
    /// safety — pass only static, statically-known expressions (column
    /// references + literals + <c>EXISTS</c> subqueries). Examples:
    /// <c>"is_active = true AND deleted_at IS NULL"</c>;
    /// <c>"EXISTS (SELECT 1 FROM catalog.properties p WHERE p.id = property_id AND p.is_active AND p.deleted_at IS NULL)"</c>.
    /// </param>
    public static MigrationBuilder EnablePublicReadCarveOut(
        this MigrationBuilder mb, string schema, string table, string usingPredicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(usingPredicate);

        var qualified = $"\"{schema}\".\"{table}\"";
        var policyName = $"rls_{schema}_{table}_public_read";

        // Predicate trusted by contract (migration code is engineering input,
        // not user input). PERMISSIVE + USING-only + FOR SELECT keeps writes
        // gated by the existing tenant-isolation policy's WITH CHECK clause.
        mb.Sql($@"
            CREATE POLICY {policyName} ON {qualified}
                AS PERMISSIVE
                FOR SELECT
                USING ({usingPredicate});
        ");

        return mb;
    }

    /// <summary>
    /// Reverse of <see cref="EnablePublicReadCarveOut"/> for the migration
    /// <c>Down</c> path. Drops the public-read policy. The original
    /// tenant-isolation policy is unaffected.
    /// </summary>
    public static MigrationBuilder DropPublicReadCarveOut(
        this MigrationBuilder mb, string schema, string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var qualified = $"\"{schema}\".\"{table}\"";
        var policyName = $"rls_{schema}_{table}_public_read";

        mb.Sql($"DROP POLICY IF EXISTS {policyName} ON {qualified};");
        return mb;
    }
}
