namespace VrBook.Contracts.Dtos;

public sealed record ThreadDto(
    Guid Id,
    Guid BookingId,
    string BookingReference,
    Guid GuestUserId,
    string GuestDisplayName,
    Guid OwnerUserId,
    string OwnerDisplayName,
    int UnreadCount,
    DateTimeOffset? LastMessageAt,
    string? LastMessagePreview);

public sealed record MessageDto(
    Guid Id,
    Guid ThreadId,
    Guid SenderUserId,
    string SenderDisplayName,
    string Body,
    IReadOnlyList<MessageAttachmentDto> Attachments,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

public sealed record MessageAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl); // pre-signed SAS, 10-min TTL — see proposal §10.4

public sealed record SendMessageRequest(
    string Body,
    IReadOnlyList<Guid>? AttachmentIds);

public sealed record MarkReadRequest(Guid UpToMessageId);

public sealed record RealtimeNegotiateResponse(
    string Url,
    string AccessToken,
    DateTimeOffset ExpiresAt);
