using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

public sealed class Refund : Entity
{
    public Guid PaymentIntentId { get; private set; }
    public string StripeRefundId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public RefundStatus Status { get; private set; }
    public string? Reason { get; private set; }

    private Refund() { } // EF

    internal Refund(Guid paymentIntentId, string stripeRefundId, decimal amount, string currency, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeRefundId);
        Id = Guid.NewGuid();
        PaymentIntentId = paymentIntentId;
        StripeRefundId = stripeRefundId;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        Status = RefundStatus.Pending;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public void UpdateStatus(RefundStatus status)
    {
        Status = status;
    }
}
