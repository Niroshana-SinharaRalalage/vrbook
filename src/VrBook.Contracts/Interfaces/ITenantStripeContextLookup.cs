namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.5 §3.4 (D4) — cross-module read used by Payment to look up the
/// Stripe-Connect routing context for a tenant. Implemented in Identity
/// against <c>IdentityDbContext.Tenants</c>; consumed by
/// <c>CreatePaymentIntentForBookingHandler</c>, <c>RefundForBookingHandler</c>,
/// and <c>HandleStripeWebhookCommand</c>.
///
/// <para>Phase 4's <c>tenant_connect_accounts</c> relationship table replaces
/// the implementation without changing the contract — the return shape stays
/// "single Stripe destination per (tenant, booking)" because a booking still
/// resolves to one supplier even in the multi-supplier topology.</para>
/// </summary>
public interface ITenantStripeContextLookup
{
    Task<TenantStripeContext?> GetAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Routing context per <see cref="ITenantStripeContextLookup"/>. Null
/// <see cref="StripeAccountId"/> means the tenant has not onboarded Stripe;
/// callers throw <c>payment.connect_account_missing</c> per OPS.M.5 §3.5.
/// </summary>
public sealed record TenantStripeContext(
    Guid TenantId,
    string? StripeAccountId,
    int PlatformFeeBps,
    string DefaultCurrency);
