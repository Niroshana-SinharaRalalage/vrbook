using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Queries;

/// <summary>
/// OPS.M.8 §4.3 — paged list of every tenant. PlatformAdmin only; the
/// handler's defense-in-depth check throws if the caller isn't admin.
/// </summary>
public sealed record ListPlatformTenantsQuery(
    int Page,
    int PageSize,
    string? StatusFilter,
    string? SearchTerm) : IRequest<PlatformTenantListResponse>;

internal sealed class ListPlatformTenantsHandler(
    IdentityDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListPlatformTenantsQuery, PlatformTenantListResponse>
{
    public async Task<PlatformTenantListResponse> Handle(
        ListPlatformTenantsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException("Platform-admin role required.");
        }

        var page = Math.Clamp(query.Page, 1, 1000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = db.Tenants.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.StatusFilter))
        {
            q = q.Where(t => t.Status == query.StatusFilter);
        }
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var s = query.SearchTerm.Trim().ToLowerInvariant();
            q = q.Where(t =>
                EF.Functions.ILike(t.Slug, $"{s}%") ||
                EF.Functions.ILike(t.DisplayName, $"{s}%"));
        }

        var total = await q.CountAsync(cancellationToken);
        var rows = await q
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new PlatformTenantListItemDto(
                t.Id, t.Slug, t.DisplayName, t.Status,
                t.StripeAccountId != null,
                t.ChargesEnabled, t.PayoutsEnabled,
                t.DefaultCurrency, t.PlatformFeeBps,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PlatformTenantListResponse(rows, total, page, pageSize);
    }
}

/// <summary>
/// OPS.M.8 §4.3 — single-tenant detail view. PlatformAdmin only.
/// </summary>
public sealed record GetPlatformTenantQuery(Guid TenantId) : IRequest<PlatformTenantDto>;

internal sealed class GetPlatformTenantHandler(
    IdentityDbContext db,
    ICurrentUser currentUser,
    IPlatformTenantStatsLookup stats)
    : IRequestHandler<GetPlatformTenantQuery, PlatformTenantDto>
{
    public async Task<PlatformTenantDto> Handle(
        GetPlatformTenantQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException("Platform-admin role required.");
        }

        var t = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", query.TenantId);

        var s = await stats.GetAsync(t.Id, cancellationToken);

        return new PlatformTenantDto(
            Id: t.Id,
            Slug: t.Slug,
            DisplayName: t.DisplayName,
            Status: t.Status,
            SuspendedReason: t.SuspendedReason,
            DefaultCurrency: t.DefaultCurrency,
            PlatformFeeBps: t.PlatformFeeBps,
            StripeAccountStatus: t.StripeAccountStatus,
            ChargesEnabled: t.ChargesEnabled,
            PayoutsEnabled: t.PayoutsEnabled,
            HasStripeAccount: t.StripeAccountId is not null,
            PropertyCount: s.PropertyCount,
            ActiveBookingCount: s.ActiveBookingCount,
            TotalBookingCount: s.TotalBookingCount,
            LifetimeGrossRevenue: s.LifetimeGrossRevenue,
            CreatedAt: t.CreatedAt,
            UpdatedAt: t.UpdatedAt);
    }
}
