using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Application.Common;

/// <summary>
/// Shared cross-property authorization guard for the rule-mutation handlers
/// (see docs/SLICE6_PLAN.md §2.12). tenant_admin role bypasses the ownership
/// check; every other authenticated user must own the property they're mutating.
///
/// <para>Slice OPS.M.15.5 — the legacy <c>currentUser.IsAdmin</c> reader
/// (App Roles path) is retired; the bypass is now scoped to the caller's
/// active tenant via <c>HasTenantRole</c> so a tenant_admin in tenant B
/// cannot mutate pricing in tenant A they don't administer.</para>
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
        if (currentUser.TenantId is { } callerTid
            && currentUser.HasTenantRole(callerTid, "tenant_admin"))
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
