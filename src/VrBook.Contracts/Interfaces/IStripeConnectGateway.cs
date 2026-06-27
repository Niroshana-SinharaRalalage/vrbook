namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.5 §3.3 (D3) — cross-module contract for Stripe Connect Express
/// onboarding operations. Lives in Contracts because the consumer is Identity
/// (the Tenant aggregate's onboarding commands) but the SDK orchestration lives
/// in Payment. Same pattern as <see cref="IPropertyOwnerLookup"/>.
///
/// <para>Per §10 best-practices: every implementation must pass an explicit
/// <c>Stripe.RequestOptions.IdempotencyKey</c> per <c>StripeIdempotency</c>'s
/// per-call formats. <see cref="CreateAccountLinkAsync"/> and
/// <see cref="CreateLoginLinkAsync"/> are deliberately NOT idempotent — each
/// call returns a fresh expiring link.</para>
/// </summary>
public interface IStripeConnectGateway
{
    /// <summary>
    /// Create a Connect Express account for a tenant. Idempotency key is
    /// <c>tenant-onboarding:{tenantId:D}</c> so retries return the same
    /// <c>acct_…</c> id.
    /// </summary>
    Task<string> CreateConnectAccountAsync(
        Guid tenantId, string email, string country, CancellationToken ct = default);

    /// <summary>
    /// Generate a fresh AccountLink for the Stripe-hosted onboarding form.
    /// Links expire after 5 minutes; re-call for a new one on each page visit.
    /// The return/refresh URLs are read from <c>StripeOptions</c> by the
    /// implementation per OPS.M.5 §3.12 (D12).
    /// </summary>
    Task<StripeAccountLink> CreateAccountLinkAsync(
        string stripeAccountId, CancellationToken ct = default);

    /// <summary>
    /// Generate a magic-link to the connected account's Stripe Express
    /// dashboard. Tenant uses this to view payouts, 1099-K filings, etc.
    /// </summary>
    Task<string> CreateLoginLinkAsync(string stripeAccountId, CancellationToken ct = default);
}

/// <summary>The URL the tenant gets redirected to + when it expires.</summary>
public sealed record StripeAccountLink(string Url, DateTimeOffset ExpiresAt);
