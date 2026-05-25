namespace VrBook.Contracts.Enums;

/// <summary>
/// Subset of Stripe PaymentIntent statuses we surface to clients.
/// Mirrors the Stripe webhook event names — keep these aligned with Stripe.net.
/// </summary>
public enum PaymentStatus
{
    RequiresPaymentMethod = 0,
    RequiresConfirmation = 1,
    RequiresAction = 2,
    Processing = 3,
    RequiresCapture = 4,
    Succeeded = 5,
    Cancelled = 6,
    Failed = 7,
}
