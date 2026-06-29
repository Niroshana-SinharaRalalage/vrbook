using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Behaviors;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Tenants.Commands;

/// <summary>
/// Slice OPS.M.10.2 F11.4 — manually re-pull the tenant's Stripe Connect
/// readiness flags. Used when the <c>account.updated</c> webhook is
/// delayed or dropped (staging webhook flake; production retries
/// usually catch up within minutes but the operator may want to flip
/// the tenant out of <c>PendingOnboarding</c> NOW).
///
/// <para><b>Auth posture</b>: <c>ITenantScoped</c> — M.4 behavior gates
/// caller vs route tenantId. Controller is
/// <c>[Authorize(Roles="Owner,Admin")]</c>.</para>
///
/// <para>Throws <c>BusinessRuleViolationException("tenant.stripe.no_account")</c>
/// if the tenant has no <c>StripeAccountId</c> yet (the operator must
/// run <c>/stripe/onboard</c> first).</para>
/// </summary>
public sealed record RefreshStripeReadinessCommand(Guid TenantId)
    : IRequest<RefreshStripeReadinessResult>, ITenantScoped, IAuditable
{
    public string AuditAction => "tenant.stripe.refresh-readiness";
    public string? AuditTargetType => "Tenant";
    public string? AuditTargetId => TenantId.ToString();
}

public sealed record RefreshStripeReadinessResult(
    string StripeAccountId,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    string Status);

internal sealed class RefreshStripeReadinessHandler(
    IdentityDbContext db,
    IStripeConnectGateway stripe,
    IConnectAccountReadinessUpdater readiness)
    : IRequestHandler<RefreshStripeReadinessCommand, RefreshStripeReadinessResult>
{
    public async Task<RefreshStripeReadinessResult> Handle(
        RefreshStripeReadinessCommand cmd, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);
        if (string.IsNullOrEmpty(tenant.StripeAccountId))
        {
            throw new BusinessRuleViolationException(
                "tenant.stripe.no_account",
                "Tenant has no Stripe Connect account. Call POST /stripe/onboard first.");
        }

        var readinessSnapshot = await stripe.GetAccountReadinessAsync(tenant.StripeAccountId, cancellationToken);

        // Reuse the IConnectAccountReadinessUpdater contract so the
        // domain state machine runs exactly the same as it does on the
        // webhook path (PendingOnboarding → Active when both flags flip).
        await readiness.UpdateAsync(
            tenant.StripeAccountId,
            readinessSnapshot.ChargesEnabled,
            readinessSnapshot.PayoutsEnabled,
            cancellationToken);

        // Re-load to surface the post-update status; the updater persists
        // via its own SaveChanges so the cached tenant entity may be stale.
        var refreshed = await db.Tenants
            .AsNoTracking()
            .FirstAsync(t => t.Id == cmd.TenantId, cancellationToken);
        return new RefreshStripeReadinessResult(
            refreshed.StripeAccountId!,
            refreshed.ChargesEnabled,
            refreshed.PayoutsEnabled,
            refreshed.Status);
    }
}
