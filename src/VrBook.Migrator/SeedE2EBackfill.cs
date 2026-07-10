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

    // Slice OPS.2.3 — deterministic public property + pricing plan for the
    // anonymous smoke suite (detail-by-slug + unauthenticated quote). Mirrored
    // in web/tests/e2e/support/testTenant.ts (E2E_SMOKE_PROPERTY_SLUG /
    // E2E_SMOKE_PROPERTY_ID) — keep the two in sync. The GUID is intentionally
    // human-recognizable + obviously synthetic so it never collides with a real
    // Guid.NewGuid() row and the quote spec can hardcode it. The property is
    // is_active=true so it clears the Catalog public-read RLS carve-out
    // (OpsM9_1a: USING is_active=true AND deleted_at IS NULL); the plan's
    // tenant_id MUST match so the quote handler's RLS-scoped plan read sees it.
    private const string SmokePropertySlug = "e2e-smoke-property";
    private static readonly Guid SmokePropertyId = new("e2e00000-0000-0000-0000-000000000001");

    // Slice OPS.2.5 — two Tentative bookings on the seed property for the owner
    // confirm/reject specs. Fixed GUIDs mirrored in testTenant.ts
    // (E2E_TENTATIVE_BOOKING_CONFIRM_ID / _REJECT_ID). Each migrator run RESETS
    // them back to Tentative (owner specs consume them → Confirmed/Rejected), so
    // a fresh deploy re-arms them; the specs skip gracefully if a prior nightly
    // already consumed them without a redeploy. guest_user_id is synthetic — no
    // cross-schema FK enforces it (booking FKs are child-table only).
    private static readonly Guid ConfirmBookingId = new("e2e00000-0000-0000-0000-000000000010");
    private static readonly Guid RejectBookingId = new("e2e00000-0000-0000-0000-000000000011");
    private static readonly Guid SyntheticGuestUserId = new("e2e00000-0000-0000-0000-0000000000f0");

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

            // Slice OPS.2.3 — the anonymous smoke fixture (property + plan).
            // Shares the wrapping transaction so a property can never land
            // without its plan (the quote smoke would then 404).
            await UpsertE2eSmokePropertyAsync(conn, tenantId, ownerId, cancellationToken);
            await UpsertE2eSmokePricingPlanAsync(conn, tenantId, cancellationToken);
            // OPS.2.5 — arm the two Tentative bookings for owner confirm/reject.
            await UpsertE2eTentativeBookingAsync(conn, tenantId, ConfirmBookingId, "E2ECONFIRM", cancellationToken);
            await UpsertE2eTentativeBookingAsync(conn, tenantId, RejectBookingId, "E2EREJECT", cancellationToken);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                cmd.CommandText = @"
