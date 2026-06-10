using VrBook.Domain.Common;

namespace VrBook.Modules.Messaging.Domain;

/// <summary>
/// One conversation between a guest and an owner about a booking. Auto-created
/// by the <c>BookingConfirmed</c> event consumer (A7.4 — first real event
/// consumer, proves the bus works end-to-end). Subsequent messages are appended
/// via the public REST surface.
///
/// Phase-1 simplifications:
///   - one thread per booking (unique on <c>BookingId</c>)
///   - exactly two participants (guest + owner) — group threads are Phase 2
///   - attachments live on the Message aggregate when A7.5 ships
/// </summary>
public sealed class MessageThread : AggregateRoot
{
    public Guid BookingId { get; private set; }
    public string BookingReference { get; private set; } = default!;
    public Guid GuestUserId { get; private set; }
    public string GuestDisplayName { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public string OwnerDisplayName { get; private set; } = default!;

    public DateTimeOffset? LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }

    private MessageThread() { } // EF

    public static MessageThread CreateForBooking(
        Guid bookingId,
        string bookingReference,
        Guid guestUserId,
        string guestDisplayName,
        Guid ownerUserId,
        string ownerDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookingReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(guestDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerDisplayName);

        return new MessageThread
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            BookingReference = bookingReference,
            GuestUserId = guestUserId,
            GuestDisplayName = guestDisplayName.Trim(),
            OwnerUserId = ownerUserId,
            OwnerDisplayName = ownerDisplayName.Trim(),
        };
    }

    /// <summary>Called by the SendMessage handler after the message row is added.</summary>
    public void RecordSent(string bodyPreview, DateTimeOffset at)
    {
        LastMessageAt = at;
        LastMessagePreview = bodyPreview;
    }

    /// <summary>True iff the given user is one of the two participants.</summary>
    public bool IsParticipant(Guid userId) =>
        userId == GuestUserId || userId == OwnerUserId;

    public Guid CounterpartyOf(Guid userId)
    {
        if (userId == GuestUserId)
        {
            return OwnerUserId;
        }
        if (userId == OwnerUserId)
        {
            return GuestUserId;
        }
        throw new BusinessRuleViolationException(
            "messaging.thread.not_participant",
            "User is not a participant in this thread.");
    }
}
