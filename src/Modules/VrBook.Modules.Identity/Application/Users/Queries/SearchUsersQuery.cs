using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Identity.Application.Users.Queries;

public sealed record SearchUsersQuery(string? Q, int Page, int Size)
    : IRequest<OffsetPagedResult<UserDto>>;
