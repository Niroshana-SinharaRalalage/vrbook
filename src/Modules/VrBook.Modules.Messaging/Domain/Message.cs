using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Messaging.Domain;

/// <summary>
/// One message in a <see cref="MessageThread"/>. Owned by the thread aggregate:
/// only created via <c>SendMessage</c> handler which validates that the sender
/// participates in the thread.
///
/// Body limit: 4000 chars. Attachments are not implemented in A7 v1 (A7.5).
/// </summary>
public sealed class Message : AggregateRoot
{
    /// <summary>
    /// Denormalised tenant id (inherits from MessageThread.TenantId). Per
    /// OPS_M_3_PLAN §1 - the denorm lives so RLS doesn't have to join threads.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public Guid ThreadId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderDisplayName { get; private set; } = default!;
    public Guid RecipientUserId { get; private set; }
    public string Body { get; private set; } = default!;
    public DateTimeOffset SentAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    private Message() { } // EF

    public static Message Send(
        MessageThread thread,
        Guid senderUserId,
        string senderDisplayName,
        string body)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (!thread.IsParticipant(senderUserId))
        {
            throw new BusinessRuleViolationException(
                "messaging.message.not_participant",
                "Sender is not a participant in this thread.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(senderDisplayName);
        var trimmed = (body ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new BusinessRuleViolationException(
                "messaging.message.empty",
                "Message body cannot be empty.");
        }
        if (trimmed.Length > 4000)
        {
            throw new BusinessRuleViolationException(
                "messaging.message.too_long",
                "Message body cannot exceed 4000 characters.");
        }

        var recipient = thread.CounterpartyOf(senderUserId);
        var messageTenantId = thread.TenantId ?? throw new InvalidOperationException(
            "Thread has no TenantId; cannot send message. Aggregate invariant violated.");
        var message = new Message
        {
            Id = Guid.NewGuid(),
            TenantId = messageTenantId,
            ThreadId = thread.Id,
            SenderUserId = senderUserId,
            SenderDisplayName = senderDisplayName.Trim(),
            RecipientUserId = recipient,
            Body = trimmed,
            SentAt = DateTimeOffset.UtcNow,
        };

        var preview = trimmed.Length > 80 ? string.Concat(trimmed.AsSpan(0, 77), "…") : trimmed;
        message.Raise(new MessageSent(message.Id, thread.Id, senderUserId, recipient, preview));
        return message;
    }

    /// <summary>Marks this message read by its recipient. Idempotent.</summary>
    public void MarkRead(Guid readerUserId, DateTimeOffset at)
    {
        if (readerUserId != RecipientUserId)
        {
            throw new BusinessRuleViolationException(
                "messaging.message.not_recipient",
                "Only the recipient can mark a message read.");
        }
        ReadAt ??= at;
    }
}
