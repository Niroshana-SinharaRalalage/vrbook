using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Queries;

internal sealed class GetMeHandler(
    ICurrentUser currentUser,
    IUserRepository users) : IRequestHandler<GetMeQuery, UserDto>
{
    public async Task<UserDto> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous user has no profile.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken)
            ?? throw new NotFoundException("User", currentUser.UserId.Value);

        return user.ToDto();
    }
}