INSERT INTO identity.migration_audit
    (""Id"", migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(),
        'OpsM2_SeedE2EBackfill',
        'bicep-declarative-e2e-fixture',
        7,
        @notes,
        NOW())";
                var p = cmd.CreateParameter();
                p.ParameterName = "notes";
                p.Value = $"tenant={TenantSlug} owner={OwnerEmail} platform_admin={PlatformAdminEmail} smoke_property={SmokePropertySlug} tentative_bookings=2";
                cmd.Parameters.Add(p);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            logger.LogInformation(
                "OPS.2.2/2.3 e2e backfill committed. tenant={TenantId} owner={OwnerId} smokeProperty={PropertyId}",
                tenantId, ownerId, SmokePropertyId);
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

    /// <summary>
    /// Slice OPS.2.3 — idempotently seed the deterministic public property that
    /// backs the anonymous detail-by-slug + quote smokes. Keyed on the fixed
    /// <see cref="SmokePropertyId"/>; on re-run only re-asserts the RLS-relevant
    /// flags (<c>is_active</c>, <c>tenant_id</c>). All owned-value-object columns
    /// (address / capacity / check-in window) are NOT NULL in the DB and set to
    /// synthetic constants. The migrator role has BYPASSRLS, so the raw INSERT
    /// isn't blocked and no <c>app.tenant_id</c> GUC is needed — the tenant_id
    /// column is set explicitly.
    /// </summary>
    private async Task UpsertE2eSmokePropertyAsync(
        System.Data.Common.DbConnection conn,
        Guid tenantId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        bool exists;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT 1 FROM catalog.properties
                                 WHERE ""Id"" = @id FOR UPDATE";
            AddParam(read, "id", SmokePropertyId);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            exists = await r.ReadAsync(cancellationToken);
        }

        if (exists)
        {
            await using var upd = conn.CreateCommand();
            upd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            upd.CommandText = @"UPDATE catalog.properties
                                SET is_active = TRUE, tenant_id = @tenantId,
                                    deleted_at = NULL, updated_at = NOW()
                                WHERE ""Id"" = @id
                                  AND (is_active IS DISTINCT FROM TRUE
                                       OR tenant_id IS DISTINCT FROM @tenantId
                                       OR deleted_at IS NOT NULL)";
            AddParam(upd, "id", SmokePropertyId);
            AddParam(upd, "tenantId", tenantId);
            await upd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation(
                "OPS.2.3 e2e smoke property already exists (id={PropertyId}).", SmokePropertyId);
            return;
        }

        await using var ins = conn.CreateCommand();
        ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        ins.CommandText = @"
INSERT INTO catalog.properties
    (""Id"", slug, title, description, property_type,
     street, city, state, postal_code, country, latitude, longitude,
     max_guests, bedrooms, bathrooms, beds,
     checkin_from, checkin_to, checkout_by,
     owner_user_id, tenant_id, is_active, turnover_hours, rating_count,
     row_version, created_at, updated_at)
VALUES (@id, @slug, 'E2E Smoke Test Property',
        'Deterministic property seeded by VrBook.Migrator.SeedE2EBackfill for the OPS.2 Playwright anonymous smoke suite. Not a real listing.',
        'Villa',
        '1 E2E Way', 'Testville', 'TS', '00001', 'USA', 0.0, 0.0,
        4, 2, 1, 2,
        TIME '15:00:00', TIME '20:00:00', TIME '11:00:00',
        @ownerId, @tenantId, TRUE, 24, 0,
        0, NOW(), NOW())";
        AddParam(ins, "id", SmokePropertyId);
        AddParam(ins, "slug", SmokePropertySlug);
        AddParam(ins, "ownerId", ownerId);
        AddParam(ins, "tenantId", tenantId);
        await ins.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation(
            "OPS.2.3 inserted e2e smoke property (id={PropertyId}, slug={Slug}).",
            SmokePropertyId, SmokePropertySlug);
    }

    /// <summary>
    /// Slice OPS.2.3 — idempotently seed the single pricing plan the anonymous
    /// quote endpoint reads. <c>tenant_id</c> MUST equal the property's tenant:
    /// the quote handler resolves the tenant from <c>properties.tenant_id</c>
    /// then reads the plan inside an RLS scope stamped with it, so a mismatch
    /// hides the plan and the quote 404s. Keyed on the unique
    /// <c>property_id</c>. No fees / rules / availability rows are needed.
    /// </summary>
    private async Task UpsertE2eSmokePricingPlanAsync(
        System.Data.Common.DbConnection conn,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        bool exists;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT 1 FROM pricing.pricing_plans
                                 WHERE property_id = @propId AND deleted_at IS NULL
                                 FOR UPDATE";
            AddParam(read, "propId", SmokePropertyId);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            exists = await r.ReadAsync(cancellationToken);
        }

        if (exists)
        {
            await using var upd = conn.CreateCommand();
            upd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            upd.CommandText = @"UPDATE pricing.pricing_plans
                                SET tenant_id = @tenantId, updated_at = NOW()
                                WHERE property_id = @propId
                                  AND tenant_id IS DISTINCT FROM @tenantId";
            AddParam(upd, "propId", SmokePropertyId);
            AddParam(upd, "tenantId", tenantId);
            await upd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation(
                "OPS.2.3 e2e smoke pricing plan already exists (property={PropertyId}).", SmokePropertyId);
            return;
        }

        await using var ins = conn.CreateCommand();
        ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        ins.CommandText = @"
