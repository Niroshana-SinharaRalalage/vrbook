namespace VrBook.Api.IntegrationTests.Auth;

/// <summary>
/// Slice OPS.M.14.1 (reshaped OPS.M.15.5) — one row in a fixture's persona
/// lookup. Consumed by <see cref="TestAuthHandler"/> to synthesize a
/// <c>ClaimsPrincipal</c> that matches the shape of an Entra External ID
/// access token.
///
/// <para>The pre-M.15 <c>IsOwner</c> / <c>IsAdmin</c> booleans were replaced
/// with <see cref="Roles"/> (a list of role tokens emitted as
/// <c>ClaimTypes.Role</c> claims) — this matches the production Entra token
/// shape where App Role assignments become a native <c>roles</c> claim that
/// JwtBearer maps to <c>ClaimTypes.Role</c>. Post-M.15 no controller reads
/// <c>Owner</c> / <c>Admin</c> role literals; tenant-admin authority comes
/// from <c>identity.tenant_memberships</c> materialized as MembershipRoles
/// by <c>UserProvisioningMiddleware</c>. Personas can populate
/// <see cref="Roles"/> only for edge cases exercising specific JwtBearer
/// behaviour (e.g., testing a token that DOES carry <c>PlatformAdmin</c>).</para>
/// </summary>
public sealed record TestPersona(
    string Oid,
    string Email,
    string DisplayName,
    IReadOnlyList<string>? Roles = null);
