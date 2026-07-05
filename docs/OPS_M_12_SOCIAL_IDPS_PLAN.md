# Slice OPS.M.12 — Google social IdP + admin-vs-guest sign-in surface split + middleware admin-rejection

- **Status:** DRAFT for reviewer sign-off (`niroshanaks`). NOT approved for execution. All §9 questions must be locked before any sub-commit lands.
- **Date:** 2026-07-05.
- **Author (role):** Platform Enterprise Architect.
- **Predecessors:** [`OPS_M_13_CLOSE_OUT.md`](./OPS_M_13_CLOSE_OUT.md) (email-canonical users + `user_identities` table live), [`OPS_M_14_CLOSE_OUT.md`](./OPS_M_14_CLOSE_OUT.md) (Entra-only auth baseline; `ExternalObjectId` rename in place; mock JwtBearer test handler wired).
- **Supersedes:** the 108-line stub previously at this same path, authored during OPS.M.10.2 F11 (pre-M.13). That draft assumed a single-oid `identity.users` schema and had no notion of admin/guest surface split. Every §-numbered thing there is subsumed by this rewrite; keep the stub's text in git history as design-diary reading only.
- **Scope:** ONE vertical slice. Wires Google as a federated IdP through Entra External ID for the guest sign-in surface, splits the SPA sign-in flow into `admin` (Entra local only) and `guest` (Entra local + Google), and lands a middleware admin-vs-social rejection gate that guarantees a Google-federated token can never carry admin authority — regardless of DB state, JWT claims, or future config drift.
- **Explicitly NOT in this slice:**
  - **Microsoft consumer accounts + Apple + Facebook** — Google is the single shipping IdP for M.12 per §9-Q1. Microsoft is the fanned-out follow-up (**OPS.M.12.1**) and reuses the same middleware gate; Apple is out of scope.
  - **App-role legacy claim reads / `[Authorize(Roles=)]` drop** — **OPS.M.15** (still deferred).
  - **`_pre_m13_snap` schema cleanup** — scheduled 2026-08-04.
  - **`@unknown.local` synthetic-email row cleanup** — maintenance follow-up.
  - **Housekeeping module** (M.16 fanout).
  - **Production tenant configuration** — the Entra External ID tenant admin surface change is applied to the staging tenant only in M.12; the same Google client registration + user-flow shape is re-applied to the prod tenant under a follow-up ops slice.

---

## §0 What we're doing + why now

### §0.1 The domain gap

The staging Entra External ID tenant (`vrbook.ciamlogin.com`) currently exposes ONE sign-in path across the board: local accounts (email + password / email + OTP). Guests booking a stay hit the same sign-in surface as tenant admins hit — with the same email-only credential form. The marketplace UX baseline (Airbnb, Booking, VRBO) is "Continue with Google" as the first-class primary CTA for guests, with email as the secondary path; every friction point in the guest funnel is a lost booking. The site-header `SiteHeaderAuth` (`web/src/components/layout/SiteHeaderAuth.tsx:37`) triggers `loginRedirect(loginRequest)` unconditionally, and `msalConfig.ts` names one authority in `NEXT_PUBLIC_ENTRA_AUTHORITY` — there is no seam for surface-conditional flow selection.

### §0.2 The policy constraint (owner-locked 2026-07-05)

The reviewer has explicitly locked the following, which shapes the entire M.12 sub-commit sequence:

> Platform Admin + Tenant Admin sign-in surface must show ONLY Entra local (email + OTP or password) — never social IdPs. Guest sign-in surface may offer Google + Entra local. Server-side, if a token's `idp` (or `amr`) claim indicates a social IdP AND the user carries an active tenant-admin membership or `is_platform_admin=true`, the request must be rejected at the auth middleware — a Google-federated user can never wield admin authority.

