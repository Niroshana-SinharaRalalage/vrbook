using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Tenants.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Queries;

/// <summary>
/// OPS.M.7 §3.2 + §4.1 — read-side projection of the caller's own tenant.
/// Not <c>ITenantScoped</c>: derives the tenant id from
/// <see cref="ICurrentUser.TenantId"/>, not the request. The controller's
/// <c>[Authorize(Roles="Owner,Admin")]</c> is the auth gate; a stray
/// <c>ITenantScoped</c> would invert that surface.
/// </summary>
public sealed record GetMyTenantQuery : IRequest<MeTenantDto>;

internal sealed class GetMyTenantHandler(
    ICurrentUser currentUser,
    IdentityDbContext db,
    IPropertyCountByTenant propertyCount)
    : IRequestHandler<GetMyTenantQuery, MeTenantDto>
{
    public async Task<MeTenantDto> Handle(GetMyTenantQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is null)
        {
            throw new ForbiddenException("Caller has no tenant membership.");
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == currentUser.TenantId.Value, cancellationToken)
            ?? throw new NotFoundException("Tenant", currentUser.TenantId.Value);

        var count = await propertyCount.GetCountAsync(tenant.Id, cancellationToken);

        var seed = new MeTenantDto(
            Id: tenant.Id,
            Slug: tenant.Slug,
            DisplayName: tenant.DisplayName,
            Status: tenant.Status,
            DefaultCurrency: tenant.DefaultCurrency,
            PlatformFeeBps: tenant.PlatformFeeBps,
            StripeAccountStatus: tenant.StripeAccountStatus,
            ChargesEnabled: tenant.ChargesEnabled,
            PayoutsEnabled: tenant.PayoutsEnabled,
            HasStripeAccount: tenant.StripeAccountId is not null,
            PropertyCount: count,
            Onboarding: new OnboardingProgressDto(false, OnboardingProgress.StepWelcome));

        return seed with
        {
            Onboarding = new OnboardingProgressDto(
                IsComplete: OnboardingProgress.DeriveIsComplete(seed),
                NextStep: OnboardingProgress.DeriveNextStep(seed)),
        };
    }
}
