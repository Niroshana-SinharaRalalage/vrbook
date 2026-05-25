using MediatR;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

internal sealed class DeactivateMeHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork uow) : IRequestHandler<DeactivateMeCommand>
{
    public async Task Handle(DeactivateMeCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Anonymous caller.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId.Value, cancellationToken)
            ?? throw new NotFoundException("User", currentUser.UserId.Value);

        user.Deactivate(cmd.Reason, currentUser.UserId.Value);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
