using FluentAssertions;
using Npgsql;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// OPS.M.3 Step 7 — schema audit. After every module's Wave A/B/C migrations
/// have applied, asserts that:
///
///   * Every tenant-scoped table carries a <c>tenant_id uuid</c> column.
///   * Wave C tables are <c>NOT NULL</c>.
///   * Per-§1.4 / §1.6 / §1.7 carve-outs (<c>webhook_events</c>,
///     <c>notification_log</c>, <c>audit_log</c>) stay nullable.
///   * Every table has a cross-schema FK to <c>identity.tenants("Id")</c>
///     with <c>ON DELETE RESTRICT</c>.
///   * Every <c>tenant_id</c> column carries an index (RLS will use it in
///     OPS.M.9).
///
/// This test is the gate the architect plan §5.2 prescribes for Wave C:
/// it has to pass before the NOT NULL flip is considered safe to ship.
/// </summary>
[Collection(nameof(TenantIdRolloutCollection))]
public sealed class TenantIdRolloutTests
{
    private readonly TenantIdRolloutFixture _fixture;

    public TenantIdRolloutTests(TenantIdRolloutFixture fixture) => _fixture = fixture;

    private static readonly (string Schema, string Table, bool NotNull)[] NotNullExpectations = new[]
    {
        ("catalog", "properties", true),
        ("catalog", "property_images", true),
        ("reviews", "reviews", true),
        ("pricing", "pricing_plans", true),
        ("pricing", "pricing_rules", true),
        ("messaging", "threads", true),
        ("messaging", "messages", true),
        ("sync", "channel_feeds", true),
        ("sync", "external_reservations", true),
        ("sync", "sync_conflicts", true),
        ("sync", "sync_runs", true),
        ("payment", "payment_intents", true),
        ("payment", "refunds", true),
        ("payment", "webhook_events", false),
        ("booking", "bookings", true),
        ("booking", "booking_holds", true),
        ("booking", "availability_blocks", true),
        ("notifications", "notification_log", false),
        ("identity", "audit_log", false),
    };

    [Fact]
    public async Task Every_tenant_scoped_table_has_tenant_id_column_with_correct_nullability()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (var (schema, table, expectNotNull) in NotNullExpectations)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT is_nullable, data_type
                  FROM information_schema.columns
                 WHERE table_schema = @schema AND table_name = @table AND column_name = 'tenant_id';
                """;
            cmd.Parameters.AddWithValue("schema", schema);
            cmd.Parameters.AddWithValue("table", table);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue(
                $"{schema}.{table} must carry a tenant_id column (OPS.M.3 §1)");
            var isNullable = reader.GetString(0);
            var dataType = reader.GetString(1);

            dataType.Should().Be("uuid", $"{schema}.{table}.tenant_id must be uuid");
            if (expectNotNull)
            {
                isNullable.Should().Be("NO",
                    $"{schema}.{table}.tenant_id should be NOT NULL after Wave C");
            }
            else
            {
                isNullable.Should().Be("YES",
                    $"{schema}.{table}.tenant_id stays nullable per OPS_M_3_PLAN §1.4/§1.6/§1.7");
            }
        }
    }

    [Fact]
    public async Task Every_tenant_scoped_table_has_cross_schema_fk_to_identity_tenants()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        // Pull the FK list once and filter in-process — fewer round trips.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                tc.table_schema, tc.table_name, rc.delete_rule
              FROM information_schema.table_constraints tc
              JOIN information_schema.referential_constraints rc
                ON tc.constraint_name = rc.constraint_name
               AND tc.constraint_schema = rc.constraint_schema
              JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name
               AND tc.constraint_schema = kcu.constraint_schema
              JOIN information_schema.constraint_column_usage ccu
                ON rc.unique_constraint_name = ccu.constraint_name
               AND rc.unique_constraint_schema = ccu.constraint_schema
             WHERE tc.constraint_type = 'FOREIGN KEY'
               AND kcu.column_name = 'tenant_id'
               AND ccu.table_schema = 'identity'
               AND ccu.table_name = 'tenants';
            """;
        var fks = new Dictionary<(string, string), string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                fks[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
            }
        }

        foreach (var (schema, table, _) in NotNullExpectations)
        {
            fks.Should().ContainKey((schema, table),
                $"{schema}.{table} must declare a tenant_id FK to identity.tenants(\"Id\") (OPS_M_3_PLAN §3)");
            fks[(schema, table)].Should().Be("RESTRICT",
                $"{schema}.{table}.fk_*_tenant must use ON DELETE RESTRICT — CASCADE would let a tenant deletion silently wipe per-tenant data (Slice 3 convention).");
        }
    }

    [Fact]
    public async Task Every_tenant_id_column_has_an_index()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.nspname, c.relname
              FROM pg_index ix
              JOIN pg_class c ON c.oid = ix.indrelid
              JOIN pg_namespace n ON n.oid = c.relnamespace
              JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(ix.indkey)
             WHERE a.attname = 'tenant_id'
               AND n.nspname IN ('catalog', 'reviews', 'pricing', 'messaging', 'sync', 'payment', 'booking', 'notifications', 'identity');
            """;
        var indexed = new HashSet<(string, string)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexed.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var (schema, table, _) in NotNullExpectations)
        {
            indexed.Should().Contain((schema, table),
                $"{schema}.{table} must index tenant_id (OPS.M.9 RLS policies will filter on it).");
        }
    }

    [Fact]
    public async Task Default_tenant_seed_row_exists()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM identity.tenants WHERE "Id" = '00000000-0000-0000-0000-000000000001'::uuid;
            """;
        var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        count.Should().Be(1L,
            "OPS.M.1 seeds the default tenant; OPS.M.3 Wave B's backfill depends on this row.");
    }
}
