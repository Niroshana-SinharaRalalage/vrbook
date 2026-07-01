namespace VrBook.Contracts.Events;

public sealed record UserRegistered(Guid UserId, string Email, string DisplayName) : DomainEvent;

public sealed record UserEmailVerified(Guid UserId, string Email) : DomainEvent;

public sealed record UserDeactivated(Guid UserId, string Reason) : DomainEvent;

/// <summary>OPS.M.8 §3.1 (D1) — raised when a user is promoted to platform-admin.</summary>
public sealed record UserPlatformAdminGranted(Guid UserId, Guid ActorId) : DomainEvent;

/// <summary>OPS.M.8 §3.1 (D1) — raised when platform-admin is revoked.</summary>
public sealed record UserPlatformAdminRevoked(Guid UserId, Guid ActorId) : DomainEvent;

/// <summary>
/// Slice OPS.M.10.2 F11.7.6 — raised when the provisioning handler rebinds an
/// existing user row's B2CObjectId to a new incoming oid. Occurs when a
/// fresh oid arrives with an email that already matches an existing row
/// (typical: DevAuth-provisioned row being reclaimed by a real Entra
/// sign-in, or a social-IdP migration). Audit trail only — no downstream
/// reactions per <c>docs/OPS_M_10_2_F11_7_6_MULTI_ROW_USER_FIX.md §9.4</c>.
/// </summary>
public sealed record UserOidRebound(Guid UserId, string OldOid, string NewOid) : DomainEvent;
