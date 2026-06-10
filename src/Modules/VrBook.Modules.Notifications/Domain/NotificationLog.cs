using VrBook.Domain.Common;

namespace VrBook.Modules.Notifications.Domain;

public enum NotificationKind
{
    BookingPlaced = 0,
    BookingConfirmed = 1,
    BookingRejected = 2,
    BookingCancelled = 3,
    BookingCheckedIn = 4,
    BookingCheckedOut = 5,
    BookingCompleted = 6,
    MessageDeliveryDeferred = 7,
    PaymentCaptured = 10,
    RefundIssued = 11,
    ReviewSubmitted = 20,
}

public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    DeadLetter = 3,
}

/// <summary>
/// Durable record of an outbound notification (typically email). Created when a
/// domain event lands; the A9 worker picks <c>Queued</c> rows and calls ACS.
/// On failure, increments <see cref="RetryCount"/> until 3 then transitions to
/// <see cref="NotificationStatus.DeadLetter"/> per the runbook.
///
/// Phase 1: actual ACS dispatch is deferred. Rows are persisted with
/// <c>Status=Queued</c> for the A9 worker to drain when the resource is wired.
/// Until then they are the audit trail / replay log.
/// </summary>
public sealed class NotificationLog : AggregateRoot
{
    public NotificationKind Kind { get; private set; }
    public NotificationStatus Status { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    private NotificationLog() { } // EF

    public static NotificationLog Queue(
        NotificationKind kind,
        Guid recipientUserId,
        string recipientEmail,
        string subject,
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        return new NotificationLog
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            Status = NotificationStatus.Queued,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail.Trim(),
            Subject = subject.Trim(),
            PayloadJson = payloadJson,
        };
    }

    public void MarkSent(DateTimeOffset at)
    {
        Status = NotificationStatus.Sent;
        SentAt = at;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error;
        Status = RetryCount >= 3 ? NotificationStatus.DeadLetter : NotificationStatus.Failed;
    }
}
