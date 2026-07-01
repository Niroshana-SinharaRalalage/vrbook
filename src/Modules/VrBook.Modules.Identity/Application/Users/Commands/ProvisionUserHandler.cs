using MediatR;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Application.Users.Commands;

/// <summary>
/// Slice OPS.M.10.2 F11.7.6 — upsert by (oid ∪ email) with survivor merge.
///
/// <para>Pre-fix (through F11.7.5): keyed only on <c>B2CObjectId</c>. A
/// fresh oid arriving with an email that already belonged to a different
/// row created a NEW row (DevAuth-oid vs real-Entra-oid diverging when
/// SetPersonaEmail had earlier bound the DevAuth persona to a real
/// email). Downstream: middleware read the wrong row for the current
/// session, PA/tenant claims missed, M.4 gate 403'd. Symptom was the
/// walk-3 <c>Cross-tenant write rejected. actual=&lt;null&gt;</c> panel.</para>
///
/// <para>Post-fix (this handler): three branches, in order.</para>
///
/// <list type="number">
///   <item><b>oid hit</b>: an existing row matches the incoming oid.
///     Refresh + return its id. Unchanged from prior behavior.</item>
///   <item><b>oid miss + email hit</b>: no row for the oid but one or
///     more active rows share the email. Pick the survivor per
///     §3 ranking (PlatformAdmin &gt; has-membership &gt; oldest CreatedAt).
///     If the survivor's current oid AND the incoming oid are BOTH real
///     Entra oids (both parse as GUIDs), throw
///     <see cref="BusinessRuleViolationException"/> with rule
///     <c>email_already_claimed</c>. Otherwise
///     <see cref="User.ClaimOidForExistingProfile"/> rebinds the survivor
///     to the incoming oid.</item>
///   <item><b>oid miss + email miss</b>: provision a fresh row. Unchanged
///     from prior behavior.</item>
/// </list>
///
/// <para>Guardrail rationale: DevAuth persona oids are the fixed strings
/// <c>dev-owner-00000000</c>, <c>dev-guest-00000001</c>,
/// <c>dev-admin-00000002</c> — the only non-GUID oids in the system per
/// <c>DevAuthHandler.cs:49-73</c>. A real Entra <c>oid</c> is always
/// GUID-shaped. So <c>Guid.TryParse</c> cleanly distinguishes the two
/// origins without a fragile prefix heuristic. Post-F11.7.7 (DevAuth
/// retirement) the same check still holds: two Entra-shaped oids
/// colliding on an email is the exact case the guardrail defends
/// against (role addresses, distribution lists, social-IdP mirror).</para>
///
/// <para>Membership survivor tiebreaker: bounded to what's visible from
/// this handler. Full "has-membership" count requires an
/// <c>IdentityDbContext</c> injection; F11.7.6 accepts an approximation —
/// PA-first, then oldest-CreatedAt-first among tied rows — because
/// integration coverage (F11.7.6.6) verifies the observable outcome
/// (survivor gets the rebind) end-to-end.</para>
/// </summary>
internal sealed class ProvisionUserHandler(
    IUserRepository users,
    IUnitOfWork uow) : IRequestHandler<ProvisionUserCommand, Guid>
{
    public async Task<Guid> Handle(ProvisionUserCommand cmd, CancellationToken cancellationToken)
    {
        // Branch 1 — oid hit (unchanged).
        var byOid = await users.GetByB2CObjectIdAsync(cmd.B2CObjectId, cancellationToken);
        if (byOid is not null)
        {
            byOid.RefreshFromLogin(cmd.DisplayName, cmd.EmailVerified);
            if (cmd.IsOwner && !byOid.IsOwner)
            {
                byOid.GrantOwner();
            }
            if (cmd.IsAdmin && !byOid.IsAdmin)
            {
                byOid.GrantAdmin();
            }
            await uow.SaveChangesAsync(cancellationToken);
            return byOid.Id;
        }

        // Branch 2 — oid miss, email lookup.
        var byEmail = await users.GetActiveByEmailAsync(cmd.Email, cancellationToken);
        if (byEmail.Count > 0)
        {
            var survivor = PickSurvivor(byEmail);

            // Guardrail: both-oids-real-Entra means two distinct human
            // identities claim the same email. Refuse to rebind.
            if (IsRealEntraOid(survivor.B2CObjectId) && IsRealEntraOid(cmd.B2CObjectId))
            {
                throw new BusinessRuleViolationException(
                    "email_already_claimed",
                    $"Email '{cmd.Email}' is already claimed by a different Entra identity ({survivor.Id}).");
            }

            survivor.ClaimOidForExistingProfile(cmd.B2CObjectId);
            survivor.RefreshFromLogin(cmd.DisplayName, cmd.EmailVerified);
            if (cmd.IsOwner && !survivor.IsOwner)
            {
                survivor.GrantOwner();
            }
            if (cmd.IsAdmin && !survivor.IsAdmin)
            {
                survivor.GrantAdmin();
            }
            await uow.SaveChangesAsync(cancellationToken);
            return survivor.Id;
        }

        // Branch 3 — oid miss + email miss: provision fresh.
#pragma warning disable CS0618 // Obsolete Provision overload retained until this handler is removed in the M.13.3 middleware flip.
        var user = User.Provision(
            cmd.B2CObjectId,
            new Email(cmd.Email),
            cmd.DisplayName,
            cmd.EmailVerified,
            cmd.IsOwner,
            cmd.IsAdmin);
#pragma warning restore CS0618

        await users.AddAsync(user, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    /// <summary>
    /// Real-Entra oids are always GUID-shaped. DevAuth persona oids are
    /// the three fixed non-GUID strings enumerated in
    /// <c>DevAuthHandler.cs</c>. See F11.7.6 doc §3.
    /// </summary>
    private static bool IsRealEntraOid(string oid) => Guid.TryParse(oid, out _);

    /// <summary>
    /// Survivor precedence per F11.7.6 §3: PlatformAdmin &gt; oldest
    /// CreatedAt. Membership-count tiebreaker is deferred to F11.7.6.4's
    /// migration where the DbContext scan is cheap; the handler-side pick
    /// runs on a small in-memory list and PA-first + oldest CreatedAt is
    /// enough for the multi-row cases we see in the wild.
    /// </summary>
    private static User PickSurvivor(IReadOnlyList<User> matches)
    {
        return matches
            .OrderByDescending(u => u.IsPlatformAdmin)
            .ThenBy(u => u.CreatedAt)
            .First();
    }
}
