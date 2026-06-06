using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Domain;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

internal sealed class UpdatePricingPlanHandler(
    ICurrentUser currentUser,
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<UpdatePricingPlanCommand, PricingPlanDto>
{
    public async Task<PricingPlanDto> Handle(UpdatePricingPlanCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }
        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));

        var existing = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken);

        if (existing is null)
        {
            // First time set - create a new plan.
            var plan = PricingPlan.Create(request.PropertyId, r.BaseNightlyRate, r.Currency);
            plan.Replace(
                r.BaseNightlyRate,
                r.WeekendRate,
                r.Currency,
                r.MinStayNights,
                r.MaxStayNights,
                r.DynamicEnabled,
                (r.Fees ?? Array.Empty<FeeDto>()).Select(f => (f.Kind, f.Amount, f.Basis, f.FreeThreshold, f.Label)));
            await plans.AddAsync(plan, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return plan.ToDto();
        }

        // Update existing plan via raw SQL to avoid the EF Core 8 tracking
        // weirdness we hit in Catalog. Delete + re-add fee rows.
        var planId = existing.Id;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE pricing.pricing_plans
SET base_nightly_rate = {r.BaseNightlyRate},
    weekend_rate = {r.WeekendRate},
    currency = {r.Currency.ToUpperInvariant()},
    min_stay_nights = {r.MinStayNights},
    max_stay_nights = {r.MaxStayNights},
    dynamic_enabled = {r.DynamicEnabled},
    updated_at = {DateTimeOffset.UtcNow},
    updated_by = {(Guid?)currentUser.UserId.Value}
WHERE ""Id"" = {planId}", cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM pricing.fees WHERE pricing_plan_id = {planId}", cancellationToken);

        foreach (var f in r.Fees ?? Array.Empty<FeeDto>())
        {
            var fid = Guid.NewGuid();
            var kind = f.Kind.ToString();
            var basis = f.Basis.ToString();
            var label = f.Label?.Trim() ?? string.Empty;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO pricing.fees (""Id"", pricing_plan_id, kind, amount, basis, free_threshold, label)
VALUES ({fid}, {planId}, {kind}, {f.Amount}, {basis}, {f.FreeThreshold}, {label})", cancellationToken);
        }

        var fresh = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new InvalidOperationException("Plan disappeared after update.");
        return fresh.ToDto();
    }
}
