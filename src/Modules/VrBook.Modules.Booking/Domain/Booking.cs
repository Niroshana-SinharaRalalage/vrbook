using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Booking aggregate root. Implements the state machine in proposal §7.1.
/// Phase-1 simplifications: no hold integration, no payment integration, no
/// loyalty discount, no refund computation. Those land in A4.1+.
/// </summary>
public sealed class Booking : AggregateRoot
{
    /// <summary>
    /// Tenant the booking belongs to (inherits from the property's tenant -
    /// guests are tenant-less per MTOP §1). Per OPS_M_3_PLAN §3.1 — `Guid?`
    /// during 3a/3b; flips to `Guid` in 3c.
    /// </summary>
    public Guid TenantId { get; private set; }

    public string Reference { get; private set; } = default!;
    public Guid PropertyId { get; private set; }
    public string PropertyTitle { get; private set; } = default!;
    public Guid GuestUserId { get; private set; }
    public string GuestDisplayName { get; private set; } = default!;
    public Stay Stay { get; private set; } = default!;
    public int GuestCount { get; private set; }

    public BookingStatus Status { get; private set; }
    public BookingSource Source { get; private set; }
    public string Currency { get; private set; } = "USD";

    public decimal Subtotal { get; private set; }
    public decimal Fees { get; private set; }
    public decimal Taxes { get; private set; }
    /// <summary>Loyalty / promo discount captured at booking time. Wired in A4.1 + A8.</summary>
    public decimal Discount { get; internal set; }
    public decimal Total { get; private set; }

    public CancellationPolicyCode CancellationPolicy { get; private set; } = CancellationPolicyCode.Moderate;

