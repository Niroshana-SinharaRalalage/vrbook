using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Domain;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin.Application.Settings;

// VRB-216 — platform-global cancellation tiers (PlatformAdmin). The controller's
// [Authorize(Roles="PlatformAdmin")] is the gate; the command is platform-scoped
// (not ITenantScoped). Audited via IAuditable → settings.platform.set-tiers.

public sealed record SetGlobalTiersCommand(
    int FirstTierDays,
    int SecondTierDays,
    int MiddleTierRefundPct,
    int FinalCutoffHours,
    int UpgradePricePct)
    : IRequest<GlobalCancellationTiersDto>, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("platform", "set-tiers");
    public string? AuditTargetType => "CancellationTiers";
    public string? AuditTargetId => CancellationTiers.SingletonId.ToString();
}

internal sealed class SetGlobalTiersValidator : AbstractValidator<SetGlobalTiersCommand>
{
    public SetGlobalTiersValidator()
    {
        RuleFor(x => x.FirstTierDays).GreaterThan(x => x.SecondTierDays)
            .WithMessage("FirstTierDays must be greater than SecondTierDays.");
        RuleFor(x => x.SecondTierDays).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MiddleTierRefundPct).InclusiveBetween(0, 100);
        RuleFor(x => x.FinalCutoffHours).GreaterThan(0);
        RuleFor(x => x.UpgradePricePct).InclusiveBetween(0, 100);
    }
}

internal sealed class SetGlobalTiersHandler(
    AdminDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, IUserEmailLookup users)
    : IRequestHandler<SetGlobalTiersCommand, GlobalCancellationTiersDto>
{
    public async Task<GlobalCancellationTiersDto> Handle(SetGlobalTiersCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        var now = clock.UtcNow;

        var row = await db.CancellationTiers.FirstOrDefaultAsync(x => x.Id == CancellationTiers.SingletonId, cancellationToken);
        if (row is null)
        {
            row = CancellationTiers.Seed(
                request.FirstTierDays, request.SecondTierDays, request.MiddleTierRefundPct,
                request.FinalCutoffHours, request.UpgradePricePct, actor, now);
            db.CancellationTiers.Add(row);
        }
        else
        {
            row.Update(
                request.FirstTierDays, request.SecondTierDays, request.MiddleTierRefundPct,
                request.FinalCutoffHours, request.UpgradePricePct, actor, now);
        }
        await db.SaveChangesAsync(cancellationToken);

        return await SettingsDisplay.MapTiersAsync(row, users, cancellationToken);
    }
}

public sealed record GetGlobalTiersQuery : IRequest<GlobalCancellationTiersDto>;

internal sealed class GetGlobalTiersHandler(AdminDbContext db, IUserEmailLookup users)
    : IRequestHandler<GetGlobalTiersQuery, GlobalCancellationTiersDto>
{
    public async Task<GlobalCancellationTiersDto> Handle(GetGlobalTiersQuery request, CancellationToken cancellationToken)
    {
        var row = await db.CancellationTiers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == CancellationTiers.SingletonId, cancellationToken);
        return row is null
            ? new GlobalCancellationTiersDto(7, 2, 50, 48, 8, 0, null, null)
            : await SettingsDisplay.MapTiersAsync(row, users, cancellationToken);
    }
}
