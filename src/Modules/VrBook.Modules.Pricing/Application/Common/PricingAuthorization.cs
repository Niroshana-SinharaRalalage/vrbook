using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Application.Common;

/// <summary>
/// Shared cross-property authorization guard for the rule-mutation handlers
/// (see docs/SLICE6_PLAN.md §2.12). Admin role bypasses the ownership check;
/// every other authenticated user must own the property they're mutating.
/// </summary>
internal static class PricingAuthorization
{
    public static async Task RequireOwnerOrAdminAsync(
        ICurrentUser currentUser,
        IPropertyOwnerLookup ownerLookup,
        Guid propertyId,
        CancellationToken ct)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }
        if (currentUser.IsAdmin)
        {
            return;
        }
        var snapshot = await ownerLookup.GetAsync(propertyId, ct)
            ?? throw new NotFoundException("Property", propertyId);
        if (snapshot.OwnerUserId != currentUser.UserId.Value)
        {
            throw new ForbiddenException("You are not the owner of this property.");
        }
    }
}