    public DateTimeOffset? TentativeUntil { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CheckedOutAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SpecialRequests { get; private set; }

    /// <summary>
    /// Slice OPS.M.16 — per-booking override of the property's default
    /// <c>TurnoverHours</c>. Null when the property default applies. Set
    /// via <see cref="ScheduleCompletion(int)"/> after check-out to push
    /// the auto-completion window out (e.g. damage-check delayed).
    /// </summary>
    public int? TurnoverHoursOverride { get; private set; }

    /// <summary>
    /// Slice OPS.M.16 — snapshotted absolute timestamp when the daily
    /// completion sweep will flip this booking <c>CheckedOut → Completed</c>.
    /// Stamped at CheckOut time from <c>CheckedOutAt + TurnoverHoursOverride
    /// ?? property.TurnoverHours</c>. Rewritten by <see cref="ScheduleCompletion"/>.
    /// Snapshot semantics: changing the property's default mid-stay does NOT
    /// shift in-flight due-ats.
    /// </summary>
    public DateTimeOffset? CompletionDueAt { get; private set; }

    private readonly List<BookingLineItem> _lineItems = new();
    public IReadOnlyList<BookingLineItem> LineItems => _lineItems;

    private readonly List<BookingGuestEntry> _guests = new();
    public IReadOnlyList<BookingGuestEntry> Guests => _guests;

    private Booking() { } // EF

    public static Booking Place(
        Guid tenantId,
        Guid propertyId,
        string propertyTitle,
        Guid guestUserId,
        string guestDisplayName,
        Stay stay,
        int guestCount,
        string currency,
        decimal subtotal,
        decimal fees,
        decimal taxes,
        decimal total,
        IEnumerable<(string kind, string label, int qty, decimal unit, decimal lineTotal)> lineItems,
        IEnumerable<(string fullName, bool isPrimary)> guests,
        string? specialRequests,
        TimeSpan tentativeSla)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(guestDisplayName);
        ArgumentOutOfRangeException.ThrowIfLessThan(guestCount, 1);

        var b = new Booking
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Reference = BookingReference.Generate(),
            PropertyId = propertyId,
            PropertyTitle = propertyTitle,
            GuestUserId = guestUserId,
            GuestDisplayName = guestDisplayName,
            Stay = stay,
            GuestCount = guestCount,
            Status = BookingStatus.Tentative,
            Source = BookingSource.Direct,
            Currency = currency.ToUpperInvariant(),
            Subtotal = subtotal,
            Fees = fees,
            Taxes = taxes,
            Total = total,
            TentativeUntil = DateTimeOffset.UtcNow.Add(tentativeSla), // VRB-207 (G2) — config-driven (48h locked), was hard-coded AddHours(24)
            SpecialRequests = string.IsNullOrWhiteSpace(specialRequests) ? null : specialRequests.Trim(),
        };
        foreach (var (kind, label, qty, unit, lineTotal) in lineItems)
        {
            b._lineItems.Add(new BookingLineItem(b.Id, kind, label, qty, unit, lineTotal));
        }
        foreach (var (name, isPrimary) in guests)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            b._guests.Add(new BookingGuestEntry(b.Id, name, isPrimary));
        }
        b.Raise(new BookingPlaced(b.Id, b.Reference, propertyId, guestUserId,
            stay.CheckinDate, stay.CheckoutDate, b.TentativeUntil!.Value, b.TenantId));
        return b;
    }

    // ---- State transitions ----
    public void Confirm()
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        TentativeUntil = null;
        Raise(new BookingConfirmed(Id, Reference, PropertyId, GuestUserId,
            Stay.CheckinDate, Stay.CheckoutDate, "owner", TenantId));
    }

    /// <summary>Slice 0.4: SLA worker auto-confirms when the tentative window
    /// expires AND no iCal conflict was detected. Same state transition as
    /// <see cref="Confirm"/> but the event Trigger field is "sla" instead of "owner".</summary>
    public void AutoConfirm()
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        TentativeUntil = null;
        Raise(new BookingConfirmed(Id, Reference, PropertyId, GuestUserId,
            Stay.CheckinDate, Stay.CheckoutDate, "sla", TenantId));
    }

    /// <summary>Slice 0.4: SLA worker auto-cancels when the tentative window
    /// expires AND a conflict was detected (or auth-hold lapsed). Transitions to
    /// Cancelled (not Rejected) because it isn't the owner's choice.</summary>
    public void AutoExpire(string reason)
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Tentative window expired without owner action" : reason.Trim();
        Raise(new BookingCancelled(Id, Reference, PropertyId, GuestUserId, "sla", 0m, Currency, TenantId));
    }

    public void Reject(string reason)
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Rejected;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Rejected by host" : reason.Trim();
        Raise(new BookingRejected(Id, Reference, PropertyId, GuestUserId, CancellationReason, TenantId));
    }

    public void CancelByGuest(string reason)
    {
        if (Status is not (BookingStatus.Tentative or BookingStatus.Confirmed))
        {
            throw new BusinessRuleViolationException("booking.cancel", $"Cannot cancel a booking in {Status} state.");
        }
        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by guest" : reason.Trim();
        // A4 v1: refund computation lives in A5 (Payment). Publish 0 for now.
        Raise(new BookingCancelled(Id, Reference, PropertyId, GuestUserId, "guest", 0m, Currency, TenantId));
    }

    public void CheckIn()
    {
        Require(BookingStatus.Confirmed);
        Status = BookingStatus.CheckedIn;
        CheckedInAt = DateTimeOffset.UtcNow;
        Raise(new BookingCheckedIn(Id, Reference));
    }

    /// <summary>
    /// Slice OPS.M.16 — CheckOut takes the property's turnover default so
    /// we can snapshot <see cref="CompletionDueAt"/> at the moment the stay
    /// ends. If a prior <see cref="ScheduleCompletion"/> call set
    /// <see cref="TurnoverHoursOverride"/>, that override wins; otherwise
    /// <paramref name="propertyTurnoverHours"/> is used.
    /// </summary>
    public void CheckOut(int propertyTurnoverHours)
    {
        Require(BookingStatus.CheckedIn);
        if (propertyTurnoverHours < 0)
        {
            throw new BusinessRuleViolationException(
                "booking.turnover_hours_negative",
                $"propertyTurnoverHours must be >= 0; got {propertyTurnoverHours}.");
        }
        Status = BookingStatus.CheckedOut;
        CheckedOutAt = DateTimeOffset.UtcNow;
        var effective = TurnoverHoursOverride ?? propertyTurnoverHours;
        CompletionDueAt = CheckedOutAt.Value.AddHours(effective);
        Raise(new BookingCheckedOut(Id, Reference));
    }

    /// <summary>
    /// Slice OPS.M.16 — override the auto-completion window after check-out.
    /// Recomputes <see cref="CompletionDueAt"/> from
    /// <see cref="CheckedOutAt"/> + <paramref name="hoursFromCheckedOutAt"/>.
    /// Domain caps at 168h (one week). 0h means "auto-complete on next
    /// sweep tick" — use <see cref="CompleteManually"/> for truly-immediate.
    /// </summary>
    public void ScheduleCompletion(int hoursFromCheckedOutAt)
    {
        Require(BookingStatus.CheckedOut);
        if (hoursFromCheckedOutAt < 0 || hoursFromCheckedOutAt > 168)
        {
            throw new BusinessRuleViolationException(
                "booking.turnover_hours_out_of_range",
                $"hoursFromCheckedOutAt must be between 0 and 168 (one week); got {hoursFromCheckedOutAt}.");
        }
        if (CheckedOutAt is null)
        {
            throw new BusinessRuleViolationException(
                "booking.checked_out_at_missing",
                "Cannot schedule completion — CheckedOutAt is missing though status is CheckedOut. Data integrity bug.");
        }
        TurnoverHoursOverride = hoursFromCheckedOutAt;
        CompletionDueAt = CheckedOutAt.Value.AddHours(hoursFromCheckedOutAt);
        Raise(new BookingCompletionRescheduled(Id, CompletionDueAt.Value, hoursFromCheckedOutAt, TenantId));
    }

    /// <summary>
    /// Slice 5: the post-stay terminal transition. Called by the daily
    /// <c>BookingCompletionWorker</c> (cron <c>0 6 * * *</c>) when
    /// <see cref="CompletionDueAt"/> is in the past. Raises
    /// <see cref="BookingCompleted"/> so Loyalty increments the stay count
    /// and Notifications enqueues the "thanks for staying" + review-request
    /// emails. CheckOut no longer raises this — the daily sweep is the sole
    /// automatic trigger; admin-initiated manual completion goes through
    /// <see cref="CompleteManually"/> (M.16).
    /// </summary>
    public void Complete()
    {
        Require(BookingStatus.CheckedOut);
        Status = BookingStatus.Completed;
        Raise(new BookingCompleted(Id, Reference, GuestUserId, TenantId, Trigger: "sweep"));
    }

    /// <summary>
    /// Slice OPS.M.16 — admin-initiated manual completion. Distinct from
    /// the sweep-triggered <see cref="Complete"/> so the emitted event
    /// carries <c>Trigger = "manual"</c> for observability; downstream
    /// handlers (Loyalty, Notifications) treat both triggers identically.
    /// </summary>
    public void CompleteManually()
    {
        Require(BookingStatus.CheckedOut);
        Status = BookingStatus.Completed;
        Raise(new BookingCompleted(Id, Reference, GuestUserId, TenantId, Trigger: "manual"));
    }

    private void Require(BookingStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessRuleViolationException(
                "booking.state",
                $"Cannot transition from {Status} when {expected} is required.");
        }
    }
}
