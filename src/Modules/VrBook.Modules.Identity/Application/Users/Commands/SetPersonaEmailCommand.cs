using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
/// the command. <b>OPS.M.10.2 F8</b> adds a handler-side prod-environment
/// guard so a config flip alone cannot expose this in Production.
/// </summary>
public sealed record SetPersonaEmailCommand(string B2CObjectId, string NewEmail) : IRequest<Unit>;

internal sealed class SetPersonaEmailHandler(
    IdentityDbContext db,
    IHostEnvironment hostEnv,
    IConfiguration configuration,
    ILogger<SetPersonaEmailHandler> logger)
    : IRequestHandler<SetPersonaEmailCommand, Unit>
{
    public async Task<Unit> Handle(SetPersonaEmailCommand request, CancellationToken cancellationToken)
    {
        // Slice OPS.M.10.2 F8 (audit #20) — defense-in-depth prod guard.
        // Pre-fix: only the controller-side DevAuth:AllowAnonymous flag
        // gated this. If that flag were ever true in Production (config
        // mistake, env-var typo), any caller could rewrite any user's
        // email by B2CObjectId — full account takeover via email change.
        // Post-fix: refuse in Production regardless of the flag; AND
        // belt-and-braces re-verify the flag at the handler.
        if (hostEnv.IsProduction())
        {
            throw new ForbiddenException("Dev bridge endpoints are disabled in Production.");
        }
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            throw new ForbiddenException("DevAuth bridge is disabled.");
        }

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