The rationale:
- Admin authority = irrevocable-in-place mutation power (approve booking, refund charge, promote another admin, edit pricing, publish property). Gate that on the assurance level the operator controls end-to-end.
- Guest authority = write access to their own bookings + reviews. If a Google account is compromised, blast radius is one guest — same class of harm we've already accepted for email-hijack.
- Splitting the surfaces at the FIRST HOP (the SPA sign-in trigger) means guests never see the admin flow and admins never see the guest flow. **The middleware gate is the belt to the SPA's braces:** if a Google-federated user acquires admin authority via any path (data-heal race, direct SQL, config drift, or a future SPA regression), the request is rejected with a specific error code before it touches a handler.

### §0.3 Why now

- OPS.M.13 shipped `identity.user_identities` with a multi-provider CHECK constraint (`entra`, `google`, `microsoft`, `apple`, `test`) — M.13 emits only `entra`/`test` but the schema was designed for M.12 to land next.
- OPS.M.14 shipped the DevAuth retirement — every path into the API from a real user now goes through JwtBearer against Entra.
- The `ExternalObjectId` rename in M.14.3 was land-first-provider-later prep for M.12; the property doc comment already reads "the identity provider's oid — Entra today, more via M.12."
- No product surface today prompts a guest to sign up if they don't already have an Entra local account. The one-step-Google-flow is a growth lever the operator wants to pull.

### §0.4 Successful-case walk (post-ship)

- Anonymous guest lands on `/properties/beach-villa`. Clicks `Sign in to book`.
- SPA calls `signIn({ flow: 'guest' })`. MSAL emits `loginRedirect` pinning Entra to the `GuestSignUpSignIn` user flow. Entra shows "Continue with Google" primary + local email field secondary.
- Guest clicks Continue with Google → Google OAuth → Entra mints access token, `idp = "google.com"`, `email = "foo@gmail.com"`, `email_verified = "true"`.
- SPA callback → `ProvisionOrLinkUserHandler` Branch 3 → fresh `identity.users` row + `user_identities(provider='google')`.
- Guest returns to `/properties/beach-villa`, clicks `Book this stay` → 201 Tentative.
- Zero admin authority; admin-rejection gate never fires because the second predicate is false.

### §0.5 Adversarial-case walk (gate fires)

- Existing tenant admin `owner-a@vrbook.test` has one `tenant_admin` membership. They accidentally click "Continue with Google" from the admin page (post-M.12 regression — should not happen).
- Google → Entra → SPA callback → Branch 2 (email hit + verified) → `user_identities(provider='google')` row created linked to the SAME `identity.users` row.
- `UserProvisioningMiddleware` computes `idp='google.com'` AND has membership → gate fires → 403 with problem type `/problems/admin-social-idp-rejected`.
- SPA admin-guard companion (§6.3) paints "Admin roles require an Entra work account. Sign out and sign back in with your Entra credentials."
- After sign-out + Entra-local sign-in, the SAME `identity.users` row is accessed via the Entra `user_identities` row → M.13 Branch 1 (identity hit) → same user, admin auth restored.

---

## §1 What ships in each sub-commit

Sub-commit convention: `Slice OPS.M.12.N: <summary>`. All commits target `develop`. Each ends CI-green under `dotnet test --filter "Category!=Integration"`. Sequence:

```
M.12.1 GREEN — Middleware idp-claim propagation + ICurrentUser.IdentityProvider accessor + arch tests.
M.12.2 GREEN — Admin-vs-social rejection middleware + AdminSocialIdpRejectedException + ProblemDetails wiring.
M.12.3 GREEN — ProvisionOrLinkUserHandler multi-provider verify + IdentityProviderClassifier + integration tests.
M.12.4 DOCS  — Entra tenant + Google Cloud runbook + ADR + Bicep audit doc.
M.12.5 GREEN — SPA sign-in flow split (/auth/signin?flow={admin|guest}) + per-flow MSAL authority + admin-layout redirect.
M.12.6 GREEN — SPA admin-guard useAdminGuard() + /auth/admin-social-idp-rejected error page + admin/error.tsx branch.
M.12.7 GREEN — Close-out: arch-tests consolidation + MASTER_PLAN row + close-out doc + Bicep second-phase cleanup.
```

