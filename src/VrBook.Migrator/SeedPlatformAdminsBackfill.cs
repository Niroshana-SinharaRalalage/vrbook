using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Migrator;

/// <summary>
/// Slice OPS.M.22.6 — Bicep-declarative backfill for pre-M.22 platform
/// admins. Reads <c>Bootstrap:SeedPlatformAdmins</c> from configuration
/// (populated by Bicep on every deploy) and idempotently inserts each
/// entry as a pre-seeded platform-admin row in <c>identity.users</c>.
/// Runs AFTER every module's <c>MigrateAsync</c> so the
/// <c>pre_seeded_at</c> column from the M.22.2 migration exists.
///
/// <para><b>Idempotency:</b> for each email in the list, does the
/// read-then-write dance inside a single transaction — checks whether
/// an active row exists for the email; INSERTs with
/// <c>pre_seeded_at=NOW()</c> when absent; UPDATEs
/// <c>is_platform_admin=TRUE</c> when the row exists but the flag is
/// unset. Never overwrites <c>pre_seeded_at</c> on an existing row
/// (audit trail is preserved).</para>
///
/// <para><b>Config shape</b> (env vars in Container App Job):</para>
/// <code>
/// Bootstrap__SeedPlatformAdmins__0__Email=niroshanaks@gmail.com
/// Bootstrap__SeedPlatformAdmins__0__DisplayName=Niroshana
/// Bootstrap__SeedPlatformAdmins__1__Email=other@example.com
/// ...
/// </code>
/// <para>Empty / missing config = no-op. Not a required section.</para>
///
/// <para><b>Chicken-and-egg</b>: the very first platform admin bootstrap
/// still runs through this backfill AS LONG AS the operator adds the
/// email to Bicep BEFORE the first deploy. If the environment is already
/// live and adding a first PA on the fly, use <c>vrbook-admin.ps1
/// seed-platform-admin</c> instead (plan §5-Q4 escape hatch).</para>
/// </summary>
public sealed class SeedPlatformAdminsBackfill(
    IdentityDbContext db,
    IConfiguration configuration,
    ILogger<SeedPlatformAdminsBackfill> logger)
{
    public sealed record SeedEntry(string Email, string DisplayName);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection("Bootstrap:SeedPlatformAdmins");
        var entries = section.GetChildren()
            .Select(c => new SeedEntry(
                c["Email"] ?? string.Empty,
                c["DisplayName"] ?? string.Empty))
            .Where(e => !string.IsNullOrWhiteSpace(e.Email) && !string.IsNullOrWhiteSpace(e.DisplayName))
            .ToArray();

        if (entries.Length == 0)
        {
            logger.LogInformation(
                "OPS.M.22.6 backfill skipped — Bootstrap:SeedPlatformAdmins is empty or missing.");
            return;
        }

        logger.LogInformation(
            "OPS.M.22.6 backfill running for {Count} platform admin(s).", entries.Length);

        // One transaction wrapping the whole backfill so a partial failure
        // doesn't leave the environment with half-seeded admins.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            int freshInserts = 0;
            int flagsGranted = 0;
            foreach (var e in entries)
            {
                var normalizedEmail = e.Email.Trim().ToLowerInvariant();
                var escapedDisplayName = e.DisplayName.Trim().Replace("'", "''", StringComparison.Ordinal);

                // Read whether an ACTIVE row exists for this email. Cheaper than
                // a full COUNT because the partial-unique index
                // users_email_active_lower_uq covers it.
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                {
                    await conn.OpenAsync(cancellationToken);
                }

                Guid? existingId;
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                    cmd.CommandText = @"SELECT ""Id"", is_platform_admin, pre_seeded_at
                                        FROM identity.users
                                        WHERE deleted_at IS NULL AND lower(email) = @email
                                        FOR UPDATE";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "email";
                    p.Value = normalizedEmail;
                    cmd.Parameters.Add(p);
                    await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                    existingId = null;
                    if (await r.ReadAsync(cancellationToken))
                    {
                        existingId = r.GetGuid(0);
                        var isPa = r.GetBoolean(1);
                        var preSeededAt = await r.IsDBNullAsync(2, cancellationToken) ? (DateTimeOffset?)null : r.GetFieldValue<DateTimeOffset>(2);
                        logger.LogInformation(
                            "Backfill target {Email} already exists (id={UserId}, pa={Pa}, pre_seeded_at={PreSeededAt}).",
                            normalizedEmail, existingId, isPa, preSeededAt);
                    }
                }

                if (existingId is null)
                {
                    // Fresh insert.
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                    cmd.CommandText = $@"
INSERT INTO identity.users
    (""Id"", email, display_name, phone, is_platform_admin, email_verified,
     row_version, created_at, updated_at, pre_seeded_at)
VALUES (gen_random_uuid(), @email, '{escapedDisplayName}', '', TRUE, FALSE, 0, NOW(), NOW(), NOW())";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "email";
                    p.Value = normalizedEmail;
                    cmd.Parameters.Add(p);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    freshInserts++;
                    logger.LogInformation(
                        "OPS.M.22.6 inserted platform-admin row for {Email}.", normalizedEmail);
                }
                else
                {
                    // Ensure the platform-admin flag holds. Does NOT touch
                    // pre_seeded_at — audit trail is immutable.
                    await using var cmd = conn.CreateCommand();
                    cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                    cmd.CommandText = @"UPDATE identity.users
                                        SET is_platform_admin = TRUE, updated_at = NOW()
                                        WHERE ""Id"" = @id AND is_platform_admin = FALSE";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "id";
                    p.Value = existingId.Value;
                    cmd.Parameters.Add(p);
                    var rows = await cmd.ExecuteNonQueryAsync(cancellationToken);
                    if (rows > 0)
                    {
                        flagsGranted++;
                        logger.LogInformation(
                            "OPS.M.22.6 promoted existing row {Email} → is_platform_admin=TRUE.", normalizedEmail);
                    }
                }
            }

            // Audit trail for the backfill run.
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
                cmd.CommandText = @"
INSERT INTO identity.migration_audit
    (""Id"", migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(),
        'OpsM22_SeedPlatformAdminsBackfill',
        'bicep-declarative-backfill',
        @affected,
        @notes,
        NOW())";
                var p1 = cmd.CreateParameter(); p1.ParameterName = "affected"; p1.Value = freshInserts + flagsGranted; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "notes"; p2.Value = $"entries={entries.Length} fresh={freshInserts} flags_granted={flagsGranted}"; cmd.Parameters.Add(p2);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            logger.LogInformation(
                "OPS.M.22.6 backfill committed. fresh={Fresh} flags_granted={Flags} total={Total}",
                freshInserts, flagsGranted, entries.Length);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            logger.LogError(ex,
                "OPS.M.22.6 backfill failed and rolled back. Fix config or DB state; re-run migrator to retry.");
            throw;
        }
    }
}
