using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Commands;

// OPS.M.5 §3.3 (D3) — three commands cover the onboarding flow:
//   Onboard → Stripe Account.create + Tenant.AssignStripeAccount (idempotent on tenantId).
//   GenerateAccountLink → fresh 5-min Stripe-hosted onboarding URL.
//   OpenLoginLink → magic-link to the Stripe Express dashboard.
// Plus SetTenantPlatformFeeBpsCommand for Super Admin per OPS_M_5_PLAN §3.16,
// dormant until Slice OPS.M.8 lights up IsPlatformAdmin.

public sealed record OnboardTenantStripeCommand(Guid TenantId, string Country)
    : IRequest<OnboardTenantStripeResult>, ITenantScoped;
public sealed record OnboardTenantStripeResult(string StripeAccountId);

public sealed record GenerateStripeAccountLinkCommand(Guid TenantId)
    : IRequest<GenerateStripeAccountLinkResult>, ITenantScoped;
public sealed record GenerateStripeAccountLinkResult(string Url, DateTimeOffset ExpiresAt);

public sealed record OpenStripeLoginLinkCommand(Guid TenantId)
    : IRequest<OpenStripeLoginLinkResult>, ITenantScoped;
public sealed record OpenStripeLoginLinkResult(string Url);

public sealed record SetTenantPlatformFeeBpsCommand(Guid TenantId, int Bps)
    : IRequest<Unit>, ITenantScoped;

internal sealed class OnboardTenantStripeHandler(
    IdentityDbContext db, IStripeConnectGateway gateway)
    : IRequestHandler<OnboardTenantStripeCommand, OnboardTenantStripeResult>
{
    public async Task<OnboardTenantStripeResult> Handle(
        OnboardTenantStripeCommand cmd, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);
        if (tenant.StripeAccountId is not null)
        {
            // Idempotent path — re-call with same tenantId returns the existing id.
            return new OnboardTenantStripeResult(tenant.StripeAccountId);
        }
        var accountId = await gateway.CreateConnectAccountAsync(
            tenant.Id, tenant.SupportEmail.Value, cmd.Country, cancellationToken);
        tenant.SetStripeAccount(accountId);
        await db.SaveChangesAsync(cancellationToken);
        return new OnboardTenantStripeResult(accountId);
    }
}

internal sealed class GenerateStripeAccountLinkHandler(
    IdentityDbContext db, IStripeConnectGateway gateway)
    : IRequestHandler<GenerateStripeAccountLinkCommand, GenerateStripeAccountLinkResult>
{
    public async Task<GenerateStripeAccountLinkResult> Handle(
        GenerateStripeAccountLinkCommand cmd, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);
        if (tenant.StripeAccountId is null)
        {
            throw new BusinessRuleViolationException(
                "tenant.stripe_not_onboarded",
                "Run Onboard first to create the Connect account.");
        }
        // OPS.M.5 §3.12 (D12) — gateway reads return/refresh URLs from StripeOptions.
        var link = await gateway.CreateAccountLinkAsync(tenant.StripeAccountId, cancellationToken);
        return new GenerateStripeAccountLinkResult(link.Url, link.ExpiresAt);
    }
}

internal sealed class OpenStripeLoginLinkHandler(
    IdentityDbContext db, IStripeConnectGateway gateway)
    : IRequestHandler<OpenStripeLoginLinkCommand, OpenStripeLoginLinkResult>
{
    public async Task<OpenStripeLoginLinkResult> Handle(
        OpenStripeLoginLinkCommand cmd, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);
        if (tenant.StripeAccountId is null)
        {
            throw new BusinessRuleViolationException(
                "tenant.stripe_not_onboarded",
                "Onboarding must complete before opening the Stripe dashboard.");
        }
        var url = await gateway.CreateLoginLinkAsync(tenant.StripeAccountId, cancellationToken);
        return new OpenStripeLoginLinkResult(url);
    }
}

internal sealed class SetTenantPlatformFeeBpsHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IRequestHandler<SetTenantPlatformFeeBpsCommand, Unit>
{
    public async Task<Unit> Handle(SetTenantPlatformFeeBpsCommand cmd, CancellationToken cancellationToken)
    {
        // OPS.M.8 §3.5 (D5) — lit-up. Only platform-admins can set the fee.
        // The TenantAuthorizationBehavior's PlatformAdmin bypass already allows
        // a cross-tenant write here; this re-check is defense-in-depth so the
        // command can't be dispatched from inside another handler under a
        // non-admin caller.
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException(
                "SetTenantPlatformFeeBpsCommand requires platform-admin privileges.");
        }
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);
        tenant.SetPlatformFeeBps(cmd.Bps);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// OPS.M.5 §3.12 (D12) — the StripeGateway reads OnboardingReturnUrl /
// OnboardingRefreshUrl directly from StripeOptions, so Identity doesn't
// need a cross-module accessor. Avoid re-introducing IStripeOnboardingUrls.
