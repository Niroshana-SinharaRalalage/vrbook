namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// Bound from configuration section <c>Stripe</c>. Secret + webhook secret come from
/// Key Vault via the container app secretRef binding (see infra/main.bicep).
/// When SecretKey is empty the gateway short-circuits and the booking flow runs
/// in payment-disabled mode — no PaymentIntent is created.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}
