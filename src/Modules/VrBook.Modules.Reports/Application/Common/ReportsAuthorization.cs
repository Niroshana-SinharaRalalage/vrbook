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

        if (currentUser.IsAdmin)
        {
            // Admin: no scope filter unless explicitly asked.
            if (requestedPropertyId is { } pid)
            {
                return new[] { pid };
            }
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
            return new[] { requested };
        }

        var owned = await ownerLookup.ListPropertyIdsOwnedByAsync(ownerId, ct);
        return owned;
    }
}
