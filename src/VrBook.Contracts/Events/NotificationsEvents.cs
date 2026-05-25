using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

/// <summary>
/// Published by any module that wants a notification sent. The Notifications worker consumes,
/// renders the template, and dispatches via SendGrid/in-app/etc.
/// </summary>
public sealed record NotificationRequested(
    string TemplateKey,
    NotificationChannel Channel,
    Guid? RecipientUserId,
    string? RecipientEmail,
    IReadOnlyDictionary<string, object?> Variables) : DomainEvent;

public sealed record NotificationDispatched(
    string TemplateKey,
    NotificationChannel Channel,
    string? ProviderMessageId,
    DateTimeOffset SentAt) : DomainEvent;

public sealed record NotificationFailed(
    string TemplateKey,
    NotificationChannel Channel,
    string Error,
    int AttemptNumber) : DomainEvent;
