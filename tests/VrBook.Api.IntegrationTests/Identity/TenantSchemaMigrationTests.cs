using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VrBook.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// OPS.M.1 — verify the Slice5_Tenant_Membership_Schema migration applied
/// cleanly: extended columns exist on identity.tenants, identity.tenant_memberships
/// exists with the right indexes + CHECK constraints, and the default tenant
/// seed row is present.
///
/// These tests run against the Testcontainer Postgres started by
/// <see cref="IdentityApiFixture"/> — migrations apply during fixture init, so
/// the assertions below are read-only schema inspections.
/// </summary>
[Collection(nameof(IdentityApiCollection))]
public sealed class TenantSchemaMigrationTests
{
    private readonly IdentityApiFixture _fixture;

    public TenantSchemaMigrationTests(IdentityApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migration_creates_extended_tenants_columns()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var columns = new HashSet<string>(StringComparer.Ordinal);
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT column_name FROM information_schema.columns
             WHERE table_schema = 'identity' AND table_name = 'tenants';
            """;
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        columns.Should().Contain(new[]
        {
            "status",
            "default_currency",
            "default_timezone",
            "support_email",
            "platform_fee_bps",
            "stripe_account_id",
            "stripe_account_status",
            "suspended_reason",
        });
    }

    [Fact]
    public async Task Migration_creates_tenant_memberships_table_with_expected_columns()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT column_name FROM information_schema.columns
             WHERE table_schema = 'identity' AND table_name = 'tenant_memberships';
            """;
        var columns = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        columns.Should().Contain(new[]
        {
            "Id", "user_id", "tenant_id", "role", "is_primary", "row_version",
            "created_at", "created_by", "updated_at", "updated_by",
            "deleted_at", "deleted_by",
        });
    }

    [Fact]
    public async Task Migration_creates_partial_unique_index_on_membership_user_tenant()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT indexdef FROM pg_indexes
             WHERE schemaname = 'identity'
               AND tablename = 'tenant_memberships'
               AND indexname = 'ux_tenant_memberships_user_tenant';
            """;
        var indexDef = (string?)await cmd.ExecuteScalarAsync();
        indexDef.Should().NotBeNull()
            .And.Contain("UNIQUE")
            .And.Contain("user_id")
            .And.Contain("tenant_id")
            .And.Contain("deleted_at IS NULL");
    }

    [Fact]
    public async Task Migration_creates_check_constraints_for_status_and_role()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT con.conname, pg_get_constraintdef(con.oid)
              FROM pg_constraint con
              JOIN pg_class cls   ON cls.oid = con.conrelid
              JOIN pg_namespace ns ON ns.oid = cls.relnamespace
             WHERE ns.nspname = 'identity'
               AND con.contype = 'c'
               AND con.conname IN ('ck_tenants_status','ck_tenant_memberships_role');
            """;
        var defs = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                defs[reader.GetString(0)] = reader.GetString(1);
            }
        }

        defs.Should().ContainKey("ck_tenants_status")
            .WhoseValue.Should().Contain("PendingOnboarding")
            .And.Contain("Active")
            .And.Contain("Suspended")
            .And.Contain("Closed");

        defs.Should().ContainKey("ck_tenant_memberships_role")
            .WhoseValue.Should().Contain("tenant_admin")
            .And.Contain("tenant_member");
    }

    [Fact]
    public async Task Migration_seeds_default_tenant_row()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", slug, display_name, status, default_currency, default_timezone, platform_fee_bps
              FROM identity.tenants
             WHERE "Id" = '00000000-0000-0000-0000-000000000001'::uuid;
            """;
        using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the OPS.M.1 migration seeds the default tenant");

        reader.GetGuid(0).Should().Be(new Guid("00000000-0000-0000-0000-000000000001"));
        reader.GetString(1).Should().Be("default");
        reader.GetString(2).Should().Be("VrBook Default");
        reader.GetString(3).Should().Be("Active");
        reader.GetString(4).Should().Be("USD");
        reader.GetString(5).Should().Be("UTC");
        reader.GetInt32(6).Should().Be(1500);
    }

    [Fact]
    public async Task Membership_unique_index_blocks_duplicate_active_rows()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var userId = Guid.NewGuid();
        var tenantId = new Guid("00000000-0000-0000-0000-000000000001");

        // Seed a user row (FK target). DB defaults handle the audit columns
        // since IDateTimeProvider isn't in this raw-SQL path.
        var seedUser = connection.CreateCommand();
        seedUser.CommandText = $"""
            INSERT INTO identity.users
                ("Id", email, display_name, phone,
                 email_verified, created_at, updated_at, row_version)
            VALUES
                ('{userId}', 'unique-test-{userId}@vrbook.test',
                 'Unique Test', '+10000000000', false, NOW(), NOW(), 0);
            """;
        await seedUser.ExecuteNonQueryAsync();

        async Task<int> InsertMembership(Guid id)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                INSERT INTO identity.tenant_memberships
                    ("Id", user_id, tenant_id, role, is_primary,
                     created_at, updated_at, row_version)
                VALUES
                    ('{id}', '{userId}', '{tenantId}', 'tenant_admin', false,
                     NOW(), NOW(), 0);
                """;
            return await cmd.ExecuteNonQueryAsync();
        }

        var firstId = Guid.NewGuid();
        (await InsertMembership(firstId)).Should().Be(1);

        // Second insert with the same (user, tenant) should violate the partial unique.
        var act = async () => await InsertMembership(Guid.NewGuid());
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "23505"); // unique_violation

        // Soft-delete the first row, then re-insert — should succeed.
        var softDelete = connection.CreateCommand();
        softDelete.CommandText = $"""
            UPDATE identity.tenant_memberships
               SET deleted_at = NOW()
             WHERE "Id" = '{firstId}';
            """;
        await softDelete.ExecuteNonQueryAsync();

        (await InsertMembership(Guid.NewGuid())).Should().Be(1,
            "soft-deleted membership is filtered out of the partial unique index");

        // Cleanup
        var cleanup = connection.CreateCommand();
        cleanup.CommandText = $"""
            DELETE FROM identity.tenant_memberships WHERE user_id = '{userId}';
            DELETE FROM identity.users WHERE "Id" = '{userId}';
            """;
        await cleanup.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Role_check_constraint_rejects_unknown_role()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        var userId = Guid.NewGuid();
        var tenantId = new Guid("00000000-0000-0000-0000-000000000001");

        // Seed a fresh user row to satisfy the FK.
        var seedUser = connection.CreateCommand();
        seedUser.CommandText = $"""
            INSERT INTO identity.users
                ("Id", email, display_name, phone,
                 email_verified, created_at, updated_at, row_version)
            VALUES
                ('{userId}', 'role-check-{userId}@vrbook.test',
                 'Role Check', '+10000000000', false, NOW(), NOW(), 0);
            """;
        await seedUser.ExecuteNonQueryAsync();

        var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO identity.tenant_memberships
                ("Id", user_id, tenant_id, role, is_primary,
                 created_at, updated_at, row_version)
            VALUES
                ('{Guid.NewGuid()}', '{userId}', '{tenantId}', 'hacker', false,
                 NOW(), NOW(), 0);
            """;
        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>()
            .Where(ex => ex.SqlState == "23514"); // check_violation

        var cleanup = connection.CreateCommand();
        cleanup.CommandText = $"""DELETE FROM identity.users WHERE "Id" = '{userId}';""";
        await cleanup.ExecuteNonQueryAsync();
    }
}
