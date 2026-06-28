using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Commands;

/// <summary>
/// OPS.M.8 §3.5 (D5) — platform-admin suspension of a target tenant. Distinct
/// from OPS.M.5's <c>SetTenantPlatformFeeBpsCommand</c>: this command is NOT
/// <c>ITenantScoped</c>. The target tenant id is the <em>subject</em> of the
/// operation, not a tenant-scope-of-self gate. The
/// <c>[Authorize(Roles="PlatformAdmin")]</c> filter at the controller +
/// the handler's defense-in-depth check are the only auth surface.
/// </summary>
public sealed record SuspendTenantCommand(Guid TargetTenantId, string Reason) : IRequest;

internal sealed class SuspendTenantHandler(
    ICurrentUser currentUser,
    IdentityDbContext db,
    IUnitOfWork uow)
    : IRequestHandler<SuspendTenantCommand>
{
    public async Task Handle(SuspendTenantCommand cmd, CancellationToken cancellationToken)
    {
        // Defense-in-depth per OPS.M.8 §7. The controller's [Authorize] is the
        // first gate; this re-check ensures the command can never be dispatched
        // from inside another handler that ran under a non-admin caller.
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException(
                "SuspendTenantCommand requires platform-admin privileges.");
        }
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("SuspendTenantCommand requires an authenticated user.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TargetTenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TargetTenantId);

        tenant.Suspend(cmd.Reason, currentUser.UserId.Value);
        await uow.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// OPS.M.8 §3.5 (D5) — platform-admin reactivation of a suspended tenant.
/// </summary>
public sealed record ReactivateTenantCommand(Guid TargetTenantId) : IRequest;

internal sealed class ReactivateTenantHandler(
    ICurrentUser currentUser,
    IdentityDbContext db,
    IUnitOfWork uow)
    : IRequestHandler<ReactivateTenantCommand>
{
    public async Task Handle(ReactivateTenantCommand cmd, CancellationToken cancellationToken)
    {
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException(
                "ReactivateTenantCommand requires platform-admin privileges.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TargetTenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TargetTenantId);

        tenant.Reactivate();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
