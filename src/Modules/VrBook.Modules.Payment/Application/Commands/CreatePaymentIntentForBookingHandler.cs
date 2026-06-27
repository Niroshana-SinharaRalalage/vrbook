using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

internal sealed class CreatePaymentIntentForBookingHandler(
    IStripeGateway stripe,
    ITenantStripeContextLookup tenantStripe,
    IPaymentIntentRepository repo,
    IUnitOfWork uow,
    IConfiguration configuration,
    ILogger<CreatePaymentIntentForBookingHandler> logger)
    : IRequestHandler<CreatePaymentIntentForBookingCommand, PaymentIntentDto?>
{
    private const string AllowPlatformFallbackKey = "Payment:AllowPlatformFallback";

    public async Task<PaymentIntentDto?> Handle(
        CreatePaymentIntentForBookingCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.IsConfigured)
        {
            logger.LogWarning(
                "Stripe not configured; skipping PaymentIntent creation for booking {BookingId}.",
                cmd.BookingId);
            return null;
        }

        var existing = await repo.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (existing is not null)
        {
            return Map(existing);
        }

        // OPS.M.5 §3.4 (D4) — replace the OPS.M.3 raw-SQL ResolveTenantIdAsync with
        // the ITenantStripeContextLookup contract; same data, typed shape.
        var ctx = await tenantStripe.GetAsync(cmd.TenantId, cancellationToken)
            ?? throw new BusinessRuleViolationException(
                "payment.tenant_context_missing",
                $"Tenant {cmd.TenantId:D} has no Stripe context row.");

        // OPS.M.5 §3.5 (D5/D15) — throw if no Stripe account; staging may opt
        // into the legacy platform path via feature flag (production default off).
        var fallbackAllowed = configuration.GetValue<bool>(AllowPlatformFallbackKey, false);
        if (ctx.StripeAccountId is null && !fallbackAllowed)
        {
            throw new BusinessRuleViolationException(
                "payment.connect_account_missing",
                $"Tenant {cmd.TenantId:D} has no Stripe Connect account. Publishing should be gated on " +
                $"StripeAccountStatus = Active; this is upstream-bug territory.");
        }

        var idempotencyKey = StripeIdempotency.ForPaymentIntent(cmd.BookingId);
        var metadata = new Dictionary<string, string>
        {
            ["booking_id"] = cmd.BookingId.ToString("D"),
            ["tenant_id"] = cmd.TenantId.ToString("D"),
        };

        StripeIntentCreated created;
        if (ctx.StripeAccountId is not null)
        {
            var feeCents = StripeFeeCalculator.ApplicationFeeCents(cmd.Amount.Amount, ctx.PlatformFeeBps);
            created = await stripe.CreatePaymentIntentAsync(
                cmd.Amount.Amount,
                cmd.Amount.Currency,
                idempotencyKey: idempotencyKey,
                metadata: metadata,
                destinationAccountId: ctx.StripeAccountId,
                applicationFeeAmount: feeCents,
                cancellationToken: cancellationToken);
            logger.LogInformation(
                "PaymentIntent routed via Connect tenant_id={TenantId} stripe_account_id={AccountId} fee_cents={Fee}",
                cmd.TenantId, ctx.StripeAccountId, feeCents);
        }
        else
        {
            // Staging fallback per AllowPlatformFallback — platform Stripe handles
            // the charge. Audit-trail leakage is acceptable in staging only.
            created = await stripe.CreatePaymentIntentAsync(
                cmd.Amount.Amount,
                cmd.Amount.Currency,
                idempotencyKey: idempotencyKey,
                metadata: metadata,
                cancellationToken: cancellationToken);
            logger.LogWarning(
                "PaymentIntent routed via platform fallback (no tenant Stripe account; flag {Flag}=true) tenant_id={TenantId}",
                AllowPlatformFallbackKey, cmd.TenantId);
        }

        var pi = PaymentIntent.Create(
            cmd.TenantId,
            cmd.BookingId,
            created.Id,
            created.ClientSecret,
            cmd.Amount.Amount,
            cmd.Amount.Currency,
            captureMethod: "manual",
            initialStatus: created.Status);

        await repo.AddAsync(pi, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return Map(pi);
    }

    private static PaymentIntentDto Map(PaymentIntent pi) => new(
        Id: pi.Id,
        BookingId: pi.BookingId,
        StripePaymentIntentId: pi.StripePaymentIntentId,
        Amount: new Money(pi.Amount, pi.Currency),
        Status: pi.Status,
        CaptureMethod: pi.CaptureMethod,
        CreatedAt: pi.CreatedAt);
}
