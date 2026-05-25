using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Users.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Queries;

internal sealed class SearchUsersHandler(IUserRepository users)
    : IRequestHandler<SearchUsersQuery, OffsetPagedResult<UserDto>>
{
    public async Task<OffsetPagedResult<UserDto>> Handle(SearchUsersQuery q, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.Size, 1, 100);
        var total = await users.CountAsync(q.Q, cancellationToken);
        var rows = await users.SearchAsync(q.Q, (page - 1) * size, size, cancellationToken);
        return new OffsetPagedResult<UserDto>(rows.Select(u => u.ToDto()).ToList(), page, size, total);
    }
}
