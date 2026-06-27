using FluentAssertions;
using Npgsql;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.5 Step 1 — RED tests pinning the post-migration shape of
/// <c>identity.tenants</c> per `docs/OPS_M_5_PLAN.md` §3.8 + §9.
///
/// <para>The plan: add <c>charges_enabled</c> and <c>payouts_enabled</c>
/// boolean columns, both <c>NOT NULL DEFAULT false</c>. Stripe surfaces these
/// two booleans on the connected account; the aggregate's
/// <c>UpdateStripeAccountReadiness(bool, bool)</c> method (Step 2) reads them
/// and auto-transitions <c>StatusPendingOnboarding</c> → <c>StatusActive</c>
/// when both are true.</para>
/// </summary>
[Collection(nameof(TenantIdRolloutCollection))]
public sealed class TenantStripeReadinessSchemaTests
{
    private readonly TenantIdRolloutFixture _fixture;

    public TenantStripeReadinessSchemaTests(TenantIdRolloutFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task tenants_has_charges_enabled_column_bool_not_null_default_false()
    {
        await AssertReadinessColumnAsync("charges_enabled");
    }

    [Fact]
    public async Task tenants_has_payouts_enabled_column_bool_not_null_default_false()
    {
        await AssertReadinessColumnAsync("payouts_enabled");
    }

    private async Task AssertReadinessColumnAsync(string columnName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT data_type, is_nullable, column_default
              FROM information_schema.columns
             WHERE table_schema = 'identity'
               AND table_name   = 'tenants'
               AND column_name  = @col;
            """;
        cmd.Parameters.AddWithValue("col", columnName);

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue(
            $"identity.tenants.{columnName} must exist after OPS.M.5 Step 1.");

        reader.GetString(0).Should().Be("boolean",
            $"{columnName} must be boolean to match Stripe's account.capabilities flags.");
        reader.GetString(1).Should().Be("NO",
            $"{columnName} must be NOT NULL — Stripe always returns a definite true/false.");

        var defaultExpr = reader.IsDBNull(2) ? null : reader.GetString(2);
        defaultExpr.Should().NotBeNull(
            $"{columnName} must declare a DEFAULT so the migration can add the column without backfill.");
        defaultExpr!.Should().Contain("false",
            $"{columnName} default must be false — new tenants haven't onboarded Stripe yet.");
    }
}
