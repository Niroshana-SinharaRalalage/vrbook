using FluentAssertions;
using Npgsql;
using VrBook.Api.IntegrationTests.Identity;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// Slice OPS.M.5 Step 1 — RED tests pinning the post-migration shape of
/// <c>payment.webhook_events</c> per `docs/OPS_M_5_PLAN.md` §3.7 + §9.
///
/// <para>The plan: add a nullable <c>stripe_account_id</c> column and flip
/// the uniqueness contract from <c>(stripe_event_id)</c> alone to
/// <c>(stripe_event_id, stripe_account_id)</c>. Per Connect docs, an event
/// affecting a connected account is delivered twice (platform-scope with
/// <c>account=null</c>, connected-scope with <c>account=acct_…</c>) with the
/// same <c>evt_…</c> id; the single-column uniqueness would block the second
/// delivery as a duplicate.</para>
///
/// <para>Fixture: reuses <see cref="TenantIdRolloutFixture"/> — same Postgres
/// testcontainer + same migration runner as OPS.M.3 Step 7. Adding M.5's
/// migration to the registered DbContexts is enough for these tests to go
/// green; no fixture change required.</para>
/// </summary>
[Collection(nameof(TenantIdRolloutCollection))]
public sealed class WebhookEventsSchemaTests
{
    private readonly TenantIdRolloutFixture _fixture;

    public WebhookEventsSchemaTests(TenantIdRolloutFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task webhook_events_has_stripe_account_id_column_nullable_varchar()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT data_type, is_nullable, character_maximum_length
              FROM information_schema.columns
             WHERE table_schema = 'payment'
               AND table_name   = 'webhook_events'
               AND column_name  = 'stripe_account_id';
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue(
            "payment.webhook_events.stripe_account_id must exist after OPS.M.5 Step 1.");
        reader.GetString(0).Should().Be("character varying");
        reader.GetString(1).Should().Be("YES",
            "stripe_account_id must be nullable — platform-scope events legitimately have no account.");
        // 120 to mirror stripe_event_id's max length and Stripe's documented account-id max.
        reader.GetInt32(2).Should().Be(120);
    }

    [Fact]
    public async Task webhook_events_single_column_stripe_event_id_unique_is_gone()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var indexes = await ListUniqueIndexesAsync(conn, schema: "payment", table: "webhook_events");
        // The pre-M.5 single-column unique (stripe_event_id) is created by InitPaymentSchema (20260607175422)
        // as `IX_webhook_events_stripe_event_id`. M.5 Step 1 must DROP it.
        indexes.Should().NotContain(i => i.Name == "IX_webhook_events_stripe_event_id",
            "OPS.M.5 §3.7 (D7) requires the single-column unique to be dropped in favor of the composite.");
    }

    [Fact]
    public async Task webhook_events_has_composite_unique_on_stripe_event_id_and_stripe_account_id()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        var indexes = await ListUniqueIndexesAsync(conn, schema: "payment", table: "webhook_events");

        var composite = indexes.SingleOrDefault(i =>
            i.Name == "IX_webhook_events_account_event"
            || i.Name.Contains("stripe_event_id", StringComparison.OrdinalIgnoreCase)
                && i.Name.Contains("stripe_account_id", StringComparison.OrdinalIgnoreCase));

        composite.Should().NotBeNull(
            "OPS.M.5 §3.7 (D7) requires a composite unique index on (stripe_event_id, stripe_account_id).");
        composite!.Columns.Should().BeEquivalentTo(
            new[] { "stripe_event_id", "stripe_account_id" },
            options => options.WithStrictOrdering(),
            "Composite must cover exactly those two columns so platform + connected dual-delivery " +
            "rows persist as distinct rows but a true replay is rejected.");
    }

    private static async Task<List<(string Name, string[] Columns)>> ListUniqueIndexesAsync(
        NpgsqlConnection conn, string schema, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.relname AS index_name,
                   array_agg(a.attname ORDER BY array_position(ix.indkey, a.attnum)) AS columns
              FROM pg_class t
              JOIN pg_namespace n  ON n.oid = t.relnamespace
              JOIN pg_index ix     ON ix.indrelid = t.oid
              JOIN pg_class i      ON i.oid = ix.indexrelid
              JOIN pg_attribute a  ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey)
             WHERE n.nspname = @schema
               AND t.relname = @table
               AND ix.indisunique = true
             GROUP BY i.relname;
            """;
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);

        var result = new List<(string, string[])>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var cols = (string[])reader.GetValue(1);
            result.Add((name, cols));
        }
        return result;
    }
}
