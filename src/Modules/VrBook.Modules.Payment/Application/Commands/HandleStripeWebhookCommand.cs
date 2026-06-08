using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Modules.Payment.Domain;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment.Application.Commands;

public sealed record HandleStripeWebhookCommand(string Payload, string SignatureHeader) : IRequest<bool>;

internal sealed class HandleStripeWebhookHandler(
    IStripeGateway stripe,
    IPaymentIntentRepository repo,
    PaymentDbContext db,
    ILogger<HandleStripeWebhookHandler> logger)
    : IRequestHandler<HandleStripeWebhookCommand, bool>
{
    public async Task<bool> Handle(HandleStripeWebhookCommand cmd, CancellationToken cancellationToken)
    {
        if (!stripe.VerifyWebhookSignature(cmd.Payload, cmd.SignatureHeader, out var eventType, out var rawJson))
        {
            return false;
        }

        var doc = JsonDocument.Parse(rawJson!);
        var stripeEventId = doc.RootElement.GetProperty("id").GetString()!;

        // Idempotency: have we seen this Stripe event id before? (Stripe event id is a
        // string like evt_... — not our Guid Id, so query the unique index, not Find.)
        var seen = await db.WebhookEvents
            .AnyAsync(w => w.StripeEventId == stripeEventId, cancellationToken);
        if (seen)
        {
            logger.LogDebug("Stripe webhook {EventId} already processed.", stripeEventId);
            return true;
        }
        var wh = new WebhookEvent(stripeEventId, eventType!, rawJson!);
        db.WebhookEvents.Add(wh);

        try
        {
            await DispatchAsync(eventType!, doc, cancellationToken);
            wh.MarkProcessed();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook dispatch failed for {EventType}", eventType);
            // Persist the event row even on failure - we don't want a poison message to loop.
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task DispatchAsync(string eventType, JsonDocument doc, CancellationToken cancellationToken)
    {
        var data = doc.RootElement.GetProperty("data").GetProperty("object");
        var stripePaymentIntentId = ExtractPaymentIntentId(data);
        if (string.IsNullOrEmpty(stripePaymentIntentId))
        {
            return;
        }
        var local = await repo.GetByStripeIdAsync(stripePaymentIntentId, cancellationToken);
        if (local is null)
        {
            return;
        }

        switch (eventType)
        {
            case "payment_intent.amount_capturable_updated":
                local.UpdateStatus(PaymentStatus.RequiresCapture, null);
                break;
            case "payment_intent.succeeded":
                local.UpdateStatus(PaymentStatus.Succeeded, data.TryGetProperty("latest_charge", out var c) ? c.GetString() : null);
                break;
            case "payment_intent.payment_failed":
                local.MarkFailed(data.TryGetProperty("last_payment_error", out var e) ? e.ToString() : "unknown");
                break;
            case "payment_intent.canceled":
                local.UpdateStatus(PaymentStatus.Cancelled, null);
                break;
        }
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
}
