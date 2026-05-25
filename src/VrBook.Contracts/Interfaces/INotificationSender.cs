using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Notifications boundary. Any module may publish <c>NotificationRequested</c> via
/// MediatR/Service Bus; the Notifications worker resolves this to render + dispatch.
/// SendGrid implementation in Phase 1; SMS/Push reserved for Phase 2.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(NotificationDispatchRequest request, CancellationToken ct = default);
}

public sealed record NotificationDispatchRequest(
    string TemplateKey,
    NotificationChannel Channel,
    string RecipientEmail,
    string? RecipientUserId,
    IReadOnlyDictionary<string, object?> Variables);
