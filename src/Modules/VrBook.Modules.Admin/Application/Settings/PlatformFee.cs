using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Domain;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin.Application.Settings;

// VRB-216 — per-tenant platform-fee override (PlatformAdmin). The platform default
// (Payment:PlatformFeeBps, 1500) applies when no override exists. Audited →
// settings.platform.set-fee.

public sealed record SetPlatformFeeCommand(Guid TenantId, int PlatformFeeBps)
    : IRequest<PlatformFeeConfigDto>, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("platform", "set-fee");
    public string? AuditTargetType => "PlatformFeeOverride";
    public string? AuditTargetId => TenantId.ToString();
}

internal sealed class SetPlatformFeeValidator : AbstractValidator<SetPlatformFeeCommand>
{
    public SetPlatformFeeValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PlatformFeeBps).InclusiveBetween(0, 10000)
            .WithMessage("PlatformFeeBps must be between 0 and 10000 (0–100%).");
    }
}

internal sealed class SetPlatformFeeHandler(
    AdminDbContext db, ICurrentUser currentUser, IDateTimeProvider clock, IConfiguration configuration)
    : IRequestHandler<SetPlatformFeeCommand, PlatformFeeConfigDto>
{
    public async Task<PlatformFeeConfigDto> Handle(SetPlatformFeeCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        var row = await db.PlatformFeeOverrides.FirstOrDefaultAsync(x => x.TenantId == request.TenantId, cancellationToken);
        if (row is null)
        {
            db.PlatformFeeOverrides.Add(
                PlatformFeeOverride.Create(request.TenantId, request.PlatformFeeBps, actor, clock.UtcNow));
        }
        else
        {
            row.Set(request.PlatformFeeBps, actor, clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);

        return await LoadAsync(db, configuration, cancellationToken);
    }

    internal static async Task<PlatformFeeConfigDto> LoadAsync(
        AdminDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var overrides = await db.PlatformFeeOverrides.AsNoTracking()
            .OrderBy(x => x.TenantId)
            .Select(x => new TenantFeeOverrideDto(x.TenantId, x.PlatformFeeBps))
            .ToListAsync(cancellationToken);
        return new PlatformFeeConfigDto(configuration.GetValue("Payment:PlatformFeeBps", 1500), overrides);
    }
}

public sealed record GetPlatformFeeConfigQuery : IRequest<PlatformFeeConfigDto>;

internal sealed class GetPlatformFeeConfigHandler(AdminDbContext db, IConfiguration configuration)
    : IRequestHandler<GetPlatformFeeConfigQuery, PlatformFeeConfigDto>
{
    public Task<PlatformFeeConfigDto> Handle(GetPlatformFeeConfigQuery request, CancellationToken cancellationToken) =>
        SetPlatformFeeHandler.LoadAsync(db, configuration, cancellationToken);
}
