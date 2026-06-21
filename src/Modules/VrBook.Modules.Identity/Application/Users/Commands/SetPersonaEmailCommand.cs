using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Slice 4 dev bridge: repoint a DevAuth persona's User row at a real
/// inbox address. Lets a queued notification (whose RecipientEmail is
/// resolved at queue time by <see cref="IUserEmailLookup"/>) reach a real
/// mailbox on next-booking. Intended for staging walk-throughs only;
/// the controller checks <c>DevAuth:AllowAnonymous</c> before invoking
/// the command.
/// </summary>
public sealed record SetPersonaEmailCommand(string B2CObjectId, string NewEmail) : IRequest<Unit>;

internal sealed class SetPersonaEmailHandler(
    IdentityDbContext db,
    ILogger<SetPersonaEmailHandler> logger)
    : IRequestHandler<SetPersonaEmailCommand, Unit>
{
    public async Task<Unit> Handle(SetPersonaEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.B2CObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NewEmail);

        var user = await db.Users.FirstOrDefaultAsync(u => u.B2CObjectId == request.B2CObjectId, cancellationToken)
            ?? throw new NotFoundException("User", request.B2CObjectId);

        var oldEmail = user.Email.Value;
        user.SetEmail(new Email(request.NewEmail.Trim()));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Dev bridge: persona {Oid} email updated {OldEmail} -> {NewEmail}.",
            request.B2CObjectId, oldEmail, request.NewEmail);

        return Unit.Value;
    }
}
