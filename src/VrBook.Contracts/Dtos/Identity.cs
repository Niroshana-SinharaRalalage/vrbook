namespace VrBook.Contracts.Dtos;

/// <summary>
/// Public-shape of an authenticated user. See proposal §6.2 — GET /me.
/// OPS.M.8 §3.10 (D10) — bumped to carry <see cref="IsPlatformAdmin"/> so the
/// web client can show/hide the Platform nav group without a second round trip.
/// <para>Slice OPS.M.21 (M.15 follow-up A step 2) — dropped the legacy
/// <c>IsOwner</c> / <c>IsAdmin</c> boolean flags. Nav derivation post-M.21
/// keys on <see cref="IsPlatformAdmin"/> + membership role via
/// <c>GET /api/v1/me/tenants</c> per ADR-0014.</para>
/// </summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    bool IsPlatformAdmin,
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UpdateProfileRequest(
    string DisplayName,
    string? Phone);

/// <summary>
/// OPS.M.7 §4.1 — read-side projection of the caller's own tenant for the
/// admin onboarding wizard. Returned by <c>GET /api/v1/me/tenant</c>. The
/// <c>Onboarding</c> sub-DTO is server-derived (see <c>OnboardingProgress</c>
/// helper in <c>VrBook.Modules.Identity</c>); the wizard never recomputes
/// <c>NextStep</c> on the client so OPS.M.8's super-admin view stays
/// authoritative.
/// </summary>
public sealed record MeTenantDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string DefaultCurrency,
    int PlatformFeeBps,
    string? StripeAccountStatus,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool HasStripeAccount,
    int PropertyCount,
    OnboardingProgressDto Onboarding);

/// <summary>
/// OPS.M.7 §4.1 — server-derived wizard progress for <see cref="MeTenantDto"/>.
/// <c>NextStep</c> is one of <c>"Welcome"</c>, <c>"CreateProperty"</c>,
/// <c>"ConnectStripe"</c>, <c>"AwaitingVerification"</c>, <c>"Done"</c>.
/// </summary>
public sealed record OnboardingProgressDto(
    bool IsComplete,
    string NextStep);

/// <summary>
/// OPS.M.8 §4.3 — paged response for the platform-admin tenant list.
/// </summary>
public sealed record PlatformTenantListResponse(
    IReadOnlyList<PlatformTenantListItemDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// OPS.M.8 §4.3 — lightweight row for the platform-admin tenant list.
/// Property/booking counts are NOT included here (§3.11 D11: stats are
/// fetched only on detail navigation to avoid N+1).
/// </summary>
public sealed record PlatformTenantListItemDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    bool HasStripeAccount,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    string DefaultCurrency,
    int PlatformFeeBps,
    DateTimeOffset CreatedAt);

/// <summary>
/// OPS.M.8 §4.3 — full detail view for one tenant. Mirrors
/// <see cref="MeTenantDto"/> with operator-only fields (SuspendedReason,
/// timestamps, lifetime stats). The PlatformAdmin web detail page binds
/// to this shape directly.
/// </summary>
public sealed record PlatformTenantDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string? SuspendedReason,
    string DefaultCurrency,
    int PlatformFeeBps,
    string? StripeAccountStatus,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool HasStripeAccount,
    int PropertyCount,
    int ActiveBookingCount,
    int TotalBookingCount,
    decimal LifetimeGrossRevenue,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Slice OPS.M.13.5 — response for <c>GET /api/v1/me/tenants</c>. Lists every
/// active tenant_memberships row the caller has, so the SPA can drive the
/// post-sign-in tenant picker per <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c>
/// §3.2. Includes IsPlatformAdmin at the top level so the SPA can route platform-
/// only humans (no memberships but PA=true) straight to the platform dashboard
/// instead of the "no tenant" dead-end.
/// </summary>
public sealed record MyTenantsResponse(
    IReadOnlyList<MyTenantMembershipDto> Memberships,
    bool IsPlatformAdmin);

/// <summary>
/// Slice OPS.M.13.5 — one tenant + role pair the caller has access to. Role is
/// the DB <c>identity.tenant_memberships.role</c> value (e.g. <c>"tenant_admin"</c>,
/// <c>"tenant_owner"</c>). Status is the tenant's operational status
/// (<c>"Active"</c> / <c>"PendingOnboarding"</c> / <c>"Suspended"</c> / <c>"Closed"</c>)
/// so the picker can gray out non-usable tenants.
/// </summary>
public sealed record MyTenantMembershipDto(
    Guid TenantId,
    string Slug,
    string DisplayName,
    string Status,
    string Role,
    bool IsPrimary);

/// <summary>
/// Slice OPS.M.22 §3 — request body for <c>POST /api/v1/admin/platform/users/seed</c>.
/// PlatformAdmin-only. Pre-creates an <c>identity.users</c> row with
/// <c>pre_seeded_at</c> set so the admin's first sign-in via
/// <c>AdminSignUpSignIn</c> flow links the arriving Entra oid to this shell row.
///
/// <para>Idempotent on normalized email: repeat requests for the same email
/// return the existing row's id + honour the memberships merge; a
/// request whose email collides with an ALREADY-linked (non-pre-seeded)
/// row returns 409.</para>
/// </summary>
public sealed record SeedAdminUserRequest(
    string Email,
    string DisplayName,
    bool IsPlatformAdmin,
    IReadOnlyList<SeedAdminUserTenantMembership> TenantMemberships);

/// <summary>
/// Slice OPS.M.22 §3 — one (tenant, role) pair the seeded admin will be a
/// member of. <c>Role</c> follows the <c>identity.tenant_memberships.role</c>
/// enum ("tenant_admin" today; "tenant_member" reserved).
/// </summary>
public sealed record SeedAdminUserTenantMembership(
    Guid TenantId,
    string Role,
    bool IsPrimary);

/// <summary>
/// Slice OPS.M.22 §3 — response from <c>POST /api/v1/admin/platform/users/seed</c>.
/// <c>Created</c> = true on fresh insert (201), false on idempotent re-hit (200).
/// <c>MembershipsCreated</c> lists the tenant ids that got a new membership row
/// on THIS request (already-existing memberships are omitted).
/// </summary>
public sealed record SeedAdminUserResult(
    Guid UserId,
    bool Created,
    IReadOnlyList<Guid> MembershipsCreated);
