using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Reviews.Application.Commands;

public sealed record SubmitReviewCommand(Guid BookingId, int Rating, string Body) : IRequest<ReviewDto>;
