using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.Conflicts.Commands;

/// <summary>
/// OPS.M.6 §3.5 (D5) Step 5 — closes the pre-M.6 gap where
/// <c>SyncConflictsController.Resolve</c> dispatched without a tenant gate.
/// The command now implements <see cref="ITenantScoped"/>; the controller
/// stamps <c>CallerTenantId()</c>; <c>TenantAuthorizationBehavior</c> rejects
/// cross-tenant resolves with <c>CrossTenantAccessException</c>.
/// </summary>
public sealed record ResolveConflictCommand(
    Guid Id,
    SyncConflictResolution Resolution,
    string Notes,
    Guid TenantId) : IRequest, ITenantScoped;

internal sealed class ResolveConflictHandler(SyncDbContext db) : IRequestHandler<ResolveConflictCommand>
{
    public async Task Handle(ResolveConflictCommand cmd, CancellationToken cancellationToken)
    {
        // The behavior already verified ICurrentUser.TenantId == cmd.TenantId.
        // Belt-and-braces row-level check: refuse if the conflict's tenant
        // doesn't match (data corruption / stale id).
        var conflict = await db.SyncConflicts
            .FirstOrDefaultAsync(c => c.Id == cmd.Id && c.TenantId == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("SyncConflict", cmd.Id);
        conflict.Resolve(cmd.Resolution, cmd.Notes);
        await db.SaveChangesAsync(cancellationToken);
    }
}
