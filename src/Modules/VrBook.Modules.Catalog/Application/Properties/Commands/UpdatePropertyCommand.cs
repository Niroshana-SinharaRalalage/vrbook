using MediatR;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

// OPS.M.4 — owner edits one of their tenant's properties. Controller stamps
// TenantId from currentUser.TenantId; behavior rejects cross-tenant attempts.
// VRB-212 — audited (settings.property.update) so property-settings edits surface in
// the "Recent changes" panel; reused (not duplicated) by the settings surface.
public sealed record UpdatePropertyCommand(Guid Id, UpdatePropertyRequest Request, Guid TenantId)
    : IRequest<PropertyDto>, ITenantScoped, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("property", "update");
    public string? AuditTargetType => "Property";
    public string? AuditTargetId => Id.ToString();
}
