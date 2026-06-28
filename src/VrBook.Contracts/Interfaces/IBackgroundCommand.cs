namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.6 §3.1 (D1) — marker for MediatR commands originating from a
/// background worker. The worker has no <c>ICurrentUser</c>, so
/// <c>TenantAuthorizationBehavior</c> early-returns for these.
///
/// <para>The complementary safety contract: every <c>IBackgroundCommand</c>
/// MUST also implement <see cref="ITenantScoped"/> — the worker stamps
/// <c>TenantId</c> from the row it's processing, and
/// <c>BackgroundCommandTenantScopeBehavior</c> rejects unstamped requests
/// (<c>TenantId == Guid.Empty</c>) before the handler runs. Enforced by
/// <c>BackgroundCommandMarkerTests</c>.</para>
///
/// <para>Pipeline order for an <c>IBackgroundCommand</c>:
/// <c>Validation → TenantAuthorization (early-return) → BackgroundCommandTenantScope
/// (assert + log-scope push) → AuditLog → handler</c>.</para>
/// </summary>
public interface IBackgroundCommand
{
}
