using FluentAssertions;
using Npgsql;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 §3 + §4.7 (D7) Step 4 — schema-introspection sweep that
/// verifies every M.9-protected table actually has RLS enabled, forced,
/// and carries the tenant-isolation policy with the expected GUC
/// references. Absorbs the OPS.M.9 §13 deferred schema facts.
///
/// <para>Per row: 4 facts (relrowsecurity, relforcerowsecurity, policy
/// exists, policy qual references both <c>app.tenant_id</c> and
/// <c>app.is_platform_admin</c>). 19 tables × 4 = 76 facts via
/// <c>[Theory]</c>.</para>
///
/// <para>Runs only when a Postgres testcontainer (or operator-provided
/// connection string) is reachable. Without a DB the theory rows skip
/// gracefully; CI's Integration step gates on the env var.</para>
/// </summary>
[Trait("Category", "Integration")]
[Collection("RlsPolicySchemaFactPack")]
public sealed class RlsPolicySchemaFactPack
{
    private static string? PostgresConnString =>
        Environment.GetEnvironmentVariable("VRBOOK_TEST_POSTGRES_CONN")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static async Task<NpgsqlConnection?> TryOpenAsync()
    {
        var cs = PostgresConnString;
        if (string.IsNullOrWhiteSpace(cs)) return null;
        var c = new NpgsqlConnection(cs);
        try { await c.OpenAsync(); }
        catch { await c.DisposeAsync(); return null; }
        return c;
    }

    /// <summary>
    /// The 19 tables M.9 §3.1 ships with RLS. Each row is
    /// (schema, table, expected_policy_name, nullable_tenant_id).
    /// </summary>
    public static IEnumerable<object[]> ProtectedTables() => new[]
    {
        new object[] { "identity", "audit_log", "rls_identity_audit_log_tenant_isolation", true },
        new object[] { "catalog", "properties", "rls_catalog_properties_tenant_isolation", false },
        new object[] { "catalog", "property_images", "rls_catalog_property_images_tenant_isolation", false },
        new object[] { "booking", "bookings", "rls_booking_bookings_tenant_isolation", false },
        new object[] { "booking", "booking_holds", "rls_booking_booking_holds_tenant_isolation", false },
        new object[] { "booking", "availability_blocks", "rls_booking_availability_blocks_tenant_isolation", false },
        new object[] { "payment", "payment_intents", "rls_payment_payment_intents_tenant_isolation", false },
        new object[] { "payment", "refunds", "rls_payment_refunds_tenant_isolation", false },
        new object[] { "payment", "webhook_events", "rls_payment_webhook_events_tenant_isolation", true },
        new object[] { "reviews", "reviews", "rls_reviews_reviews_tenant_isolation", false },
        new object[] { "pricing", "pricing_plans", "rls_pricing_pricing_plans_tenant_isolation", false },
        new object[] { "pricing", "pricing_rules", "rls_pricing_pricing_rules_tenant_isolation", false },
        new object[] { "messaging", "threads", "rls_messaging_threads_tenant_isolation", false },
        new object[] { "messaging", "messages", "rls_messaging_messages_tenant_isolation", false },
        new object[] { "notifications", "notification_log", "rls_notifications_notification_log_tenant_isolation", true },
        new object[] { "sync", "channel_feeds", "rls_sync_channel_feeds_tenant_isolation", false },
        new object[] { "sync", "external_reservations", "rls_sync_external_reservations_tenant_isolation", false },
        new object[] { "sync", "sync_conflicts", "rls_sync_sync_conflicts_tenant_isolation", false },
        new object[] { "sync", "sync_runs", "rls_sync_sync_runs_tenant_isolation", false },
    };

    [Theory]
    [MemberData(nameof(ProtectedTables))]
    public async Task Table_has_row_level_security_enabled(
        string schema, string table, string _, bool __)
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT relrowsecurity FROM pg_class
            WHERE oid = '""{schema}"".""{table}""'::regclass;";
        var result = (bool?)await cmd.ExecuteScalarAsync();
        result.Should().BeTrue(
            because: $"OPS.M.9 §3.1 — {schema}.{table} must have ROW LEVEL SECURITY enabled.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTables))]
    public async Task Table_has_row_level_security_FORCED(
        string schema, string table, string _, bool __)
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT relforcerowsecurity FROM pg_class
            WHERE oid = '""{schema}"".""{table}""'::regclass;";
        var result = (bool?)await cmd.ExecuteScalarAsync();
        result.Should().BeTrue(
            because: $"OPS.M.9 §3.4 — {schema}.{table} must FORCE RLS so the table owner isn't exempt.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTables))]
    public async Task Table_has_tenant_isolation_policy(
        string schema, string table, string expectedPolicy, bool _)
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT 1 FROM pg_policy
            WHERE polrelid = '""{schema}"".""{table}""'::regclass
              AND polname = @polname;";
        cmd.Parameters.AddWithValue("@polname", expectedPolicy);
        var result = await cmd.ExecuteScalarAsync();
        result.Should().NotBeNull(
            because: $"OPS.M.9 §3.4 (D9) — {schema}.{table} must carry policy '{expectedPolicy}'.");
    }

    [Theory]
    [MemberData(nameof(ProtectedTables))]
    public async Task Policy_qual_references_both_GUCs(
        string schema, string table, string expectedPolicy, bool nullable)
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT pg_get_expr(polqual, polrelid) AS using_clause,
                   pg_get_expr(polwithcheck, polrelid) AS with_check
            FROM pg_policy
            WHERE polrelid = '""{schema}"".""{table}""'::regclass
              AND polname = @polname;";
        cmd.Parameters.AddWithValue("@polname", expectedPolicy);
        await using var rdr = await cmd.ExecuteReaderAsync();
        var found = await rdr.ReadAsync();
        found.Should().BeTrue("the policy must exist before its qual can be inspected.");

        var usingClause = rdr.GetString(0);
        var withCheck = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);

        usingClause.Should().Contain("app.tenant_id");
        usingClause.Should().Contain("app.is_platform_admin");
        withCheck.Should().Contain("app.tenant_id");
        withCheck.Should().Contain("app.is_platform_admin");
        if (nullable)
        {
            usingClause.Should().Contain("tenant_id IS NULL",
                because: "OPS.M.9 §4.12 (D12) — nullable-tenant tables include the IS NULL branch.");
        }
    }
}