7 sub-commits; one doc-only (M.12.4); six code-carrying. No RED-only commit — arch tests bundle with the code.

### M.12.1 — GREEN. `idp` claim propagation + `ICurrentUser.IdentityProvider` accessor.

**Land first** because every downstream sub-commit reads `idp`. Zero user-visible change.

Files touched:
- `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` — ADD `string? IdentityProvider { get; }`.
- `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs` — ADD `public string? IdentityProvider => null;`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs`
  - ADD `public const string IdpClaim = "idp";`
  - ADD `public const string ProviderEntraLocal = "entra";`
  - ADD `public const string ProviderGoogle = "google";`
  - ADD `public static readonly IReadOnlySet<string> SocialIdpValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "google.com", "facebook.com", "live.com", "linkedin.com", "twitter.com", "amazon.com" };`
  - ADD `public string? IdentityProvider` accessor — reads `idp` claim, normalizes to `"entra"` if absent or equals tenant issuer host, returns raw claim value otherwise.

Tests:
- `tests/VrBook.Modules.Identity.Tests/HttpCurrentUserIdpAccessorTests.cs` (NEW, Unit) — 5 facts: no idp, google.com, issuer-host, mixed case, anonymous.
- `tests/VrBook.Architecture.Tests/OpsM12_IdentityProviderAccessorShapeTests.cs` (NEW, Unit) — 4 reflection/constant facts.

CI: green.

### M.12.2 — GREEN. Admin-vs-social rejection middleware.

Auth-critical; land before any UX depends on it. Belt-first, then braces.

Files touched:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AdminSocialIdpRejectionMiddleware.cs` (NEW) — sits after `UserProvisioningMiddleware`. Predicate: `IdentityProvider ∈ SocialIdpValues AND (IsPlatformAdmin OR MembershipRoles.Count > 0)` → throw `AdminSocialIdpRejectedException`. Whitelist: `GET /api/v1/me` + `GET /api/v1/me/tenants` (hard-coded set, not config-driven — auth-critical). Anonymous short-circuits.
- `src/VrBook.Domain/Common/DomainException.cs` — ADD `AdminSocialIdpRejectedException : ForbiddenException` with `Rule => "admin_authority_requires_entra_local"`, `IdentityProvider`, `AttemptedTenantIds` properties.
- `src/VrBook.Contracts/Common/ProblemTypes.cs` — ADD `AdminSocialIdpRejected` constant.
- `src/VrBook.Api/Middleware/ProblemDetailsConfig.cs` — register a specific mapper BEFORE the generic `ForbiddenException` mapper. Emit `rule` + `identityProvider` in `Extensions`.
- `src/VrBook.Api/Program.cs` — insert `app.UseMiddleware<AdminSocialIdpRejectionMiddleware>();` after `UserProvisioningMiddleware`.

Tests:
- `tests/VrBook.Modules.Identity.Tests/AdminSocialIdpRejectionMiddlewareTests.cs` (NEW, Unit) — 8 truth-table facts + whitelist exemption.
- `tests/VrBook.Api.IntegrationTests/Auth/AdminSocialIdpRejectionEndToEndTests.cs` (NEW, Integration) — 3 facts against `TwoTenantApiFixture` with new social-IdP personas.
- `tests/VrBook.Api.IntegrationTests/Auth/TestAuthHandler.cs` (MODIFY) — `TestPersona` gains `string IdentityProvider = "entra"` default. `HandleAuthenticateAsync` writes `new Claim("idp", p.IdentityProvider)`.
- `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantTestAuthHandler.cs` (MODIFY) — add `SocialGuestOid` + `SocialTenantAdminIdp` personas.
- `tests/VrBook.Architecture.Tests/OpsM12_AdminSocialIdpRejectionShapeTests.cs` (NEW, Unit) — 6 shape facts.

