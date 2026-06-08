using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Reviews.Domain;

/// <summary>
/// One review per booking, by the guest, after CheckedOut. Stars 1-5 plus optional body.
/// Owner may post a single reply. A6 v1 ships everything Approved-by-default (no moderation
/// queue) - moderation lands in A6.1 with the Hidden / Rejected states.
/// </summary>
public sealed class Review : AggregateRoot
{
    public Guid BookingId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid GuestUserId { get; private set; }
    public string GuestDisplayName { get; private set; } = default!;
    public int Rating { get; private set; }
    public string Body { get; private set; } = default!;
    public ReviewStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public string? ResponseBody { get; private set; }
    public DateTimeOffset? ResponseAt { get; private set; }

    private Review() { } // EF

    public static Review Submit(
        Guid bookingId,
        Guid propertyId,
        Guid guestUserId,
        string guestDisplayName,
        int rating,
        string body)
    {
        if (rating < 1 || rating > 5)
        {
            throw new BusinessRuleViolationException("review.rating", "Rating must be between 1 and 5.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(guestDisplayName);
        var trimmedBody = (body ?? string.Empty).Trim();
        if (trimmedBody.Length > 4000)
        {
            throw new BusinessRuleViolationException("review.body", "Review body cannot exceed 4000 characters.");
        }
        return new Review
        {
            Id = Guid.NewGuid(),
            BookingId = bookingId,
            PropertyId = propertyId,
            GuestUserId = guestUserId,
            GuestDisplayName = guestDisplayName.Trim(),
            Rating = rating,
            Body = trimmedBody,
            Status = ReviewStatus.Approved,
            PublishedAt = DateTimeOffset.UtcNow,
        };
    }

    public void AddOwnerResponse(string body)
    {
        if (ResponseBody is not null)
        {
            throw new BusinessRuleViolationException("review.response_once", "Only one host response is allowed.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ResponseBody = body.Trim();
        ResponseAt = DateTimeOffset.UtcNow;
    }
}
