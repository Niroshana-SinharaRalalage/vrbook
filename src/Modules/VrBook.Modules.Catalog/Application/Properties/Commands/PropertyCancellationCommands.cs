using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

// VRB-215 — per-property cancellation-model selection (tenant-admin). The host picks
// the model; tier schedule + upgrade % are platform-set (VRB-216) and echoed read-only.
// TenantId is stamped by the controller from ICurrentUser.TenantId (TenantAuthorizationBehavior
// validates it); audited via IAuditable → settings.cancellation.set-model.

public sealed record SetPropertyCancellationModelCommand(Guid TenantId, Guid PropertyId, CancellationModel Model)
    : IRequest<PropertyCancellationSettingsDto>, ITenantScoped, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("cancellation", "set-model");
    public string? AuditTargetType => "Property";
    public string? AuditTargetId => PropertyId.ToString();
}

internal sealed class SetPropertyCancellationModelValidator : AbstractValidator<SetPropertyCancellationModelCommand>
{
    public SetPropertyCancellationModelValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.Model).IsInEnum();
    }
}

internal sealed class SetPropertyCancellationModelHandler(CatalogDbContext db, ICancellationTierProvider tiers)
    : IRequestHandler<SetPropertyCancellationModelCommand, PropertyCancellationSettingsDto>
{
    public async Task<PropertyCancellationSettingsDto> Handle(
        SetPropertyCancellationModelCommand request, CancellationToken cancellationToken)
    {
        var property = await db.Properties
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId && p.TenantId == request.TenantId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        property.SetCancellationModel(request.Model);
        await db.SaveChangesAsync(cancellationToken);

        var active = await tiers.GetActiveAsync(cancellationToken);
        return PropertyCancellationDisplay.Map(request.PropertyId, request.Model, active);
    }
}

public sealed record GetPropertyCancellationSettingsQuery(Guid TenantId, Guid PropertyId)
    : IRequest<PropertyCancellationSettingsDto>, ITenantScoped;

internal sealed class GetPropertyCancellationSettingsHandler(CatalogDbContext db, ICancellationTierProvider tiers)
    : IRequestHandler<GetPropertyCancellationSettingsQuery, PropertyCancellationSettingsDto>
{
    public async Task<PropertyCancellationSettingsDto> Handle(
        GetPropertyCancellationSettingsQuery request, CancellationToken cancellationToken)
    {
        var model = await db.Properties.AsNoTracking()
            .Where(p => p.Id == request.PropertyId && p.TenantId == request.TenantId)
            .Select(p => p.CancellationModel)
            .FirstOrDefaultAsync(cancellationToken);

        var active = await tiers.GetActiveAsync(cancellationToken);
        return PropertyCancellationDisplay.Map(request.PropertyId, model ?? CancellationModel.Tiered, active);
    }
}

internal static class PropertyCancellationDisplay
{
    public static PropertyCancellationSettingsDto Map(Guid propertyId, CancellationModel model, GlobalCancellationTiers t) =>
        new(
            propertyId,
            model,
            new GlobalCancellationTiersDto(
                t.FirstTierDays, t.SecondTierDays, t.MiddleTierRefundPct, t.FinalCutoffHours,
                t.UpgradePricePct, t.Version, LastChangedBy: null, LastChangedAt: null),
            LastChangedBy: null,
            LastChangedAt: null);
}
