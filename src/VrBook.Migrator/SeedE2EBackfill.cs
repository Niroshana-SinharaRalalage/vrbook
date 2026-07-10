using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Migrator;

/// <summary>
/// Slice OPS.2.2 — Bicep-declarative backfill for the Playwright E2E test
/// fixtures. Reads <c>Bootstrap:E2e:Enabled</c> from configuration (set by
/// Bicep's <c>bootstrapE2eTenantEnabled</c> param — <c>true</c> on staging,
/// <c>false</c> on prod) and, when enabled, idempotently provisions the
/// isolated <c>e2e-tenant</c> plus the two PRE-SEEDED admin personas the
/// suite drives through real Entra CIAM sign-in:
///
/// <list type="bullet">
///   <item><c>e2e-tenant</c> — <c>identity.tenants</c> row with
///     <c>is_e2e = TRUE</c>, <c>status = Active</c>, and Stripe readiness
///     flags forced <c>TRUE</c> so owner booking/payment flows aren't gated
///     on a live Stripe Connect onboarding. The E2E suite namespaces all its
///     data under this tenant so nightly runs never touch real rows.</item>
///   <item><c>e2e-owner@vrbook.test</c> — pre-seeded user
///     (<c>pre_seeded_at = NOW()</c>) + a <c>tenant_admin</c>
///     <c>tenant_memberships</c> row on <c>e2e-tenant</c> (<c>is_primary =
///     TRUE</c>). Drives the owner / tenant-admin scenarios.</item>
///   <item><c>e2e-platform-admin@vrbook.test</c> — pre-seeded user with
///     <c>is_platform_admin = TRUE</c>. Drives the platform-admin scenarios.
///     Admin personas MUST be pre-seeded per ADR-0017 — the middleware
///     admin-gate 401s any admin whose row is absent.</item>
/// </list>
///
/// <para><b>Guest persona is deliberately NOT seeded.</b>
/// <c>e2e-guest@vrbook.test</c> is a guest surface — it lazy-provisions on
/// first sign-in via <c>UserProvisioningMiddleware</c> Branch 3, exactly like
/// a real guest. Seeding it here would test a code path guests never hit.</para>
///
/// <para><b>Idempotency:</b> every entity does a read-then-write inside the
/// single wrapping transaction. Tenant looked up by slug <c>FOR UPDATE</c>;
/// users by <c>lower(email)</c> against the partial-unique
/// <c>users_email_active_lower_uq</c>; membership by <c>(user_id, tenant_id)</c>
/// against <c>ux_tenant_memberships_user_tenant</c>. <c>pre_seeded_at</c> is
/// never overwritten on an existing row (audit trail preserved). Safe on
/// every deploy.</para>
///
/// <para><b>Config shape</b> (env var on the migrator Container App Job):
/// <c>Bootstrap__E2e__Enabled=true</c>. Absent / <c>false</c> = no-op.</para>
///
/// Mirrors <see cref="SeedPlatformAdminsBackfill"/>; runs AFTER every module's
/// <c>MigrateAsync</c> so the OPS.2.2 <c>is_e2e</c> column and the M.22
/// <c>pre_seeded_at</c> column both exist.
/// </summary>
public sealed class SeedE2EBackfill(
    IdentityDbContext db,
    IConfiguration configuration,
    ILogger<SeedE2EBackfill> logger)
{
    private const string TenantSlug = "e2e-tenant";
    private const string TenantDisplayName = "E2E Test Tenant";
    private const string OwnerEmail = "e2e-owner@vrbook.test";
    private const string OwnerDisplayName = "E2E Owner";
    private const string PlatformAdminEmail = "e2e-platform-admin@vrbook.test";
    private const string PlatformAdminDisplayName = "E2E Platform Admin";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var enabled = configuration.GetValue<bool>("Bootstrap:E2e:Enabled");
        if (!enabled)
        {
            logger.LogInformation(
                "OPS.2.2 e2e backfill skipped — Bootstrap:E2e:Enabled is false or missing.");
            return;
        }

        logger.LogInformation("OPS.2.2 e2e backfill running (Bootstrap:E2e:Enabled=true).");

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        // One transaction so a partial failure doesn't leave the environment
        // with a half-seeded e2e fixture (e.g. tenant but no owner membership).
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var tenantId = await UpsertE2eTenantAsync(conn, cancellationToken);
            var ownerId = await UpsertPreSeededUserAsync(
                conn, OwnerEmail, OwnerDisplayName, isPlatformAdmin: false, cancellationToken);
            await UpsertTenantAdminMembershipAsync(conn, ownerId, tenantId, cancellationToken);
            await UpsertPreSeededUserAsync(
                conn, PlatformAdminEmail, PlatformAdminDisplayName, isPlatformAdmin: true, cancellationToken);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                cmd.CommandText = @"
INSERT INTO identity.migration_audit
    (""Id"", migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(),
        'OpsM2_SeedE2EBackfill',
        'bicep-declarative-e2e-fixture',
        3,
        @notes,
        NOW())";
                var p = cmd.CreateParameter();
                p.ParameterName = "notes";
                p.Value = $"tenant={TenantSlug} owner={OwnerEmail} platform_admin={PlatformAdminEmail}";
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            logger.LogInformation(
                "OPS.2.2 e2e backfill committed. tenant={TenantId} owner={OwnerId}", tenantId, ownerId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex,
                "OPS.2.2 e2e backfill failed and rolled back. Fix DB state and re-run the migrator.");
            throw;
        }
    }

    private async Task<Guid> UpsertE2eTenantAsync(
        System.Data.Common.DbConnection conn, CancellationToken cancellationToken)
    {
        Guid? existingId = null;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT ""Id"" FROM identity.tenants
                                 WHERE deleted_at IS NULL AND slug = @slug
                                 FOR UPDATE";
            AddParam(read, "slug", TenantSlug);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                existingId = r.GetGuid(0);
            }
        }

        if (existingId is not null)
        {
            // Ensure the marker + active-readiness flags hold; never resurrect
            // a suspended/closed tenant, but keep the e2e marker true.
            await using var upd = conn.CreateCommand();
            upd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            upd.CommandText = @"UPDATE identity.tenants
                                SET is_e2e = TRUE, updated_at = NOW()
                                WHERE ""Id"" = @id AND (is_e2e IS DISTINCT FROM TRUE)";
            AddParam(upd, "id", existingId.Value);
            await upd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("OPS.2.2 e2e-tenant already exists (id={TenantId}).", existingId);
            return existingId.Value;
        }

        var newId = Guid.NewGuid();
        await using (var ins = conn.CreateCommand())
        {
            ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            ins.CommandText = @"
INSERT INTO identity.tenants
    (""Id"", slug, display_name, status, default_currency, default_timezone,
     support_email, platform_fee_bps, charges_enabled, payouts_enabled,
     is_e2e, row_version, created_at, updated_at)
VALUES (@id, @slug, @displayName, 'Active', 'USD', 'UTC',
        @supportEmail, 1500, TRUE, TRUE,
        TRUE, 0, NOW(), NOW())";
            AddParam(ins, "id", newId);
            AddParam(ins, "slug", TenantSlug);
            AddParam(ins, "displayName", TenantDisplayName);
            AddParam(ins, "supportEmail", OwnerEmail);
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
        logger.LogInformation("OPS.2.2 inserted e2e-tenant (id={TenantId}).", newId);
        return newId;
    }

    private async Task<Guid> UpsertPreSeededUserAsync(
        System.Data.Common.DbConnection conn,
        string email,
        string displayName,
        bool isPlatformAdmin,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        Guid? existingId = null;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT ""Id"" FROM identity.users
                                 WHERE deleted_at IS NULL AND lower(email) = @email
                                 FOR UPDATE";
            AddParam(read, "email", normalizedEmail);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            if (await r.ReadAsync(cancellationToken))
            {
                existingId = r.GetGuid(0);
            }
        }

        if (existingId is not null)
        {
            // Ensure the platform-admin flag holds where required; never touch
            // pre_seeded_at (audit trail is immutable).
            if (isPlatformAdmin)
            {
                await using var upd = conn.CreateCommand();
                upd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                upd.CommandText = @"UPDATE identity.users
                                    SET is_platform_admin = TRUE, updated_at = NOW()
                                    WHERE ""Id"" = @id AND is_platform_admin = FALSE";
                AddParam(upd, "id", existingId.Value);
                await upd.ExecuteNonQueryAsync(cancellationToken);
            }
            logger.LogInformation(
                "OPS.2.2 e2e user {Email} already exists (id={UserId}).", normalizedEmail, existingId);
            return existingId.Value;
        }

        var newId = Guid.NewGuid();
        await using (var ins = conn.CreateCommand())
        {
            ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            ins.CommandText = @"
INSERT INTO identity.users
    (""Id"", email, display_name, phone, is_platform_admin, email_verified,
     row_version, created_at, updated_at, pre_seeded_at)
VALUES (@id, @email, @displayName, '', @isPa, TRUE, 0, NOW(), NOW(), NOW())";
            AddParam(ins, "id", newId);
            AddParam(ins, "email", normalizedEmail);
            AddParam(ins, "displayName", displayName);
            AddParam(ins, "isPa", isPlatformAdmin);
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }
        logger.LogInformation(
            "OPS.2.2 inserted pre-seeded e2e user {Email} (id={UserId}, pa={Pa}).",
            normalizedEmail, newId, isPlatformAdmin);
        return newId;
    }

    private async Task UpsertTenantAdminMembershipAsync(
        System.Data.Common.DbConnection conn,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        bool exists;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT 1 FROM identity.tenant_memberships
                                 WHERE deleted_at IS NULL
                                   AND user_id = @userId AND tenant_id = @tenantId
                                 FOR UPDATE";
            AddParam(read, "userId", userId);
            AddParam(read, "tenantId", tenantId);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            exists = await r.ReadAsync(cancellationToken);
        }

        if (exists)
        {
            logger.LogInformation(
                "OPS.2.2 e2e owner membership already exists (user={UserId}, tenant={TenantId}).",
                userId, tenantId);
            return;
        }

        await using var ins = conn.CreateCommand();
        ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        ins.CommandText = @"
INSERT INTO identity.tenant_memberships
    (""Id"", user_id, tenant_id, role, is_primary, row_version, created_at, updated_at)
VALUES (gen_random_uuid(), @userId, @tenantId, 'tenant_admin', TRUE, 0, NOW(), NOW())";
        AddParam(ins, "userId", userId);
        AddParam(ins, "tenantId", tenantId);
        await ins.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation(
            "OPS.2.2 inserted e2e owner tenant_admin membership (user={UserId}, tenant={TenantId}).",
            userId, tenantId);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
