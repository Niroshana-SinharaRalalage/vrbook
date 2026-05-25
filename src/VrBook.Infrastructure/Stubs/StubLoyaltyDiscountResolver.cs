using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Stubs;

/// <summary>
/// A0 stub. Returns Bronze tier and 0% discount. Replaced by the real implementation
/// in A8 (Reviews + Loyalty). See proposal §11.3.
/// </summary>
public sealed class StubLoyaltyDiscountResolver : ILoyaltyDiscountResolver
{
    public Task<LoyaltyDiscount> ResolveAsync(Guid? userId, CancellationToken ct = default) =>
        Task.FromResult(new LoyaltyDiscount(LoyaltyTier.Bronze, 0m, IsEnabled: false));
}
