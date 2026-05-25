using MediatR;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

internal sealed class ProvisionUserHandler(
    IUserRepository users,
    IUnitOfWork uow) : IRequestHandler<ProvisionUserCommand, Guid>
{
    public async Task<Guid> Handle(ProvisionUserCommand cmd, CancellationToken cancellationToken)
    {
        var existing = await users.GetByB2CObjectIdAsync(cmd.B2CObjectId, cancellationToken);
        if (existing is not null)
        {
            existing.RefreshFromLogin(cmd.DisplayName, cmd.EmailVerified);
            if (cmd.IsOwner && !existing.IsOwner)
            {
                existing.GrantOwner();
            }

            if (cmd.IsAdmin && !existing.IsAdmin)
            {
                existing.GrantAdmin();
            }

            await uow.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var user = User.Provision(
            cmd.B2CObjectId,
            new Email(cmd.Email),
            cmd.DisplayName,
            cmd.EmailVerified,
            cmd.IsOwner,
            cmd.IsAdmin);

        await users.AddAsync(user, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}
