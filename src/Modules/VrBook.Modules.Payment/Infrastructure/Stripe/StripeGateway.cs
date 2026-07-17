using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

internal sealed class StripeGateway : IStripeGateway, VrBook.Contracts.Interfaces.IStripeConnectGateway
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
        => await CreatePaymentIntentInternalAsync(
            amount, currency, idempotencyKey, metadata,
            destinationAccountId: null, applicationFeeAmount: 0, cancellationToken);

    public async Task<StripeIntentCreated> CreatePaymentIntentAsync(
        decimal amount, string currency, string idempotencyKey, IDictionary<string, string>? metadata,
        string destinationAccountId, long applicationFeeAmount, CancellationToken cancellationToken = default)
        => await CreatePaymentIntentInternalAsync(
            amount, currency, idempotencyKey, metadata,
            destinationAccountId, applicationFeeAmount, cancellationToken);

    private async Task<StripeIntentCreated> CreatePaymentIntentInternalAsync(
        decimal amount, string currency, string idempotencyKey, IDictionary<string, string>? metadata,
        string? destinationAccountId, long applicationFeeAmount, CancellationToken cancellationToken)
    {
        RequireConfigured();
        var service = new PaymentIntentService();
        var opts = BuildDestinationChargeOptions(amount, currency, metadata, destinationAccountId, applicationFeeAmount);
        var requestOpts = new RequestOptions { IdempotencyKey = idempotencyKey };
        var pi = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(opts, requestOpts, token),
            cancellationToken);
        logger.LogInformation(
            "Stripe PaymentIntent created stripe_payment_intent_id={Id} destination_account={Dest} fee_cents={Fee} on_behalf_of={OnBehalfOf} idempotency_key={Key}",
            pi.Id, destinationAccountId ?? "<platform>", applicationFeeAmount, opts.OnBehalfOf is not null, idempotencyKey);
        return new StripeIntentCreated(pi.Id, pi.ClientSecret, MapStatus(pi.Status));
    }

    /// <summary>
    /// Builds the <see cref="PaymentIntentCreateOptions"/>. Extracted for
    /// unit-testability (VRB-105). For a Connect destination charge the platform
    /// stays merchant-of-record: <c>TransferData.Destination</c> +
    /// <c>ApplicationFeeAmount</c> route funds + fee.
    /// </summary>
    internal static PaymentIntentCreateOptions BuildDestinationChargeOptions(
        decimal amount, string currency, IDictionary<string, string>? metadata,
        string? destinationAccountId, long applicationFeeAmount)
    {
        var opts = new PaymentIntentCreateOptions
        {
            Amount = ToCents(amount),
            Currency = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            // Allow Stripe Elements (web) to attach a card. Owner confirms = capture.
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = metadata is null ? null : new Dictionary<string, string>(metadata),
        };
        if (destinationAccountId is not null)
        {
            // Connect destination charge — platform is merchant-of-record
            // (VRB-105 / gap G38): route funds net of the platform fee to the
            // connected account, but do NOT set OnBehalfOf — that would make the
            // supplier the settlement merchant + card-statement entity, which
            // contradicts the marketplace-facilitator tax posture (VRB-103).
            opts.TransferData = new PaymentIntentTransferDataOptions { Destination = destinationAccountId };
            opts.ApplicationFeeAmount = applicationFeeAmount;
        }
        return opts;
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
        => await RefundInternalAsync(
            stripePaymentIntentId, amount, idempotencyKey, reason,
            refundApplicationFee: false, applicationFeeRefundCents: null, cancellationToken);

    public async Task<StripeRefundCreated> RefundAsync(
        string stripePaymentIntentId, decimal? amount, string idempotencyKey, string? reason,
        bool refundApplicationFee, long? applicationFeeRefundCents, CancellationToken cancellationToken = default)
        => await RefundInternalAsync(
            stripePaymentIntentId, amount, idempotencyKey, reason,
            refundApplicationFee, applicationFeeRefundCents, cancellationToken);

    private async Task<StripeRefundCreated> RefundInternalAsync(
        string stripePaymentIntentId, decimal? amount, string idempotencyKey, string? reason,
        bool refundApplicationFee, long? applicationFeeRefundCents, CancellationToken cancellationToken)
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
        // OPS.M.5 §3.6 (D6) — fee reversal for Connect destination charges.
        if (refundApplicationFee)
        {
            opts.RefundApplicationFee = true;
        }
        if (applicationFeeRefundCents is { } cents)
        {
            // Stripe accepts an explicit amount via the metadata or via a follow-up
            // ApplicationFeeRefundService.CreateAsync call; the RefundCreateOptions
            // surface doesn't include a typed property for it on 47.x. Use metadata
            // so audit trail records the intended proportional reversal.
            opts.Metadata = new Dictionary<string, string>
            {
                ["application_fee_refund_cents"] = cents.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        var requestOpts = new RequestOptions { IdempotencyKey = idempotencyKey };
        var refund = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(opts, requestOpts, token),
            cancellationToken);
        logger.LogInformation(
            "Stripe refund issued stripe_refund_id={Id} refund_application_fee={Reverse} application_fee_refund_cents={Cents} idempotency_key={Key}",
            refund.Id, refundApplicationFee, applicationFeeRefundCents, idempotencyKey);
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

    // ---- OPS.M.5 IStripeConnectGateway ----

    public async Task<string> CreateConnectAccountAsync(
        Guid tenantId, string email, string country, CancellationToken ct = default)
    {
        RequireConfigured();
        var service = new AccountService();
        var opts = new AccountCreateOptions
        {
            Type = "express",
            Country = country,
            Email = email,
            Capabilities = new AccountCapabilitiesOptions
            {
                CardPayments = new() { Requested = true },
                Transfers = new() { Requested = true },
            },
        };
        var requestOpts = new RequestOptions
        {
            IdempotencyKey = StripeIdempotency.ForOnboarding(tenantId),
        };
        var account = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(opts, requestOpts, token),
            ct);
        logger.LogInformation(
            "Stripe Connect account created tenant={TenantId} stripe_account_id={AccountId} idempotency_key={Key}",
            tenantId, account.Id, requestOpts.IdempotencyKey);
        return account.Id;
    }

    public async Task<VrBook.Contracts.Interfaces.StripeAccountLink> CreateAccountLinkAsync(
        string stripeAccountId, CancellationToken ct = default)
    {
        RequireConfigured();
        var service = new AccountLinkService();
        var opts = new AccountLinkCreateOptions
        {
            Account = stripeAccountId,
            Type = "account_onboarding",
            ReturnUrl = options.OnboardingReturnUrl,
            RefreshUrl = options.OnboardingRefreshUrl,
        };
        var link = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(opts, cancellationToken: token),
            ct);
        var expiresAt = new DateTimeOffset(link.ExpiresAt, TimeSpan.Zero);
        logger.LogInformation(
            "Stripe Connect account link issued stripe_account_id={AccountId} expires_at={ExpiresAt}",
            stripeAccountId, expiresAt);
        return new VrBook.Contracts.Interfaces.StripeAccountLink(link.Url, expiresAt);
    }

    public async Task<VrBook.Contracts.Interfaces.StripeAccountReadiness> GetAccountReadinessAsync(
        string stripeAccountId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeAccountId);
        RequireConfigured();
        // Slice OPS.M.10.2 F11.4 — manual reconcile when the
        // account.updated webhook is delayed. Read-only; no idempotency
        // key needed (GET is naturally idempotent).
        var service = new AccountService();
        var account = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.GetAsync(stripeAccountId, cancellationToken: token),
            ct);
        logger.LogInformation(
            "Stripe Connect account readiness fetched stripe_account_id={AccountId} charges_enabled={Charges} payouts_enabled={Payouts}",
            stripeAccountId, account.ChargesEnabled, account.PayoutsEnabled);
        return new VrBook.Contracts.Interfaces.StripeAccountReadiness(
            account.Id, account.ChargesEnabled, account.PayoutsEnabled);
    }

    public async Task<string> CreateLoginLinkAsync(string stripeAccountId, CancellationToken ct = default)
    {
        RequireConfigured();
        var service = new AccountLoginLinkService();
        var link = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(stripeAccountId, cancellationToken: token),
            ct);
        logger.LogInformation(
            "Stripe Connect login link issued stripe_account_id={AccountId}", stripeAccountId);
        return link.Url;
    }

    // ---- Utilities ----

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
