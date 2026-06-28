using FluentAssertions;
using Npgsql;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.8 §3.1 (D1) Step 1 — pins the <c>identity.users.is_platform_admin</c>
/// schema. The runtime auth gate trusts this column (the OPS.M.2 ADR-0014
/// DB-wins precedence makes it authoritative); a future migration that drops
/// the column or relaxes <c>NOT NULL</c> would silently disable the bypass.
///
/// <para>Tests live in the Postgres-fixture project so they run only in CI's
/// integration step. The collection name matches the existing
/// <c>TenantIdRolloutFixture</c> pattern; if the fixture isn't reachable the
/// theory is skipped, not failed.</para>
/// </summary>
[Trait("Category", "Integration")]
[Collection("PlatformAdminSchema")]
public sealed class UsersIsPlatformAdminSchemaTests
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
        try
        {
            await c.OpenAsync();
        }
        catch
        {
            await c.DisposeAsync();
            return null;
        }
        return c;
    }

    [Fact]
    public async Task Column_is_platform_admin_exists_with_boolean_not_null()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return; // CI-only

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users'
              AND column_name = 'is_platform_admin';
            """;
        await using var rdr = await cmd.ExecuteReaderAsync();
        rdr.Read().Should().BeTrue("the column must exist after OpsM8a migration.");
        rdr.GetString(0).Should().Be("boolean");
        rdr.GetString(1).Should().Be("NO");
    }

    [Fact]
    public async Task Column_default_value_is_false()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT column_default
            FROM information_schema.columns
            WHERE table_schema = 'identity' AND table_name = 'users'
              AND column_name = 'is_platform_admin';
            """;
        var defaultVal = (string?)await cmd.ExecuteScalarAsync();
        defaultVal.Should().NotBeNull();
        defaultVal!.ToLowerInvariant().Should().Contain("false");
    }

    [Fact]
    public async Task Partial_index_for_true_values_exists()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null) return;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'identity' AND tablename = 'users'
              AND indexname = 'ix_users_is_platform_admin';
            """;
        var def = (string?)await cmd.ExecuteScalarAsync();
        def.Should().NotBeNull("partial index ix_users_is_platform_admin is required.");
        def!.Should().Contain("is_platform_admin", "must index the platform-admin column");
        def.Should().MatchRegex(@"WHERE\s+\(?is_platform_admin\s*=?\s*true",
            because: "OPS.M.8 §3.1 — partial index over rows where is_platform_admin = true keeps the index tiny.");
    }
}
