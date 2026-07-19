namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-212 — cross-module read of a tenant's persisted Stripe-Connect readiness
/// (<c>identity.tenants</c>: Status / ChargesEnabled / PayoutsEnabled). Catalog uses it
/// to enforce the property-activation gate (<c>Property.Activate</c>) at the handler
/// boundary — a property may only go live when its tenant is payment-ready. Additive
/// to the Stripe-context family (<see cref="ITenantStripeContextLookup"/>); the impl
/// lives in the Identity module, keeping Catalog free of an Identity dependency
/// (mirrors <see cref="IPropertyOwnerLookup"/>).
/// </summary>
public interface ITenantStripeReadinessLookup
{
    Task<TenantStripeReadiness?> GetAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary><paramref name="Status"/> is the tenant lifecycle status (e.g. <c>"Active"</c>);
/// <paramref name="ChargesEnabled"/>/<paramref name="PayoutsEnabled"/> mirror the Stripe
/// Connect account capabilities. Payment-ready ⇔ Status == "Active" AND both flags true.</summary>
public sealed record TenantStripeReadiness(string Status, bool ChargesEnabled, bool PayoutsEnabled)
{
    public bool IsPaymentReady =>
        string.Equals(Status, "Active", StringComparison.Ordinal) && ChargesEnabled && PayoutsEnabled;
}
