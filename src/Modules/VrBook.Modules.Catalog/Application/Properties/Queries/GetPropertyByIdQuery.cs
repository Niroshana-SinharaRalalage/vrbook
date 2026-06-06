using MediatR;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

/// <summary>
/// Cross-module read shim. Returns the minimum fields other modules need
/// (title + ownerUserId) without leaking the Catalog DbContext to them.
/// </summary>
public sealed record GetPropertyByIdQuery(Guid Id) : IRequest<PropertyBasicInfo?>;

public sealed record PropertyBasicInfo(Guid Id, string Slug, string Title, Guid OwnerUserId, bool IsActive);
