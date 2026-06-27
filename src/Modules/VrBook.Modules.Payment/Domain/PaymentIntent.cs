using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

/// <summary>
/// Local mirror of a Stripe PaymentIntent. Created when a booking is placed,
/// captured when the owner confirms, refunded on cancel / reject.
/// Webhook events update <see cref="Status"/> asynchronously.
/// </summary>
public sealed class PaymentIntent : AggregateRoot
{
    /// <summary>Tenant from the booking's property. OPS.M.3c flipped to non-nullable.</summary>
    public Guid TenantId { get; private set; }

    public Guid BookingId { get; private set; }
    public string StripePaymentIntentId { get; private set; } = default!;
    public string? StripeChargeId { get; private set; }
    public string ClientSecret { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; }
    public string CaptureMethod { get; private set; } = "manual";
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? LastError { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyList<Refund> Refunds => _refunds;

    private PaymentIntent() { } // EF

    public static PaymentIntent Create(
        Guid tenantId,
        Guid bookingId,
        string stripePaymentIntentId,
        string clientSecret,
        decimal amount,
        string currency,
        string captureMethod,
        PaymentStatus initialStatus)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(stripePaymentIntentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        var pi = new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BookingId = bookingId,
            StripePaymentIntentId = stripePaymentIntentId,
            ClientSecret = clientSecret,
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Status = initialStatus,
            CaptureMethod = captureMethod,
        };
        pi.Raise(new PaymentAuthorized(pi.Id, bookingId, stripePaymentIntentId, amount, pi.Currency));
        return pi;
    }

    public void UpdateStatus(PaymentStatus status, string? stripeChargeId = null)
    {
        Status = status;
        if (!string.IsNullOrEmpty(stripeChargeId))
        {
            StripeChargeId = stripeChargeId;
        }
        if (status == PaymentStatus.RequiresCapture)
        {
            AuthorizedAt ??= DateTimeOffset.UtcNow;
        }
        if (status == PaymentStatus.Succeeded)
        {
            CapturedAt ??= DateTimeOffset.UtcNow;
            Raise(new PaymentCaptured(Id, BookingId, StripePaymentIntentId, Amount, Currency));
        }
    }

    public void MarkFailed(string reason)
    {
        Status = PaymentStatus.Failed;
        LastError = reason;
        Raise(new PaymentFailed(Id, BookingId, reason));
    }

    public Refund AddRefund(string stripeRefundId, decimal amount, string? reason)
    {
        var refund = new Refund(TenantId, Id, stripeRefundId, amount, Currency, reason);
        _refunds.Add(refund);
        Raise(new RefundIssued(refund.Id, Id, BookingId, amount, Currency, reason ?? string.Empty));
        return refund;
    }
}
