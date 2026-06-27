using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Domain;

/// <summary>
/// OPS.M.5 §3.6 (D6) — thrown by <c>RefundForBookingHandler</c> when a refund
/// would push the connected account's balance negative. Subclasses
/// <see cref="BusinessRuleViolationException"/> for the standard 422 mapping;
/// the dedicated type lets telemetry filter and ops dashboards alert on
/// "negative balance refund attempts" specifically.
///
/// <para>The guard is a local sufficient condition (we don't poll Stripe for
/// the live balance at refund time); the fields below let an operator
/// reconstruct the math from the audit log.</para>
/// </summary>
public sealed class NegativeBalanceRefundException : BusinessRuleViolationException
{
    public NegativeBalanceRefundException(
        Guid paymentIntentId, decimal attemptedRefund, decimal availableConnectedBalance)
        : base(
            "payment.negative_balance_refund",
            $"Refund of {attemptedRefund:F2} would push the connected balance below zero " +
            $"(available {availableConnectedBalance:F2}). " +
            $"Reduce the amount or coordinate with the tenant.")
    {
        PaymentIntentId = paymentIntentId;
        AttemptedRefund = attemptedRefund;
        AvailableConnectedBalance = availableConnectedBalance;
    }

    public Guid PaymentIntentId { get; }
    public decimal AttemptedRefund { get; }
    public decimal AvailableConnectedBalance { get; }
}
