using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Modules.Loyalty.Domain;
using VrBook.Modules.Loyalty.Infrastructure.Persistence;

namespace VrBook.Modules.Loyalty.Application.Accounts.Handlers;

/// <summary>
/// A8.1.4 — when a booking completes (guest checked out), increment that guest's
/// stay count and recompute their tier. Opens a new LoyaltyAccount on first
/// completion. Pure read-side: doesn't refuse, doesn't replay charges.
/// </summary>
internal sealed class OnBookingCompletedHandler(
    LoyaltyDbContext db,
    ILogger<OnBookingCompletedHandler> logger) : INotificationHandler<BookingCompleted>
{
    public async Task Handle(BookingCompleted notification, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(
            a => a.UserId == notification.GuestUserId, cancellationToken);
        if (account is null)
        {
            account = LoyaltyAccount.OpenForUser(notification.GuestUserId);
            db.Accounts.Add(account);
        }

        var beforeTier = account.Tier;
        account.RecordCompletedStay();
        await db.SaveChangesAsync(cancellationToken);

        if (account.Tier != beforeTier)
        {
            logger.LogInformation(
                "Loyalty tier upgrade for user {UserId}: {OldTier} → {NewTier} after {Stays} stays.",
                notification.GuestUserId, beforeTier, account.Tier, account.CompletedStayCount);
        }
    }
}
