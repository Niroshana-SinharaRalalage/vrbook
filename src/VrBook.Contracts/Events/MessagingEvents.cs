namespace VrBook.Contracts.Events;

// OPS.M.4 Step 1 — only MessageSent has a cross-module consumer (Notifications);
// MessageRead + MessageDeliveryDeferred stay same-module.

public sealed record MessageSent(
    Guid MessageId,
    Guid ThreadId,
    Guid SenderUserId,
    Guid RecipientUserId,
    string BodyPreview,
    Guid TenantId) : DomainEvent;

public sealed record MessageRead(
    Guid ThreadId,
    Guid ReaderUserId,
    Guid UpToMessageId) : DomainEvent;

/// <summary>
/// Raised when a recipient has been offline > 10 min and a message awaits delivery —
/// Notifications module sends the email fallback. See proposal §10.1.
/// </summary>
public sealed record MessageDeliveryDeferred(
    Guid MessageId,
    Guid ThreadId,
    Guid RecipientUserId) : DomainEvent;