INSERT INTO pricing.pricing_plans
    (""Id"", property_id, base_nightly_rate, weekend_rate, currency,
     min_stay_nights, max_stay_nights, dynamic_enabled, tenant_id,
     row_version, created_at, updated_at)
VALUES (gen_random_uuid(), @propId, 100.00, 100.00, 'USD',
        1, 30, FALSE, @tenantId,
        0, NOW(), NOW())";
        AddParam(ins, "propId", SmokePropertyId);
        AddParam(ins, "tenantId", tenantId);
        await ins.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation(
            "OPS.2.3 inserted e2e smoke pricing plan (property={PropertyId}).", SmokePropertyId);
    }

    /// <summary>
    /// Slice OPS.2.5 — idempotently arm a Tentative booking on the seed property
    /// for the owner confirm/reject specs. Keyed on the fixed <paramref name="id"/>;
    /// on re-run RESETS status→Tentative and clears the confirm/cancel timestamps
    /// so a fresh deploy re-arms a booking a prior run consumed. No booking-guest
    /// or line-item child rows are needed for the confirm/reject panel to render
    /// (it gates only on <c>status == 'Tentative'</c>). tenant_id MUST match the
    /// property's tenant (RLS + the owner's active-tenant scope).
    /// </summary>
    private async Task UpsertE2eTentativeBookingAsync(
        System.Data.Common.DbConnection conn,
        Guid tenantId,
        Guid id,
        string reference,
        CancellationToken cancellationToken)
    {
        bool exists;
        await using (var read = conn.CreateCommand())
        {
            read.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            read.CommandText = @"SELECT 1 FROM booking.bookings WHERE ""Id"" = @id FOR UPDATE";
            AddParam(read, "id", id);
            await using var r = await read.ExecuteReaderAsync(cancellationToken);
            exists = await r.ReadAsync(cancellationToken);
        }

        if (exists)
        {
            await using var upd = conn.CreateCommand();
            upd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            upd.CommandText = @"UPDATE booking.bookings
                                SET status = 'Tentative',
                                    tentative_until = NOW() + INTERVAL '365 days',
                                    confirmed_at = NULL, cancelled_at = NULL,
                                    cancellation_reason = NULL, tenant_id = @tenantId,
                                    deleted_at = NULL, updated_at = NOW()
                                WHERE ""Id"" = @id";
            AddParam(upd, "id", id);
            AddParam(upd, "tenantId", tenantId);
            await upd.ExecuteNonQueryAsync(cancellationToken);
            logger.LogInformation("OPS.2.5 reset e2e Tentative booking {Id} ({Ref}).", id, reference);
            return;
        }

        await using var ins = conn.CreateCommand();
        ins.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        ins.CommandText = @"
INSERT INTO booking.bookings
    (""Id"", reference, property_id, property_title, guest_user_id, guest_display_name,
     checkin_date, checkout_date, guest_count, status, source, currency,
     subtotal, fees, taxes, discount, total, cancellation_policy, tentative_until,
     tenant_id, row_version, created_at, updated_at)
VALUES (@id, @reference, @propId, 'E2E Smoke Test Property', @guestId, 'E2E Guest',
        DATE '2031-03-01', DATE '2031-03-03', 2, 'Tentative', 'Direct', 'USD',
        200.00, 0, 0, 0, 200.00, 'Flexible', NOW() + INTERVAL '365 days',
        @tenantId, 0, NOW(), NOW())";
        AddParam(ins, "id", id);
        AddParam(ins, "reference", reference);
        AddParam(ins, "propId", SmokePropertyId);
        AddParam(ins, "guestId", SyntheticGuestUserId);
        AddParam(ins, "tenantId", tenantId);
        await ins.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("OPS.2.5 inserted e2e Tentative booking {Id} ({Ref}).", id, reference);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