CI: green.

### M.12.3 — GREEN. `ProvisionOrLinkUserHandler` multi-provider verify.

Verification-heavy; the M.13 handler is provider-parametric, minimal source diff.

Files touched:
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionOrLinkUserHandler.cs` — no algorithm change. Add one `ILogger.LogInformation` line at Branch 2 for cross-provider-link observability.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs` — line ~94: `Provider: "entra"` hardcoded → `Provider: IdentityProviderClassifier.Classify(idpClaim, tenantIssuerHost)`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/IdentityProviderClassifier.cs` (NEW) — static helper mapping `idp` claim → `user_identities.provider` string.

Config: `EntraExternalId:TenantIssuerHost` — new key. Bicep/env wiring lands in M.12.5.

Tests:
- `tests/VrBook.Modules.Identity.Tests/IdentityProviderClassifierTests.cs` (NEW, Unit) — 8 facts.
- `tests/VrBook.Modules.Identity.Tests/ProvisionOrLinkUserHandler_MultiProviderTests.cs` (NEW, Unit) — 6 facts covering Branch 1/2/3 with google + races.
- `tests/VrBook.Api.IntegrationTests/Identity/SocialIdpProvisioningEndToEndTests.cs` (NEW, Integration) — 3 facts.

CI: green.

### M.12.4 — DOCS. Entra + Google Cloud runbook.

Docs-only. Documents the portal steps so M.12.5 can land against a specific user-flow name.

Files touched:
- `docs/runbooks/social_idp_setup.md` (NEW) — 10-step operator recipe:
  1. Google Cloud Console OAuth client creation.
  2. Entra External ID Google IdP registration (Key Vault-backed credentials).
  3. `GuestSignUpSignIn` user flow: Local Accounts + Google. **Application claims MUST include `idp`** (critical — see §7 risk 1).
  4. `AdminSignUpSignIn` user flow: Local Accounts only.
  5. Application registration attach.
  6. Bicep audit table (portal-only vs Bicep-managed).
  7. Redirect URI documentation.
  8. Smoke walk (10 min): successful path + adversarial path.
- `docs/adr/0016-admin-vs-social-idp-surface-split.md` (NEW) — ADR capturing the split, mirrors `docs/adr/0012-entra-external-id-over-b2c.md` shape.

No code changes. Per `feedback_no_ci_for_doc_only_commits`, the prior code commit (M.12.3) is the CI gate.

### M.12.5 — GREEN. SPA sign-in flow split.

Files touched (web):
- `web/src/lib/auth/msalConfig.ts` — refactor:
  - `authorityForFlow('admin' | 'guest')` reads `NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN` / `_GUEST` with `NEXT_PUBLIC_ENTRA_AUTHORITY` as one-cycle fallback.
  - `loginRequestFor(flow)` returns a `RedirectRequest` with per-flow authority + `state: JSON.stringify({ flow, returnTo })`.
- `web/src/lib/auth/useAuth.ts` — `signIn` accepts `{ flow?: SignInFlow }`; default `guest`.
- `web/src/components/layout/SiteHeaderAuth.tsx` — passes `{ flow: 'guest' }`.
- `web/src/components/auth/SignInGate.tsx` — same.
- `web/src/app/admin/layout.tsx` (NEW) — client-side guard: unauthenticated → `router.replace('/auth/signin?flow=admin&returnTo=<pathname>')`.
- `web/src/app/auth/signin/page.tsx` (NEW) — reads `?flow` + `?returnTo`, persists flow to sessionStorage, kicks off `loginRedirect(loginRequestFor(flow))`.
- `web/src/app/auth/callback/page.tsx` (MODIFY) — reads `state.flow` + `state.returnTo`. Admin flow → M.13 tenant picker. Guest flow → `returnTo`.
- `infra/main.bicep` — new Key Vault-backed env vars `NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN`, `_GUEST`, `EntraExternalId__TenantIssuerHost`. Legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` retained one cycle.
- `.env.example` — sync.

