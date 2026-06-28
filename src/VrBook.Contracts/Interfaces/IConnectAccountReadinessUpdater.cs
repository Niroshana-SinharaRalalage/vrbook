namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.5 §3.7 (D7) + §3.8 (D8) — cross-module command surface invoked by the
/// Payment <c>account.updated</c> webhook handler. Identity owns the
/// implementation: it loads the matching tenant, calls
/// <c>Tenant.UpdateStripeAccountReadiness</c> (which runs the
/// PendingOnboarding → Active / Active → Suspended state machine), and persists.
/// </summary>
public interface IConnectAccountReadinessUpdater
{
    /// <summary>
    /// Apply the latest <c>charges_enabled</c> + <c>payouts_enabled</c> flags
    /// from a Stripe <c>account.updated</c> event. Returns <c>true</c> when a
    /// tenant matched the account; <c>false</c> if the account is unknown
    /// (stale or replayed event). Implementations are tenant-scoped via
    /// <c>UpdateStripeAccountReadiness</c>'s no-op for repeated transitions.
    /// </summary>
    Task<bool> UpdateAsync(
        string stripeAccountId,
        bool chargesEnabled,
        bool payoutsEnabled,
        CancellationToken ct = default);
}
