using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

public sealed record HandleStripeWebhookCommand(string Payload, string SignatureHeader) : IRequest<bool>;

internal sealed class HandleStripeWebhookHandler : IRequestHandler<HandleStripeWebhookCommand, bool>
{
    private readonly IStripeGateway stripe;
    private readonly ITenantStripeContextLookup tenantStripe;
    private readonly IConnectAccountReadinessUpdater readinessUpdater;
    private readonly IPaymentIntentRepository repo;
    private readonly PaymentDbContext db;
    private readonly ILogger<HandleStripeWebhookHandler> logger;

    public HandleStripeWebhookHandler(
        IStripeGateway stripe,
        ITenantStripeContextLookup tenantStripe,
        IConnectAccountReadinessUpdater readinessUpdater,
        IPaymentIntentRepository repo,
        PaymentDbContext db,
        ILogger<HandleStripeWebhookHandler> logger)
    {
        this.stripe = stripe;
        this.tenantStripe = tenantStripe;
        this.readinessUpdater = readinessUpdater;
        this.repo = repo;
        this.db = db;
        this.logger = logger;
    }

    public async Task<bool> Handle(HandleStripeWebhookCommand cmd, CancellationToken cancellationToken)
    {
        // OPS.M.5 §3.7 #5 — signature verification runs FIRST, before any
        // JSON parsing or DB write, to prevent unsigned payloads from poisoning
        // the WebhookEvents log.
        if (!stripe.VerifyWebhookSignature(cmd.Payload, cmd.SignatureHeader, out var eventType, out var rawJson))
        {
            return false;
        }

        var doc = JsonDocument.Parse(rawJson!);
        var stripeEventId = doc.RootElement.GetProperty("id").GetString()!;

        // OPS.M.5 §3.7 — Connect webhooks carry a top-level "account" key
        // (the connected account). Platform events have it null.
        var stripeAccountId = doc.RootElement.TryGetProperty("account", out var acct)
            && acct.ValueKind == JsonValueKind.String
            ? acct.GetString()
            : null;

        // OPS.M.5 §3.7 — idempotency check is composite on
        // (stripe_event_id, stripe_account_id). A single Stripe event ID can be
        // delivered both as platform AND as connected; we treat them as
        // distinct rows.
        var seen = await db.WebhookEvents
            .AnyAsync(
                w => w.StripeEventId == stripeEventId && w.StripeAccountId == stripeAccountId,
                cancellationToken);
        if (seen)
        {
            logger.LogDebug(
                "Stripe webhook {EventId} (account={AccountId}) already processed.",
                stripeEventId, stripeAccountId);
            return true;
        }

        var wh = new WebhookEvent(stripeEventId, eventType!, rawJson!, stripeAccountId);

        // OPS.M.5 §3.7 — resolve account → tenant and stamp the row before
        // dispatching. Unknown account = null tenant (typical for fresh
        // onboarding pre-Active or for platform-scope events).
        if (stripeAccountId is not null)
        {
            var ctx = await tenantStripe.GetByStripeAccountAsync(stripeAccountId, cancellationToken);
            if (ctx is not null)
            {
                wh.SetTenantId(ctx.TenantId);
            }
            else
            {
                logger.LogWarning(
                    "Stripe webhook {EventId} arrived for unknown account {AccountId}; persisting as orphan.",
                    stripeEventId, stripeAccountId);
            }
        }

        db.WebhookEvents.Add(wh);

        try
        {
            await DispatchAsync(eventType!, doc, stripeAccountId, cancellationToken);
            wh.MarkProcessed();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook dispatch failed for {EventType}", eventType);
            // Persist the event row even on failure — we don't want a poison
            // message to loop. Manual reprocessing happens via Slice OPS.M.8.
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // OPS.M.5 §3.7 (D7) — the dispatch table. A static dictionary forces every
    // supported Stripe event type to be discoverable by reflection, which the
    // arch test `WebhookDispatchTableEnumerationTests` enforces.
    private static readonly Dictionary<string, EventHandler> Dispatch =
        new(StringComparer.Ordinal)
        {
            ["payment_intent.amount_capturable_updated"] = OnAmountCapturable,
            ["payment_intent.succeeded"] = OnSucceeded,
            ["payment_intent.payment_failed"] = OnPaymentFailed,
            ["payment_intent.canceled"] = OnCanceled,
            ["charge.refunded"] = OnChargeRefunded,
            ["charge.dispute.created"] = OnDisputeCreated,
            ["account.updated"] = OnAccountUpdated,
        };

    private async Task DispatchAsync(
        string eventType, JsonDocument doc, string? stripeAccountId, CancellationToken cancellationToken)
    {
        if (!Dispatch.TryGetValue(eventType, out var handler))
        {
            logger.LogDebug("Ignoring unhandled webhook event_type={EventType}", eventType);
            return;
        }
        var ctx = new EventContext(this, doc, stripeAccountId, cancellationToken);
        await handler(ctx);
    }

    private delegate Task EventHandler(EventContext ctx);

    private sealed record EventContext(
        HandleStripeWebhookHandler Handler, JsonDocument Doc, string? StripeAccountId, CancellationToken Ct);

    private static async Task OnAmountCapturable(EventContext c) =>
        await c.Handler.UpdatePiAsync(c, PaymentStatus.RequiresCapture, chargeId: null);

    private static async Task OnSucceeded(EventContext c)
    {
        var data = c.Doc.RootElement.GetProperty("data").GetProperty("object");
        var chargeId = data.TryGetProperty("latest_charge", out var ch) ? ch.GetString() : null;
        await c.Handler.UpdatePiAsync(c, PaymentStatus.Succeeded, chargeId);
    }

    private static async Task OnPaymentFailed(EventContext c)
    {
        var data = c.Doc.RootElement.GetProperty("data").GetProperty("object");
        var reason = data.TryGetProperty("last_payment_error", out var e) ? e.ToString() : "unknown";
        var pi = await c.Handler.LoadPiAsync(c);
        pi?.MarkFailed(reason);
    }

    private static async Task OnCanceled(EventContext c) =>
        await c.Handler.UpdatePiAsync(c, PaymentStatus.Cancelled, chargeId: null);

    private static Task OnChargeRefunded(EventContext c)
    {
        // The refund row is already persisted by RefundForBookingHandler. The
        // webhook arrives shortly after; treat as confirmation and no-op (the
        // event row itself + ProcessedAt is the audit trail).
        c.Handler.logger.LogInformation(
            "charge.refunded webhook acknowledged stripe_account_id={AccountId}",
            c.StripeAccountId);
        return Task.CompletedTask;
    }

    private static Task OnDisputeCreated(EventContext c)
    {
        // OPS.M.5 carries dispute events but does not yet raise the
        // DisputeOpened domain event (handler arrives in a later slice). The
        // WebhookEvent row is the persistence anchor for now.
        c.Handler.logger.LogWarning(
            "charge.dispute.created received stripe_account_id={AccountId}; manual review required.",
            c.StripeAccountId);
        return Task.CompletedTask;
    }

    private static async Task OnAccountUpdated(EventContext c)
    {
        // OPS.M.5 §3.8 (D8) — drives Tenant.UpdateStripeAccountReadiness.
        if (c.StripeAccountId is null)
        {
            c.Handler.logger.LogWarning("account.updated arrived without a top-level account id; skipping.");
            return;
        }
        var data = c.Doc.RootElement.GetProperty("data").GetProperty("object");
        var charges = data.TryGetProperty("charges_enabled", out var ce) && ce.GetBoolean();
        var payouts = data.TryGetProperty("payouts_enabled", out var pe) && pe.GetBoolean();
        await c.Handler.readinessUpdater.UpdateAsync(c.StripeAccountId, charges, payouts, c.Ct);
    }

    private async Task UpdatePiAsync(EventContext c, PaymentStatus status, string? chargeId)
    {
        var pi = await LoadPiAsync(c);
        pi?.UpdateStatus(status, chargeId);
    }

    private async Task<PaymentIntent?> LoadPiAsync(EventContext c)
    {
        var data = c.Doc.RootElement.GetProperty("data").GetProperty("object");
        var stripePaymentIntentId = ExtractPaymentIntentId(data);
        if (string.IsNullOrEmpty(stripePaymentIntentId))
        {
            return null;
        }
        return await repo.GetByStripeIdAsync(stripePaymentIntentId, c.Ct);
    }

    private static string? ExtractPaymentIntentId(JsonElement data)
    {
        if (data.TryGetProperty("payment_intent", out var pi))
        {
            return pi.GetString();
        }
        if (data.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }
        return null;
    }

    // Internal accessor for tests + arch enumeration test.
    internal static IReadOnlyCollection<string> SupportedEventTypes => (IReadOnlyCollection<string>)Dispatch.Keys;
}