Tests:
- `web/src/lib/auth/msalConfig.test.ts` (MODIFY) — authority selection.
- `web/src/__tests__/signin-flow-routing.test.tsx` (NEW).
- `web/src/__tests__/admin-layout-signin-redirect.test.tsx` (NEW).

CI: `cd-staging-web.yml` green.

### M.12.6 — GREEN. SPA admin-guard companion.

Files touched (web):
- `web/src/lib/auth/useAdminGuard.ts` (NEW) — hook returning `{ status: 'ok' | 'social-admin-rejected' | 'loading' | 'unauthenticated' }`.
- `web/src/app/auth/admin-social-idp-rejected/page.tsx` (NEW) — error page with "Sign out and try again" CTA.
- `web/src/app/admin/error.tsx` (MODIFY) — adds branch for `problem.type === '/problems/admin-social-idp-rejected'`.
- `web/src/app/admin/layout.tsx` (MODIFY) — `useAdminGuard()` check; redirect to `/auth/admin-social-idp-rejected` on `social-admin-rejected`.
- `web/src/app/select-tenant/page.tsx` (MODIFY) — same guard.

Tests: 6 facts in `useAdminGuard.test.tsx` + error page render tests.

CI: green.

### M.12.7 — GREEN. Close-out.

- `tests/VrBook.Architecture.Tests/OpsM12_SocialIdpShapeTests.cs` (NEW) — 7 consolidated shape facts.
- `docs/OPS_M_12_CLOSE_OUT.md` (NEW) — standard 8-section close-out.
- `docs/MASTER_PLAN.md` — add M.12 row.
- `docs/runbooks/social_idp_setup.md` — append post-M.12 walk playbook.
- `infra/main.bicep` — drop legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` fallback.
- `.env.example` — sync.

CI: green.

---

## §2 Domain-model decisions locked

### §2.1 One User, many `UserIdentity` rows — unchanged from M.13

The M.13 schema allows N identities per user; M.12 exercises this for the first time. No constraint added.

### §2.2 Linking rule when Google sign-in email matches an existing Entra user

**Decision: LINK by default** (M.13 Branch 2, verified-email guard). No new algorithm. Rejected: REJECT-with-error, CONSENT-prompt.

### §2.3 No new domain event on cross-provider link

The existing `IAuditable` marker on `ProvisionOrLinkUserCommand` writes to `identity.audit_events`. That's the durable audit; no new event.

### §2.4 Cross-account email edge

M.13 partial-UNIQUE on `lower(email) WHERE deleted_at IS NULL` prevents the two-user-same-email state. Out-of-band email change on Entra doesn't rewrite our stored email (Branch 1 only refreshes `LastSeenAt`). No code change.

---

## §3 Middleware admin+social rejection semantics

### §3.1 `idp` claim mapping

| Sign-in path | Token `idp` | Normalized |
|---|---|---|
| Entra local | absent OR equals tenant issuer host | `"entra"` |
| Google | `"google.com"` | raw for gate; `"google"` in `user_identities.provider` |
| Microsoft consumer (M.12.1) | `"live.com"` | raw + `"microsoft"` |
| Facebook/Apple (out of scope) | `"facebook.com"` / `"apple.com"` | raw + DB CHECK rejects provider write |

`IdentityProviderClassifier.Classify(idpClaim, tenantIssuerHost)` centralizes; both middleware + `HttpCurrentUser.IdentityProvider` accessor call the same classifier.

### §3.2 Gate predicate

```
if IdentityProvider ∈ SocialIdpValues
AND (IsPlatformAdmin == true OR MembershipRoles.Count > 0)
    reject with 403 AdminSocialIdpRejectedException
else
    proceed
