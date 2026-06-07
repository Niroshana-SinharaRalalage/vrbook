using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

/// <summary>
/// Idempotency log for Stripe webhook events. We persist the Stripe event ID
/// before dispatching so duplicate webhook deliveries become no-ops.
/// </summary>
public sealed class WebhookEvent : Entity
{
    public string StripeEventId { get; private set; } = default!;
    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset ReceivedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; private set; }

    private WebhookEvent() { } // EF

    public WebhookEvent(string stripeEventId, string eventType, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeEventId);
        Id = Guid.NewGuid();
        StripeEventId = stripeEventId;
        EventType = eventType;
        PayloadJson = payloadJson;
    }

    public void MarkProcessed() => ProcessedAt = DateTimeOffset.UtcNow;
}
