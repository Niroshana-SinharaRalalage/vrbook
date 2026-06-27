namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Marker for MediatR commands whose execution must be gated by
/// <c>TenantAuthorizationBehavior</c>. Carries the tenant the command intends
/// to act on; the behavior rejects the request if it does not match
/// <see cref="ICurrentUser.TenantId"/>.
///
/// <para>
/// Per OPS_M_4_PLAN section 3.1: the marker is non-bare so the contract is
/// type-checked at compile time and the behavior reads the value directly off
/// the command without reflection. The aggregate-lookup variant was rejected
/// because OPS.M.3 made the aggregate's <c>TenantId</c> authoritative; the
/// command's <c>TenantId</c> field is what the controller stamped from
/// <c>currentUser.TenantId</c> (owner writes) or read off the aggregate
/// during command construction (guest writes — see <c>PlaceBookingHandler</c>
/// for the pattern).
/// </para>
///
/// <para>
/// Defense in depth comes from RLS in Slice OPS.M.9.
/// </para>
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