```

Both conjuncts required. `MembershipRoles.Count > 0` = any membership (cheap safety; endpoint-level auth still applies once past).

### §3.3 Post-rejection semantics

Reject the request; don't force sign-out server-side. SPA companion (M.12.6) drives "sign out and try again."

### §3.4 Rejection scope

All routes EXCEPT `GET /api/v1/me` + `GET /api/v1/me/tenants` (SPA needs these to render the error page). Anonymous surface short-circuits.

### §3.5 Claim source — `idp` alone

`idp` alone. `amr` is a fallback if empirically absent on some user flow config. Not shipped by default.

### §3.6 Whitelist path — hard-coded

`HashSet<PathString>` field in middleware source. Any change requires code review + arch-test update. Config-driven whitelisting is auth-critical drift risk.

---

## §4 Provisioning handler multi-provider extension

### §4.1 Algorithm reuse — zero source diff

M.13's three branches (identity hit, identity-miss + email-hit + verified, identity-miss + email-miss) all use `cmd.Provider` verbatim. Only the middleware call-site changes.

### §4.2 Cross-provider linking — worked example

1. User A signs in with Entra local → Branch 3 → user U1 + entra identity.
2. Later signs in with Google, same email, verified → classifier maps `provider='google'` → Branch 1 miss → Branch 2 hit → new `user_identities(user_id=U1, provider=google)`.
3. Middleware sees U1 with `idp=google.com` → if U1 has memberships/PA → reject.
4. Sign out. Sign in with Entra local → Branch 1 (entra identity hit) → U1 unchanged. Admin auth works again.

Key invariant: same User ID stable across identities. Only CURRENT sign-in's IdP gates admin authority.

### §4.3 M.13.4 backfill compatibility

Every existing user has `user_identities(provider='entra')` from M.13.4. First Google sign-in for any such user hits Branch 2 and creates the second identity row. No migration needed.

### §4.4 Different-email edge

Alice-entra + Alice-google with different emails → Branch 3 creates a NEW User. Two separate accounts. Merging is out-of-scope.

---

## §5 Entra + Google configuration

### §5.1 Two user flows

- `AdminSignUpSignIn` — Local Accounts only. Claims: `email, email_verified, name, oid, idp`.
- `GuestSignUpSignIn` — Local Accounts + Google. Same claims. **`idp` MUST be included** (§7 risk 1).

### §5.2 MSAL flow mechanism — per-flow authority (Option A)

`NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN` + `_GUEST` env vars. Fallback if broken: `extraQueryParameters: { p: '<flow>' }`.

### §5.3 Bicep vs portal

- Google OAuth client — portal.
- Entra IdP registration + user flows — portal.
- Key Vault secrets — Bicep.

### §5.4 Redirect URI

Google Cloud Console: `https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/federation/oauth2`.

Entra app registration reply URLs unchanged from M.14.

---

## §6 SPA sign-in flow split

### §6.1 Wireframes

Admin: email + password (Entra-rendered). No Google button.
Guest: primary "Continue with Google" + secondary email + password.

### §6.2 MSAL config

Per-flow authority. `sessionStorage.setItem('vrbook-signin-flow', flow)` on callback so silent-token-refresh reconstructs the right authority.

### §6.3 Admin-guard hook

`useAdminGuard()` in `web/src/app/admin/layout.tsx` + `select-tenant/page.tsx`. NOT in `Providers.tsx`.

### §6.4 Post-callback routing

Admin → M.13.5 tenant picker (0/1/N). Guest → skip picker, redirect to `returnTo`.

### §6.5 Sign-out

Unchanged from M.14 — clears sessionStorage + MSAL logout.

---

## §7 Risks + open questions

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | `idp` absent on user-flow claims config | Medium | HIGH | Runbook explicitly requires `idp` in claims list; smoke walk verifies |
| 2 | MSAL 3.x per-flow authority doesn't work | Low | Medium | Fall back to Option B `extraQueryParameters` |
| 3 | Google rejects redirect URI format | Low | Low | Documented canonical format; portal-config-time detection |
| 4 | Entra + Google emails differ | Low | Low | Two-accounts case, natural handling |
| 5 | Guest promoted to tenant admin with Google-only identity | Medium | Medium | §9-Q4 rec: promote flow refuses; sign in with Entra local first |
| 6 | Entra rejects Google `email_verified` shape | Very low | Low | Per-provider policy fallback if seen |
| 7 | Google callback rate limit | Very low | Low | 1M/day free tier |
| 8 | Silent-refresh missing pre-cached authority | Medium | Low | sessionStorage flow + per-flow authority resolution |
| 9 | Google-only admin locked out | Medium | Medium | §9-Q3 rec: block at middleware; sign in Entra local with same email restores access |
| 10 | MSAL `state` size limit | Very low | Low | `{flow, returnTo}` only, <2KB |
| 11 | Future SPA regression exposes guest flow on admin page | Medium over time | HIGH | Middleware gate + arch tests catch |

---

## §8 Slice ordering + registry impact

Predecessors: **OPS.M.13** + **OPS.M.14**. Independent of **OPS.M.15**. Recommended successor: OPS.M.15 or OPS.M.12.1.

M.12.1 (Microsoft consumer): small delta on M.12. Classifier already maps `live.com`; `SocialIdpValues` already contains it. Portal-add Microsoft to guest flow. Expected: 2-3 sub-commits, one session.

MASTER_PLAN row:
```
| Slice OPS.M.12 — Google social IdP + admin-vs-guest sign-in surface split + middleware admin-rejection | ✅ | commit range TBD | staging; close-out at [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md); Microsoft consumer IdP fanned out as OPS.M.12.1 |
```

---

## §9 Questions to lock BEFORE execution

### §9-Q1: Google only in M.12, OR Google + Microsoft together?
- **A (recommended): Google only.** M.12.1 fans Microsoft out later.
- B: Both together.

### §9-Q2: Linking rule when Google email matches existing Entra user
- **A (recommended): LINK by default** (M.13 Branch 2, verified-email guard).
- B: REJECT — force manual link via profile page.
- C: CONSENT prompt.

### §9-Q3: Google-federated user with pre-existing admin authority
- **A (recommended): block sign-in at middleware; SPA routes to `/auth/admin-social-idp-rejected`.**
- B: allow sign-in but strip admin roles.
- C: allow with warning banner.

### §9-Q4: Admin promotes a Google-federated user
- **A (recommended): refuse promotion at handler.** Not scoped in M.12's sub-commits — the promote endpoint doesn't exist yet post-M.13.6. Deferred to whenever the promote endpoint lands.
- B: accept, middleware gate enforces.
- C: accept as-is.

### §9-Q5: Which email is authoritative if Google and Entra differ
- **A (recommended): M.13 partial-UNIQUE remains authoritative.** DB email set at provisioning; never mutates automatically.
- B: latest-sign-in wins.
- C: user picks default.

### §9-Q6: MSAL flow selection mechanism
- **A (recommended): per-flow authority URL.**
- B: shared authority + `extraQueryParameters: { p: '<flow>' }`.
- C: distinct `client_id` per flow.

### §9-Q7: SPA admin-guard placement
- **A (recommended): new hook `useAdminGuard()` in admin subtree.**
- B: inline in `Providers.tsx` MsalProvider effect.

### §9-Q8: Migration path for existing `provider='entra'` users
- **A (recommended): NO data-heal.** M.13.4 backfill covered it. First social sign-in adds the second identity via Branch 2.

---

## §10 Approval gate — PENDING

Reviewer must lock §9 Q1-Q8 before M.12.1 lands. On sign-off, this file's Status header is updated to "APPROVED for execution."

Session budget estimate: **2 sessions.** Session 1 = M.12.1-3 (backend). Session 2 = M.12.4-7 (docs + web).

---

**End of plan. Awaiting reviewer sign-off on §9.**
