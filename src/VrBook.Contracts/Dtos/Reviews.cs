using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record ReviewDto(
    Guid Id,
    Guid BookingId,
    Guid PropertyId,
    Guid GuestUserId,
    string GuestDisplayName,
    int Rating,
    string Body,
    ReviewStatus Status,
    DateTimeOffset? PublishedAt,
    ReviewResponseDto? Response,
    DateTimeOffset CreatedAt);

public sealed record ReviewResponseDto(
    Guid Id,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record SubmitReviewRequest(int Rating, string Body);

public sealed record SubmitReviewResponseRequest(string Body);

public sealed record ModerateReviewRequest(string? Reason);
