# 16. Admin sign-in surface is separate from guest sign-in surface, and admins may NEVER hold a social identity row

- **Status**: Accepted
- **Date**: 2026-07-06
- **Deciders**: Solutions Architecture (architect consult during OPS.M.12 planning), Owner (owner-locked answers 2026-07-05)
- **Tags**: identity, entra, authorization, product-decision

## Context

VrBook ships two overlapping sign-in surfaces:

1. **Guest sign-in** — book, view, cancel. High-volume, low-privilege.
   Consumer-grade UX: one-tap Google / Microsoft / Facebook / Apple is
   table stakes.
2. **Admin sign-in** — platform admin (~3 humans) and tenant admin
   (per-tenant operators). Low-volume, high-privilege. Consumer social
   sign-in is not appropriate:
   - Provider-side account compromise (e.g. Google credential stuffing)
     directly propagates to admin authority.
   - Consumer providers have variable MFA enforcement; Entra local
     lets us mandate OTP or password unconditionally.
   - Auditability — "admin signed in via Google" adds a link in the
     trust chain that legal and SOC2 discussions have to defend.

The M.13 identity aggregate already allows N `user_identities` rows per
`user` (one per provider). Without a constraint, an admin's user row
could end up with `provider IN ('entra','google')` — a Google sign-in
would silently satisfy the middleware because the user IS
`IsPlatformAdmin=true` and the token IS valid — nothing prevents this
except code discipline.

Owner policy locked 2026-07-05:

> "Reject for Tenant Admin, I want tenant Admin only to have username
> password. Guests can Entra id or external idp."
> "tenant admi only have extra id with username/password no matter what."

## Decision

The two surfaces are split at three layers.

### Layer 0 — Provider trust: two Entra user flows

`AdminSignUpSignIn` allows Email signup only. `GuestSignUpSignIn`
allows Email signup + Google + Microsoft + Facebook + Apple. Both
flows emit the `idp` application claim.

The SPA picks the flow via a per-flow authority URL:
`NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN` vs `_GUEST`. `sessionStorage`
persists the chosen flow so silent-token-refresh reconstructs the same
authority.

