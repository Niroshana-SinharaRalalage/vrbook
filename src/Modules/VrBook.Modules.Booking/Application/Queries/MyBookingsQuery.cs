using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Queries;

public sealed record MyBookingsQuery(string? Cursor, int Limit) : IRequest<PagedResult<BookingSummaryDto>>;
