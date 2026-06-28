using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Reports.Application.Common;

/// <summary>
/// Resolves the property-id set a report should scope to (see SLICE7_PLAN §2.4).
/// Admins see everything; owners see their own. Cross-property probes -> 403.
/// Mirrors the PricingAuthorization helper from Slice 6.
/// </summary>
internal static class ReportsAuthorization
{
    /// <summary>
    /// Returns the property ids that the report should aggregate over.
    /// <list type="bullet">
    ///   <item>Admin role: when <paramref name="requestedPropertyId"/> is set, scope to that one; else <c>null</c> = no filter.</item>
    ///   <item>Owner with <paramref name="requestedPropertyId"/>: verify ownership; throw <see cref="ForbiddenException"/> if not theirs.</item>
    ///   <item>Owner without filter: return the full set of property ids they own.</item>
    /// </list>
    /// </summary>
    public static async Task<IReadOnlyList<Guid>?> ResolvePropertyScopeAsync(
        ICurrentUser currentUser,
        IPropertyOwnerLookup ownerLookup,
        Guid? requestedPropertyId,
        CancellationToken ct)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        // OPS.M.10.2 C3 (#13 High) — verify the requested property belongs
        // to the caller's tenant on BOTH the Admin and the Owner paths.
        // Previously: the Admin-with-explicit-propertyId branch trusted the
        // input GUID without any check, returning the report scoped to a
        // cross-tenant propertyId (RLS filters bookings to empty but the
        // report shape implies the property exists). The Owner branch
        // checked OwnerUserId but NOT TenantId, so an Owner with
        // multi-tenant membership could probe a property they own in
        // another tenant.
        if (currentUser.IsAdmin)
        {
            if (requestedPropertyId is { } pid)
            {
                var snapshot = await ownerLookup.GetAsync(pid, ct)
                    ?? throw new NotFoundException("Property", pid);
                if (currentUser.TenantId is null || snapshot.TenantId != currentUser.TenantId.Value)
                {
                    throw new ForbiddenException(
                        "Admins may only run reports against properties in their own tenant.");
                }
                return new[] { pid };
            }
            // Admin without an explicit property id: M.9 RLS scopes the
            // downstream report query to the caller's tenant.
            return null;
        }

        var ownerId = currentUser.UserId.Value;

        if (requestedPropertyId is { } requested)
        {
            var snapshot = await ownerLookup.GetAsync(requested, ct)
                ?? throw new NotFoundException("Property", requested);
            if (snapshot.OwnerUserId != ownerId)
            {
                throw new ForbiddenException("You are not the owner of this property.");
            }
            if (currentUser.TenantId is null || snapshot.TenantId != currentUser.TenantId.Value)
            {
                throw new ForbiddenException(
                    "This property belongs to a different tenant than your current membership.");
            }
            return new[] { requested };
        }

        // OPS.M.10.2 C3 — the listing must be tenant-scoped at the source.
        // IPropertyOwnerLookup.ListPropertyIdsOwnedByAsync today does not
        // accept a tenant filter; the M.9 RLS on catalog.properties scopes
        // the lookup to the caller's tenant via the GUC, so we rely on
        // that. Defense-in-depth flagged in OPS.M.10.2 audit #13 fix note (c).
        var owned = await ownerLookup.ListPropertyIdsOwnedByAsync(ownerId, ct);
        return owned;
    }
}
