using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Domain;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin.Application.Settings;

// VRB-216 — platform tax posture (PlatformAdmin): facilitator flag + per-state roster.
// Posture only (engine is PAY VRB-103). Audited → settings.tax.set-posture.

public sealed record SetTaxPostureCommand(bool FacilitatorActive, IReadOnlyDictionary<string, bool> PerStateEnabled)
    : IRequest<TaxPostureDto>, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("tax", "set-posture");
    public string? AuditTargetType => "TaxPosture";
    public string? AuditTargetId => TaxPostureRow.SingletonId.ToString();
}

internal sealed class SetTaxPostureValidator : AbstractValidator<SetTaxPostureCommand>
{
    // US state codes are 2 uppercase letters; reject malformed roster keys so the
    // stored JSON stays queryable.
    public SetTaxPostureValidator()
    {
        RuleFor(x => x.PerStateEnabled)
            .Must(r => r is null || r.Keys.All(k => k.Length == 2 && k.All(char.IsAsciiLetterUpper)))
            .WithMessage("PerStateEnabled keys must be 2-letter uppercase US state codes (e.g. 'CA').");
    }
}

internal sealed class SetTaxPostureHandler(AdminDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<SetTaxPostureCommand, TaxPostureDto>
{
    public async Task<TaxPostureDto> Handle(SetTaxPostureCommand request, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        var json = JsonSerializer.Serialize(
            request.PerStateEnabled ?? new Dictionary<string, bool>());

        var row = await db.TaxPosture.FirstOrDefaultAsync(x => x.Id == TaxPostureRow.SingletonId, cancellationToken);
        if (row is null)
        {
            db.TaxPosture.Add(TaxPostureRow.Seed(request.FacilitatorActive, json, actor, clock.UtcNow));
        }
        else
        {
            row.Set(request.FacilitatorActive, json, actor, clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);

        return new TaxPostureDto(
            request.FacilitatorActive,
            request.PerStateEnabled ?? new Dictionary<string, bool>());
    }
}

public sealed record GetTaxPostureQuery : IRequest<TaxPostureDto>;

internal sealed class GetTaxPostureHandler(AdminDbContext db) : IRequestHandler<GetTaxPostureQuery, TaxPostureDto>
{
    public async Task<TaxPostureDto> Handle(GetTaxPostureQuery request, CancellationToken cancellationToken)
    {
        var row = await db.TaxPosture.AsNoTracking().FirstOrDefaultAsync(x => x.Id == TaxPostureRow.SingletonId, cancellationToken);
        if (row is null)
        {
            return new TaxPostureDto(true, new Dictionary<string, bool>());
        }
        var roster = string.IsNullOrWhiteSpace(row.PerStateJson)
            ? new Dictionary<string, bool>()
            : JsonSerializer.Deserialize<Dictionary<string, bool>>(row.PerStateJson) ?? new Dictionary<string, bool>();
        return new TaxPostureDto(row.FacilitatorActive, roster);
    }
}
