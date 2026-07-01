using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Domain;

/// <summary>
/// Slice OPS.M.13 — links an <see cref="User"/> aggregate to a specific
/// (provider, external_id) sign-in identity. Enables one-human-many-oids
/// as the model natively supports both the current single-provider
/// (Entra External ID) and the OPS.M.12 multi-provider (Google + Microsoft
/// federated through External ID) shapes.
///
/// <para>Design ref: <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1 +
/// §3.5. Same pattern used by Auth0, Passport.js, Google People API.</para>
/// </summary>
public sealed class UserIdentity : AggregateRoot
{
    /// <summary>The <see cref="User"/>.Id this identity is linked to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Identity provider name. Constrained by DB CHECK to
    /// <c>('entra','google','microsoft','apple','test')</c> per §2.1.
    /// The M.13 slice only emits <c>entra</c> and <c>test</c>; OPS.M.12
    /// introduces <c>google</c> and <c>microsoft</c>.
    /// </summary>
    public string Provider { get; private set; } = default!;

    /// <summary>
    /// External identifier from the provider (the <c>oid</c> claim for
    /// Entra; <c>sub</c> or provider-specific external id for social IdPs).
    /// The (Provider, ExternalId) pair is UNIQUE at the DB level.
    /// </summary>
    public string ExternalId { get; private set; } = default!;

    public DateTimeOffset FirstSeenAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    private UserIdentity() { } // EF Core

    /// <summary>
    /// Create a new user-identity link. Called from
    /// <c>ProvisionOrLinkUserHandler</c> when a fresh (provider, oid) pair
    /// arrives — either the first identity on a new user, or a
    /// second-provider identity on an existing user.
    /// </summary>
    public static UserIdentity Create(
        Guid userId,
        string provider,
        string externalId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        return new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = provider,
            ExternalId = externalId,
            FirstSeenAt = now,
            LastSeenAt = now,
        };
    }

    /// <summary>
    /// Bump <see cref="LastSeenAt"/> on Branch 1 (identity-hit) of the
    /// provisioning handler. Idempotent — the write is applied even when
    /// the value is unchanged so the DB row's <c>updated_at</c> reflects
    /// the sign-in even if the clock hasn't moved.
    /// </summary>
    public void UpdateLastSeen(DateTimeOffset now)
    {
        LastSeenAt = now;
    }
}
