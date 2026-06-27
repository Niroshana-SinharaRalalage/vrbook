using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

/// <summary>
/// Idempotency log for Stripe webhook events. We persist the Stripe event ID
/// before dispatching so duplicate webhook deliveries become no-ops.
/// </summary>
public sealed class WebhookEvent : Entity
{
    /// <summary>
    /// Tenant the webhook event maps to (routed via tenants.stripe_account_id
    /// in OPS.M.5). Per OPS_M_3_PLAN §1.4 — nullable. May stay null permanently
    /// for platform-level events (e.g. account.updated for fresh Connect
    /// onboarding) that don't yet have a tenant context.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public string StripeEventId { get; private set; } = default!;

    /// <summary>
    /// Stripe Connect routing key (per OPS_M_5_PLAN §3.7). Nullable so platform-scope
    /// events (delivered with <c>account=null</c>) coexist with connected-scope
    /// duplicates of the same <c>StripeEventId</c>. The unique constraint at the DB
    /// layer is composite on <c>(stripe_event_id, stripe_account_id)</c>.
    /// </summary>
    public string? StripeAccountId { get; private set; }

    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset ReceivedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }

    private WebhookEvent() { } // EF

    public WebhookEvent(string stripeEventId, string eventType, string payloadJson, string? stripeAccountId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeEventId);
        Id = Guid.NewGuid();
        StripeEventId = stripeEventId;
        StripeAccountId = stripeAccountId;
        EventType = eventType;
        PayloadJson = payloadJson;
    }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// OPS.M.5 will call this from the Stripe webhook handler once
    /// <c>account → tenant</c> resolution lands. Until then the column stays
    /// null and the column exists only for forward-compat per OPS_M_3_PLAN §1.4.
    /// </summary>
    public void SetTenantId(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        TenantId = tenantId;
    }
}
