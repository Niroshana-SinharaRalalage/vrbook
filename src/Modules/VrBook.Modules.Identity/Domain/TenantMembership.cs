using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Joins a <see cref="User"/> to a <see cref="Tenant"/> with a per-tenant role. Per
/// `docs/identity/roles-architecture.md` §3.2 — the table that lets the API answer
/// "which tenants is this user in, and with what role." The middleware enrichment
/// in OPS.M.2 will read these via <c>db.Set&lt;TenantMembership&gt;().Where(...)</c>
/// on every authenticated request.
///
/// Role values: <see cref="RoleTenantAdmin"/> (per `MULTI_TENANCY_OPS_PLAN.md` §1 —
/// the "Property Owner" persona) or <see cref="RoleTenantMember"/> (deferred UI;
/// schema supports it per `docs/OPS_M_1_PLAN.md` §3.2).
///
/// <see cref="IsPrimary"/> answers "which tenant is this user currently acting as"
/// for users with multiple memberships. App-level enforcement only in OPS.M.1; see
/// `OPS_M_1_PLAN.md` §3.2 for the partial-index deferral.
/// </summary>
public sealed class TenantMembership : AggregateRoot
{
    public const string RoleTenantAdmin = "tenant_admin";
    public const string RoleTenantMember = "tenant_member";

    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal)
    {
        RoleTenantAdmin, RoleTenantMember,
    };

    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string Role { get; private set; } = default!;
    public bool IsPrimary { get; private set; }

    private TenantMembership() { }   // EF Core

    public static TenantMembership Create(Guid userId, Guid tenantId, string role, bool isPrimary = false)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("userId is required.", nameof(userId));
        }
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("tenantId is required.", nameof(tenantId));
        }
        EnsureRoleAllowed(role);

        var membership = new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            IsPrimary = isPrimary,
        };
        membership.Raise(new TenantMembershipCreated(membership.Id, userId, tenantId, role));
        return membership;
    }

    public void MakePrimary() => IsPrimary = true;
    public void ClearPrimary() => IsPrimary = false;

    public void ChangeRole(string newRole)
    {
        EnsureRoleAllowed(newRole);
        if (Role == newRole)
        {
            return;
        }
        var oldRole = Role;
        Role = newRole;
        Raise(new TenantMembershipRoleChanged(Id, oldRole, newRole));
    }

    public void Revoke(Guid actorId)
    {
        if (IsDeleted)
        {
            return;
        }
        DeletedAt = DateTimeOffset.UtcNow;
        DeletedBy = actorId;
        Raise(new TenantMembershipRevoked(Id, UserId, TenantId));
    }

    private static void EnsureRoleAllowed(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!AllowedRoles.Contains(role))
        {
            throw new ArgumentException(
                $"Role '{role}' is not allowed. Use '{RoleTenantAdmin}' or '{RoleTenantMember}'.",
                nameof(role));
        }
    }
}
