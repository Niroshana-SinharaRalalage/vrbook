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

    // Slice 5
    ReviewRequest = 21,
    LoyaltyTierPromotion = 22,

    // Slice 4 C4: owner-side notifications. Reserved at 30+ so the guest-side
    // enum values stay stable; OwnerNotificationHandlers queues these.
    OwnerTentativeReceived = 30,
    OwnerActionRequiredReminder = 31,
    OwnerAutoConfirmed = 32,
    OwnerCancellationAlert = 33,
    OwnerSyncConflict = 34,

    // Slice 4.V2 (M.15 App Roles cleanup + notifications residuals): lifecycle-
    // of-user templates. Reserved at 40+ so the booking/owner enums stay stable.
    // TenantNotificationHandlers queues TenantWelcome on TenantMembershipCreated
    // when the row is the tenant's first tenant_admin membership; GuestWelcome
    // fires on UserRegistered for non-tenant-admin signups.
    TenantWelcome = 40,
}

public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    DeadLetter = 3,

    /// <summary>
    /// Slice 4 C2: the dispatch worker has leased the row and is in the middle of
    /// the ACS call. If the worker crashes between <see cref="NotificationLog.Lease"/>
    /// and <see cref="NotificationLog.MarkSent"/>/<see cref="NotificationLog.RecordFailure"/>,
    /// the row sits in <c>Sending</c> with a stale <see cref="NotificationLog.DispatchStartedAt"/>;
    /// the next worker tick resets it to <c>Queued</c> after a 5-minute timeout
    /// (see <see cref="NotificationLog.ReleaseLease"/>).
    /// </summary>
    Sending = 4,
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
    /// <summary>
    /// Tenant the notification is *about* (e.g. tenant_admin alerts). Per
    /// OPS_M_3_PLAN §1.6 — nullable forever (no 3c flip). Many notifications
    /// go to guests (BookingConfirmed to a guest email) and have no tenant.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public NotificationKind Kind { get; private set; }
    public NotificationStatus Status { get; private set; }
    public Guid RecipientUserId { get; private set; }
    public string RecipientEmail { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Slice 4 C2: optional deferred-send timestamp. Worker query is
    /// <c>Status=Queued AND (NotBeforeUtc IS NULL OR NotBeforeUtc &lt;= NOW())</c>.
    /// Used by SLICE4_PLAN §2.3 for <c>owner.action_required_24h_reminder</c> and any
    /// future scheduled template.
    /// </summary>
    public DateTimeOffset? NotBeforeUtc { get; private set; }

    /// <summary>
    /// Slice 4 C2: when the worker leases this row (transitions Queued → Sending),
    /// stamps the moment it started the ACS call. If the worker crashes after this
    /// point but before <see cref="MarkSent"/>/<see cref="RecordFailure"/>, the next
    /// tick's <see cref="ReleaseLease"/> resets the row after a 5-minute timeout.
    /// </summary>
    public DateTimeOffset? DispatchStartedAt { get; private set; }

    private NotificationLog() { } // EF

    /// <summary>
    /// OPS.M.4 Step 4 — <c>tenantId</c> is intentionally REQUIRED (no default).
    /// Every call site must pass it consciously: <c>null</c> for guest-bound mail
    /// and loyalty notifications, the originating event's <c>TenantId</c> for
    /// owner-bound mail. The arch test in Step 5 will lock the contract.
    /// </summary>
    public static NotificationLog Queue(
        NotificationKind kind,
        Guid recipientUserId,
        string recipientEmail,
        string subject,
        string payloadJson,
        Guid? tenantId,
        DateTimeOffset? notBeforeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        return new NotificationLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Kind = kind,
            Status = NotificationStatus.Queued,
            RecipientUserId = recipientUserId,
            RecipientEmail = recipientEmail.Trim(),
            Subject = subject.Trim(),
            PayloadJson = payloadJson,
            NotBeforeUtc = notBeforeUtc,
        };
    }

    /// <summary>
    /// Slice 4 C2: claim the row for dispatch. Worker calls this before the ACS
    /// send so multi-replica polling is idempotent. Only valid from
    /// <see cref="NotificationStatus.Queued"/>.
    /// </summary>
    public void Lease(DateTimeOffset at)
    {
        if (Status != NotificationStatus.Queued)
        {
            throw new InvalidOperationException(
                $"Cannot lease NotificationLog {Id}; current status is {Status}.");
        }
        Status = NotificationStatus.Sending;
        DispatchStartedAt = at;
    }

    /// <summary>
    /// Slice 4 C2: revert a stale <see cref="NotificationStatus.Sending"/> row to
    /// <see cref="NotificationStatus.Queued"/> when its lease has expired (worker
    /// crashed mid-send). Caller passes a <paramref name="cutoff"/>; rows with
    /// <see cref="DispatchStartedAt"/> older than the cutoff are reset.
    /// </summary>
    public void ReleaseLease(DateTimeOffset cutoff)
    {
        if (Status != NotificationStatus.Sending)
        {
            return;
        }
        if (DispatchStartedAt is null || DispatchStartedAt > cutoff)
        {
            return;
        }
        Status = NotificationStatus.Queued;
        DispatchStartedAt = null;
        LastError = "lease-expired";
    }

    public void MarkSent(DateTimeOffset at)
    {
        Status = NotificationStatus.Sent;
        SentAt = at;
        LastError = null;
        DispatchStartedAt = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error;
        Status = RetryCount >= 3 ? NotificationStatus.DeadLetter : NotificationStatus.Failed;
        DispatchStartedAt = null;
    }

    /// <summary>
    /// Slice 4 C5: admin "Retry" button. Resets a Failed/DeadLetter row back to
    /// Queued so the next worker tick picks it up. Clears the retry counter so
    /// the row gets a fresh 3-attempt budget.
    /// </summary>
    public void Reset()
    {
        if (Status != NotificationStatus.Failed && Status != NotificationStatus.DeadLetter)
        {
            throw new InvalidOperationException(
                $"Cannot reset NotificationLog {Id}; current status is {Status}.");
        }
        Status = NotificationStatus.Queued;
        RetryCount = 0;
        LastError = null;
        DispatchStartedAt = null;
    }
}
