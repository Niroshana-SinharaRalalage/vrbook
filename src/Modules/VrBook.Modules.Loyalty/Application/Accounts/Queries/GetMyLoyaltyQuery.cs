using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Loyalty.Domain;
using VrBook.Modules.Loyalty.Infrastructure.Persistence;

namespace VrBook.Modules.Loyalty.Application.Accounts.Queries;

public sealed record GetMyLoyaltyQuery : IRequest<LoyaltyAccountDto>;

internal sealed class GetMyLoyaltyHandler(LoyaltyDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyLoyaltyQuery, LoyaltyAccountDto>
{
    public async Task<LoyaltyAccountDto> Handle(GetMyLoyaltyQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign in to view loyalty status.");
        }
        var me = currentUser.UserId.Value;
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == me, cancellationToken);
        var tier = account?.Tier ?? LoyaltyTier.Bronze;
        var stays = account?.CompletedStayCount ?? 0;
        var (nextTier, staysToNext) = TierDefinition.NextTier(stays);
        return new LoyaltyAccountDto(
            UserId: me,
            Tier: tier,
            CompletedStayCount: stays,
            CurrentDiscountPct: TierDefinition.DiscountFor(tier),
            NextTier: nextTier,
            StaysUntilNextTier: staysToNext);
    }
}
