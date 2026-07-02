using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Queries;

/// <summary>
/// Slice OPS.M.13.5 — list every tenant the caller has active membership in.
/// The tenant-picker SPA calls this after MSAL sign-in completes and routes:
/// 0 memberships → landing (or platform dashboard if PlatformAdmin);
/// 1 membership → auto-pick + return to deep-link;
/// N memberships → /select-tenant page.
///
/// <para>NOT <c>ITenantScoped</c>: this is the query that DECIDES which tenant
/// becomes active; it must run before any tenant scope exists. The
/// caller is authenticated (JWT bearer → <c>currentUser.UserId</c>) but has no
/// <c>X-Active-Tenant</c> header at this point.</para>
/// </summary>
public sealed record GetMyTenantsQuery : IRequest<MyTenantsResponse>;

internal sealed class GetMyTenantsHandler(
    ICurrentUser currentUser,
    IdentityDbContext db)
    : IRequestHandler<GetMyTenantsQuery, MyTenantsResponse>
{
    public async Task<MyTenantsResponse> Handle(GetMyTenantsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == Guid.Empty)
        {
            throw new ForbiddenException("Caller is not authenticated.");
        }

        var memberships = await (
            from m in db.TenantMemberships
            join t in db.Tenants on m.TenantId equals t.Id
            where m.UserId == currentUser.UserId
                && m.DeletedAt == null
                && t.DeletedAt == null
            orderby m.IsPrimary descending, t.DisplayName
            select new MyTenantMembershipDto(
                t.Id,
                t.Slug,
                t.DisplayName,
                t.Status,
                m.Role,
                m.IsPrimary))
            .ToListAsync(cancellationToken);

        return new MyTenantsResponse(memberships, currentUser.IsPlatformAdmin);
    }
}