Detail in [runbooks/social_idp_setup.md §7](../runbooks/social_idp_setup.md#7-entra-user-flows-admin-vs-guest).

### Layer 1 — REFUSE at provisioning (invariant enforced in DB shape)

`ProvisionOrLinkUserHandler` inspects the target user for admin
authority before adding any social-provider identity row. If the
matched user has `IsPlatformAdmin=true` OR any active
`tenant_memberships` row, the handler throws
`BusinessRuleViolationException` with rule `admin_social_signin_refused`
— the row is never inserted. This runs on both Branch 2 (matched by
verified email) and Branch 3 (race-recovery path after a concurrent
insert). Layer 1 guarantees the invariant in the database, not just in
runtime code paths.

Detail in [OPS_M_12_SOCIAL_IDPS_PLAN.md §2.2](../OPS_M_12_SOCIAL_IDPS_PLAN.md#22-linking-rule-when-a-social-sign-in-email-matches-an-existing-entra-user).

### Layer 2 — Middleware belt (defence-in-depth)

`AdminSocialIdpRejectionMiddleware` inspects every authenticated
request. If `ICurrentUser.IdentityProvider` is in
`HttpCurrentUser.SocialIdpValues` AND
(`ICurrentUser.IsPlatformAdmin` OR `ICurrentUser.MembershipRoles.Count
> 0`), the middleware throws `AdminSocialIdpRejectedException` → 403
with `ProblemTypes.AdminSocialIdpRejected`.

Whitelist: `/api/v1/me` + `/api/v1/me/tenants` so the SPA can render
the admin-social-rejected error page and offer a "Sign out and try
again" CTA.

Layer 2 is redundant by construction — if Layer 1 works, no admin will
ever hold a social identity, so no admin token will ever carry a
social `idp` claim. But Layer 2 catches Layer 1 regressions
proactively; both must be present.

## Consequences

### Positive

- **Invariant in DB shape**: `is_platform_admin=true` OR
  `tenant_memberships` row implies `user_identities.provider` set is a
  subset of `{'entra'}`. Queryable, verifiable, auditable.
- **Admin surface hardened**: password / OTP mandatory; no provider-side
  account compromise routes to admin authority.
- **Guest UX unchanged**: guests get one-tap Google / Microsoft /
  Facebook / Apple; no regression from M.13 shape.
- **Auditability**: `user_identities` audit-log rows show a clean
  history — either "entra local from day one" (admin path) or "social
  provider + optional entra later" (guest path). No mixed history for
  any admin.

### Negative

- **Two Entra user flows to maintain**: each portal setup must be
  duplicated. Runbook §7 documents both.
- **A guest promoted to tenant admin loses social sign-in**: after
  promotion, the guest must sign in via Entra local (email + password
  OR OTP) to exercise admin authority. If the guest never had an entra
  identity, the promote flow (future slice) will refuse promotion
  until the operator uses the tenant-invitation flow instead. Owner-
  locked answer §11-Q4.
- **Existing admins created via Entra local pre-M.12**: unaffected. The
  M.13.4 backfill already recorded their `provider='entra'` identity.
- **Adds a middleware hop on every authenticated request**: cost is
  cheap (in-memory set lookup on a claim that's already resolved).

### Neutral

- **Owner-locked, not architecture-forced**: this choice IS the owner
  policy. If policy changes (e.g. "admins may use Google if MFA is
  enforced"), Layer 1 relaxes to a per-provider allowlist; Layer 2
  removes the `IsPlatformAdmin OR memberships > 0` predicate. Both
  layers point at the same policy centre — one edit changes the
  behaviour of both.

## Alternatives considered

**A. Middleware only (Layer 2 only), no Layer 1 REFUSE.**
Rejected — the DB shape isn't the invariant we want. A regression that
lets an admin's user row acquire a `provider='google'` row is
undetectable at rest; only signed-in traffic would be blocked. Owner
policy is about the DB shape, not just the traffic shape.

**B. Different admin app registration ("vrbook-admin-api").**
Considered. Would give admin a fully separate token audience.
Rejected — doubles the client-registration surface for questionable
gain; the flow split already achieves surface separation, and the
tenant registration for admin app would need to be prod-Entra-not-CIAM
which contradicts ADR-0012. Revisit if we ever need Conditional Access
policies that differ by app.

**C. Deny social IdPs at the guest flow too (all-Entra-local).**
Rejected — owner policy explicitly wants social IdPs for guest UX
("Guests can Entra id or external idp"). This choice would also make
the guest signup funnel noticeably worse than competitors.

**D. Layer 1 only, no middleware belt.**
Rejected — Layer 1 alone is correct-by-construction but a Layer 1
regression (e.g. a new provisioning code path added elsewhere) would
be invisible until an admin actually signed in via social IdP. Layer
2 fails loud in that case with a testable 403 and a specific problem
type the SPA reads.

## References

- [OPS_M_12_SOCIAL_IDPS_PLAN.md](../OPS_M_12_SOCIAL_IDPS_PLAN.md) — full
  design doc including §11 owner-locked answers 2026-07-05.
- [runbooks/social_idp_setup.md](../runbooks/social_idp_setup.md) —
  operator procedure for the 4 IdPs + 2 Entra flows.
- [ADR-0012 — Entra External ID over B2C](0012-entra-external-id-over-b2c.md) —
  single-tenant Entra CIAM baseline.
- [ADR-0014 — App Roles for global, DB for per-tenant](0014-app-roles-global-db-per-tenant.md) —
  authorization split this ADR complements.
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionOrLinkUserHandler.cs` — Layer 1 implementation.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AdminSocialIdpRejectionMiddleware.cs` — Layer 2 implementation.
- `src/VrBook.Domain/Common/DomainException.cs` — `AdminSocialIdpRejectedException`.
