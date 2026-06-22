using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 5: queues the <c>loyalty.tier_promotion</c> email when Loyalty raises
/// <see cref="TierPromoted"/>. Mirrors <see cref="BookingNotificationHandlers"/>:
/// looks up the user's email via <see cref="IUserEmailLookup"/>, computes the
/// new discount %, and dumps it into the payload extras the renderer reads.
/// </summary>
internal sealed class LoyaltyNotificationHandlers(
    NotificationsDbContext db,
    IUserEmailLookup users,
    ILogger<LoyaltyNotificationHandlers> logger) : INotificationHandler<TierPromoted>
{
    public async Task Handle(TierPromoted n, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(n.UserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "User {UserId} not found in identity.users; skipping LoyaltyTierPromotion notification.",
                n.UserId);
            return;
        }

        var newDiscountPct = DiscountFor(n.ToTier);

        var payload = new Dictionary<string, object>
        {
            ["UserId"] = n.UserId.ToString(),
            ["GuestDisplayName"] = user.DisplayName,
            ["OldTier"] = n.FromTier.ToString(),
            ["NewTier"] = n.ToTier.ToString(),
            ["CompletedStayCount"] = n.CompletedStayCount,
            ["NewDiscountPct"] = newDiscountPct.ToString("0.##"),
        };

        var log = NotificationLog.Queue(
            kind: NotificationKind.LoyaltyTierPromotion,
            recipientUserId: n.UserId,
            recipientEmail: user.Email,
            subject: $"Welcome to {n.ToTier}",
            payloadJson: JsonSerializer.Serialize(payload));
        db.Logs.Add(log);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Queued LoyaltyTierPromotion {OldTier}->{NewTier} for user {UserId} ({LogId}) -> {RecipientEmail}.",
            n.FromTier, n.ToTier, n.UserId, log.Id, user.Email);
    }

    private static decimal DiscountFor(VrBook.Contracts.Enums.LoyaltyTier tier) => tier switch
    {
        VrBook.Contracts.Enums.LoyaltyTier.Gold => 10m,
        VrBook.Contracts.Enums.LoyaltyTier.Silver => 5m,
        _ => 0m,
    };
}
