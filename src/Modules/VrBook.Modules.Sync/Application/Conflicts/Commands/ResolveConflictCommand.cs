using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.Conflicts.Commands;

public sealed record ResolveConflictCommand(
    Guid Id,
    SyncConflictResolution Resolution,
    string Notes) : IRequest;

internal sealed class ResolveConflictHandler(SyncDbContext db) : IRequestHandler<ResolveConflictCommand>
{
    public async Task Handle(ResolveConflictCommand cmd, CancellationToken cancellationToken)
    {
        var conflict = await db.SyncConflicts.FirstOrDefaultAsync(c => c.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("SyncConflict", cmd.Id);
        conflict.Resolve(cmd.Resolution, cmd.Notes);
        await db.SaveChangesAsync(cancellationToken);
    }
}
