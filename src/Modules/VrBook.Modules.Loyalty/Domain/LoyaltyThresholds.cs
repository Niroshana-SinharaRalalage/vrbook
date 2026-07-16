namespace VrBook.Modules.Loyalty.Domain;

/// <summary>
/// VRB-206 (gap G1) — the completed-stay counts at which a guest reaches each tier.
/// Passed into the domain (<see cref="LoyaltyAccount.RecordCompletedStay"/> /
/// <see cref="TierDefinition"/>) so the thresholds are config-driven without the
/// domain reading configuration. Invariant: <c>1 ≤ Bronze &lt; Silver &lt; Gold</c>.
/// </summary>
public readonly record struct LoyaltyThresholds(int Bronze, int Silver, int Gold)
{
    /// <summary>The Phase-1 defaults (Bronze 1 / Silver 3 / Gold 6) — the values that
    /// were previously hard-coded, so behaviour is unchanged when config is absent.</summary>
    public static readonly LoyaltyThresholds Default = new(1, 3, 6);
}
