using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Users.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Queries;

internal sealed class SearchUsersHandler(IUserRepository users, ICurrentUser currentUser)
    : IRequestHandler<SearchUsersQuery, OffsetPagedResult<UserDto>>
{
    public async Task<OffsetPagedResult<UserDto>> Handle(SearchUsersQuery q, CancellationToken cancellationToken)
    {
        // OPS.M.10.2 C1 (#1 Critical) — close the cross-tenant user enumeration leak.
        // Pre-fix: `users.SearchAsync(q)` returned matching users across EVERY
        // tenant. OwnerA could query "owner-b@" and read OwnerB's PII.
        //
        // Post-fix:
        // - PlatformAdmin → platform-wide search (tenantId = null).
        // - Owner / Admin → search scoped to caller's primary tenant (via
        //   tenant_memberships join in the repository).
        // - Anonymous / non-tenant caller → ForbiddenException.
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.Size, 1, 100);

        Guid? scope;
        if (currentUser.IsPlatformAdmin)
        {
            scope = null;
        }
        else
        {
            scope = currentUser.TenantId
                ?? throw new ForbiddenException(
                    "User search requires a tenant membership or PlatformAdmin role.");
        }

        var total = await users.CountAsync(q.Q, scope, cancellationToken);
        var rows = await users.SearchAsync(q.Q, scope, (page - 1) * size, size, cancellationToken);
        return new OffsetPagedResult<UserDto>(rows.Select(u => u.ToDto()).ToList(), page, size, total);
    }
}
