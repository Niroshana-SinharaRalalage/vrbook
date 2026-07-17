using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

public sealed class Refund : Entity
{
    /// <summary>Denorm from PaymentIntent. OPS.M.3c flipped to non-nullable.</summary>
    public Guid TenantId { get; private set; }

    public Guid PaymentIntentId { get; private set; }
    public string StripeRefundId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public RefundStatus Status { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>
    /// VRB-104 (gap G37) — platform application fee actually reversed by Stripe
    /// for this refund, in cents (read back from Stripe = authoritative). Null
    /// for a non-Connect refund, or a legacy row created before this column
    /// existed (the negative-balance guard falls back to a proportional
    /// approximation only for those null rows).
    /// </summary>
    public long? FeeReversalCents { get; private set; }

    private Refund() { } // EF

    internal Refund(
        Guid tenantId, Guid paymentIntentId, string stripeRefundId, decimal amount,
        string currency, string? reason, long? feeReversalCents = null)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeRefundId);
        Id = Guid.NewGuid();
        TenantId = tenantId;
        PaymentIntentId = paymentIntentId;
        StripeRefundId = stripeRefundId;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Status = RefundStatus.Pending;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        FeeReversalCents = feeReversalCents;
    }

    public void UpdateStatus(RefundStatus status)
    {
        Status = status;
    }
}
