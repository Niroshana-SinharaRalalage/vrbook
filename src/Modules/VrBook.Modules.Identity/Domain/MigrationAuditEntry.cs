namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Slice OPS.M.13 — audit row emitted by data-heal migrations to record
/// what they did. Read via
/// <c>GET /api/v1/admin/platform/migration-audit</c>.
///
/// <para>Motivated by the F11.7 failure mode: Postgres <c>RAISE NOTICE</c>
/// from container-app job stdout does NOT reliably reach Log Analytics,
/// so three F11.7 data-heals shipped blind. This table is the canonical
/// side-effect record and does not depend on log ingestion.</para>
///
/// <para>POCO. Immutable from the app's perspective — INSERTed by
/// migration SQL, read via admin endpoint. No mutating methods needed.</para>
///
/// <para>Design ref: <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1 +
/// §2.8. This is also the OPS.M.16 (migration audit) deliverable, bundled
/// into M.13 rather than shipped as a separate slice.</para>
/// </summary>
#pragma warning disable S1144 // Unused private setters — EF hydrates them via reflection.
#pragma warning disable S3453 // Parameterless ctor is only for EF's reflection.
public sealed class MigrationAuditEntry
{
    public Guid Id { get; private set; }
    public string MigrationName { get; private set; } = default!;
    public string StepName { get; private set; } = default!;
    public int AffectedCount { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }

    private MigrationAuditEntry() { } // EF Core / read-side hydration
}
#pragma warning restore S3453
#pragma warning restore S1144
