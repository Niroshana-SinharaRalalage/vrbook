using FluentAssertions;
using Npgsql;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 §3 + §4.7 (D7) Step 4 — verifies the OPS.M.9 §3.2
/// carve-out tables do NOT have RLS enabled. Catches a regression where a
/// future migration accidentally adds RLS to a table that needs to remain
/// cross-tenant readable (e.g. the outbox).
/// </summary>
[Trait("Category", "Integration")]
[Collection("RlsCarveOutSchemaFactPack")]
public sealed class RlsCarveOutSchemaFactPack
{
    private static string? PostgresConnString =>
        Environment.GetEnvironmentVariable("VRBOOK_TEST_POSTGRES_CONN")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static async Task<NpgsqlConnection?> TryOpenAsync()
    {
        var cs = PostgresConnString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            return null;
        }

        var c = new NpgsqlConnection(cs);
        try { await c.OpenAsync(); }
        catch { await c.DisposeAsync(); return null; }
        return c;
    }

    /// <summary>OPS.M.9 §3.2 carve-out tables.</summary>
    public static IEnumerable<object[]> CarveOutTables() => new[]
    {
        // Identity carve-outs (per M.9 §3.2 rows 1-3, 12)
        new object[] { "identity", "users" },
        new object[] { "identity", "tenants" },
        new object[] { "identity", "tenant_memberships" },
        // Outbox carve-outs (per M.9 §3.2 rows 4-12)
        new object[] { "catalog", "outbox_messages" },
        new object[] { "booking", "outbox_messages" },
        new object[] { "payment", "outbox_messages" },
        new object[] { "reviews", "outbox_messages" },
        new object[] { "pricing", "outbox_messages" },
        new object[] { "messaging", "outbox_messages" },
        new object[] { "notifications", "outbox_messages" },
        new object[] { "sync", "outbox_messages" },
        new object[] { "identity", "outbox_messages" },
        // Reference data
        new object[] { "catalog", "amenities" },
    };

    [Theory]
    [MemberData(nameof(CarveOutTables))]
    public async Task CarveOut_table_does_NOT_have_row_level_security_enabled(
        string schema, string table)
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
        {
            return;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT relrowsecurity FROM pg_class
            WHERE oid = '""{schema}"".""{table}""'::regclass;";
        try
        {
            var result = (bool?)await cmd.ExecuteScalarAsync();
            result.Should().BeFalse(
                because: $"OPS.M.9 §3.2 — {schema}.{table} is intentionally carved out of RLS. " +
                         "Adding RLS here would break the cross-module / cross-tenant read paths " +
                         "that the app-layer (M.4 + M.8) is responsible for gating.");
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // 42P01 = undefined_table. Some carve-outs (amenities) only exist
            // after specific migrations; if the table isn't present the
            // carve-out invariant is vacuously satisfied.
        }
    }
}
