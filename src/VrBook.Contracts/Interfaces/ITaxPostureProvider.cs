namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-216 — exposes the platform tax posture (marketplace-facilitator status +
/// per-state enablement roster) as a read-model for the settings/pricing UI. This
/// owns only the POSTURE, not the tax computation — <see cref="ITaxCalculator"/> (PAY
/// VRB-103) owns the Stripe-Tax engine and swaps in independently.
/// </summary>
public interface ITaxPostureProvider
{
    Task<TaxPosture> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// <paramref name="FacilitatorActive"/>: whether the platform acts as the
/// marketplace-facilitator merchant-of-record for tax (Q25). <paramref name="PerStateEnabled"/>:
/// US-state code → enabled flag (empty at seed; an operator fills the roster at go-live).
/// </summary>
public sealed record TaxPosture(
    bool FacilitatorActive,
    IReadOnlyDictionary<string, bool> PerStateEnabled)
{
    /// <summary>Seed default: facilitator active, no states enabled yet.</summary>
    public static readonly TaxPosture Default = new(FacilitatorActive: true, PerStateEnabled: new Dictionary<string, bool>());
}
