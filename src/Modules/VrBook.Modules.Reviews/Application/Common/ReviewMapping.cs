using VrBook.Contracts.Dtos;
using VrBook.Modules.Reviews.Domain;

namespace VrBook.Modules.Reviews.Application.Common;

internal static class ReviewMapping
{
    public static ReviewDto ToDto(this Review r) =>
        new(
            Id: r.Id,
            BookingId: r.BookingId,
            PropertyId: r.PropertyId,
            GuestUserId: r.GuestUserId,
            GuestDisplayName: r.GuestDisplayName,
            Rating: r.Rating,
            Body: r.Body,
            Status: r.Status,
            PublishedAt: r.PublishedAt,
            Response: r.ResponseBody is null
                ? null
                : new ReviewResponseDto(r.Id, r.ResponseBody, r.ResponseAt ?? r.UpdatedAt),
            CreatedAt: r.CreatedAt);
}
