namespace VrBook.Contracts.Dtos;

/// <summary>Public-shape of an authenticated user. See proposal §6.2 — GET /me.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    bool IsOwner,
    bool IsAdmin,
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
