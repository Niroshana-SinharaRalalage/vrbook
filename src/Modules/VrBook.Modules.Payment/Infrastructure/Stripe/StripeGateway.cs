using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

internal sealed class StripeGateway : IStripeGateway
{
    private readonly StripeOptions options;
    private readonly ILogger<StripeGateway> logger;

    public StripeGateway(IOptions<StripeOptions> options, ILogger<StripeGateway> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        if (this.options.IsConfigured)
        {
            StripeConfiguration.ApiKey = this.options.SecretKey;
        }
    }

    public bool IsConfigured => options.IsConfigured;
    public string PublishableKey => options.PublishableKey;

    private void RequireConfigured()
    {
        if (!IsConfigured)
        {
            throw new BusinessRuleViolationException(
                "payment.not_configured",
                "Payment provider is not configured for this environment.");
        }
    }

    public async Task<StripeIntentCreated> CreatePaymentIntentAsync(
        decimal amount, string currency, string idempotencyKey, IDictionary<string, string>? metadata, CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        var service = new PaymentIntentService();
        var opts = new PaymentIntentCreateOptions
        {
            Amount = ToCents(amount),
            Currency = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            // Allow Stripe Elements (web) to attach a card. Owner confirms = capture.
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = metadata is null ? null : new Dictionary<string, string>(metadata),
        };
        var requestOpts = new RequestOptions { IdempotencyKey = idempotencyKey };
        var pi = await service.CreateAsync(opts, requestOpts, cancellationToken);
        return new StripeIntentCreated(pi.Id, pi.ClientSecret, MapStatus(pi.Status));
    }

    public async Task<StripeIntentUpdate> CapturePaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        var service = new PaymentIntentService();
        var pi = await service.CaptureAsync(stripePaymentIntentId, new PaymentIntentCaptureOptions(), cancellationToken: cancellationToken);
        return new StripeIntentUpdate(pi.Id, MapStatus(pi.Status), pi.LatestChargeId);
    }

    public async Task<StripeIntentUpdate> CancelPaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        var service = new PaymentIntentService();
        var pi = await service.CancelAsync(stripePaymentIntentId, cancellationToken: cancellationToken);
        return new StripeIntentUpdate(pi.Id, MapStatus(pi.Status), pi.LatestChargeId);
    }

    public async Task<StripeRefundCreated> RefundAsync(
        string stripePaymentIntentId, decimal? amount, string idempotencyKey, string? reason, CancellationToken cancellationToken = default)
    {
        RequireConfigured();
        var service = new RefundService();
        var opts = new RefundCreateOptions
        {
            PaymentIntent = stripePaymentIntentId,
            Reason = "requested_by_customer",
        };
        // Stripe's Reason enum is limited (requested_by_customer / duplicate / fraudulent).
        // User-supplied free-text reasons aren't compatible; we keep our own copy server-side.
        _ = reason;
        if (amount.HasValue)
        {
            opts.Amount = ToCents(amount.Value);
        }
        var requestOpts = new RequestOptions { IdempotencyKey = idempotencyKey };
        var refund = await service.CreateAsync(opts, requestOpts, cancellationToken);
        return new StripeRefundCreated(refund.Id, refund.Amount / 100m, MapRefundStatus(refund.Status));
    }

    public bool VerifyWebhookSignature(string payload, string signatureHeader, out string? eventType, out string? rawEventJson)
    {
        eventType = null;
        rawEventJson = null;
        if (!IsConfigured || string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            logger.LogWarning("Webhook secret not configured; rejecting webhook.");
            return false;
        }
        try
        {
            // Stripe accounts are routinely on a newer API version than the SDK release we ship.
            // ConstructEvent throws on version mismatch by default; we use only the top-level
            // event metadata + a few known PaymentIntent fields, which are stable across versions.
            // Bigger SDK upgrades happen in their own commits.
            var evt = EventUtility.ConstructEvent(payload, signatureHeader, options.WebhookSecret,
                throwOnApiVersionMismatch: false);
            eventType = evt.Type;
            rawEventJson = payload;
            return true;
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed.");
            return false;
        }
    }

    private static long ToCents(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static PaymentStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "requires_payment_method" => PaymentStatus.RequiresPaymentMethod,
        "requires_confirmation" => PaymentStatus.RequiresConfirmation,
        "requires_action" => PaymentStatus.RequiresAction,
        "processing" => PaymentStatus.Processing,
        "requires_capture" => PaymentStatus.RequiresCapture,
        "succeeded" => PaymentStatus.Succeeded,
        "canceled" => PaymentStatus.Cancelled,
        _ => PaymentStatus.Failed,
    };

    private static RefundStatus MapRefundStatus(string stripeStatus) => stripeStatus switch
    {
        "succeeded" => RefundStatus.Succeeded,
        "pending" => RefundStatus.Pending,
        "canceled" => RefundStatus.Cancelled,
        _ => RefundStatus.Failed,
    };
}
