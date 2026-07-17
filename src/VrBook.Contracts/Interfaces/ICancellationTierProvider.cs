namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-216 — resolves the platform-global cancellation tier numbers. Backed by
/// <c>Cancellation:Tiers:*</c> config now (the fail-safe default + VRB-102's config
/// AC); VRB-216 later replaces the registration with a DB-backed impl
/// (<c>admin.cancellation_tiers</c>, platform-admin editable) seeded from the same
/// defaults. Consumers (PAY VRB-102, the policy resolver) depend only on this
/// interface, so the config→DB swap is invisible to them.
/// </summary>
public interface ICancellationTierProvider
{
    Task<GlobalCancellationTiers> GetActiveAsync(CancellationToken ct = default);
}

/// <summary>
/// The active tier schedule. <paramref name="FirstTierDays"/>: full refund when
/// cancelling at least this many days before check-in. <paramref name="SecondTierDays"/>:
/// lower bound of the partial band. <paramref name="MiddleTierRefundPct"/>: percent
/// refunded within the band (0–100). <paramref name="FinalCutoffHours"/>: no refund
/// inside this many hours. <paramref name="Version"/>: monotonic version of the
/// active row (0 for config-backed) — snapshotted onto bookings for provenance.
/// Invariant: <c>FirstTierDays &gt; SecondTierDays</c> and <c>MiddleTierRefundPct ∈ [0,100]</c>.
/// </summary>
public sealed record GlobalCancellationTiers(
    int FirstTierDays,
    int SecondTierDays,
    int MiddleTierRefundPct,
    int FinalCutoffHours,
    int Version)
{
    /// <summary>The seed defaults (7 / 2 / 50% / 48h) — the Q24 proposal starting point;
    /// fully editable via VRB-216 once the DB-backed provider lands.</summary>
    public static readonly GlobalCancellationTiers Default = new(7, 2, 50, 48, Version: 0);
}
