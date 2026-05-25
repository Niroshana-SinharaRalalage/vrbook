using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

internal sealed class UpdateProfileHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork uow) : IRequestHandler<UpdateProfileCommand, UserDto>
{
    public async Task<UserDto> Handle(UpdateProfileCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous caller.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken)
            ?? throw new NotFoundException("User", currentUser.UserId.Value);

        user.UpdateProfile(cmd.DisplayName, new PhoneNumber(cmd.Phone ?? string.Empty));
        await uow.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }
}
