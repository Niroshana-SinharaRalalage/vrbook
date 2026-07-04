# Slice OPS.M.14 — Close-Out

**Status:** shipped end-to-end on staging.
**Slice plan:** [`OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md`](OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md).
**Predecessor:** [`OPS_M_13_CLOSE_OUT.md`](OPS_M_13_CLOSE_OUT.md) (email-canonical
users + tenant picker + X-Active-Tenant header pipeline shipped).
**Supersedes:** [`OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md`](OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md)
(F11.7.7 draft that never shipped — the mechanism carried forward but
the ordering, data-heal scope, and split into 6 sub-commits are M.14
decisions).

---

## 1. What shipped

### DevAuth production surface — DELETED

- `DevAuthHandler.cs` — entire file (options + persona enum + snapshot + handler).
- `DevAuthController` + 7 dev-bridge endpoints — deleted:
  - `GET /api/v1/dev-auth/personas`
  - `GET /api/v1/dev-auth/current-tenant`
  - `GET/POST /api/v1/dev-auth/switch`
  - `POST /api/v1/dev-auth/backdate-checked-out-at`
  - `POST /api/v1/dev-auth/persona-email`
  - `POST /api/v1/dev-auth/bootstrap-operator`
  - `POST /api/v1/dev-auth/stub-stripe-readiness`
- `SetPersonaEmailCommand` (record + handler) — deleted. This was the
  account-takeover primitive DevAuth guarded; deleting the code makes
  the primitive impossible to reintroduce via a config flip.
- `User.SetEmail(Email)` domain method — deleted. Its only caller was
  `SetPersonaEmailHandler`.

### Config surface — DELETED / RENAMED

- `DevAuth__AllowAnonymous` env var — deleted from `infra/main.bicep`
  and `.env.example`. A stray flip on a live container app can no
  longer reintroduce the synthetic-principal surface (there's no
  handler to enable).
- `DevAuth__WebBaseUrl` → `App__WebBaseUrl` — renamed. Two-phase drop
  landed in M.14.2 (fallback added) → M.14.6 (fallback removed). The
  key describes what it IS (app-side web base URL) rather than which
  now-retired handler owned it.
- `DevAuth:AllowStripeStub` — no live reader remains after
  `bootstrap-operator` + `stub-stripe-readiness` deletion. Bicep never
  set this key; nothing to remove.

### Rename — `ICurrentUser.B2CObjectId → ExternalObjectId`

- `ICurrentUser`, `AnonymousCurrentUser`, `HttpCurrentUser` — property
  renamed. JWT-claim reads unchanged (still `"oid"` then
  `NameIdentifier`).
- `PlaceBookingHandler` guest-name fallback + `PricingRuleEndpointsTests`
  reference call site updated.
- `IdentityEvents.UserOidRebound` XML doc rewrites off DevAuth
  language — event shape unchanged (no wire-format break).
- Property now describes what it IS (the identity provider's oid,
  Entra today + more via OPS.M.12) rather than which provider issued it.

### Test fixture replacement — Entra-shaped mock JwtBearer

- `tests/VrBook.Api.IntegrationTests/Auth/TestAuthHandler.cs` (NEW) —
  generic `AuthenticationHandler<TestAuthOptions>` that reads
  `X-Test-Persona` header + synthesizes Entra-shaped `ClaimsPrincipal`
  (oid, emails, email_verified, extension_isOwner, roles conditionally).
- `TwoTenantDevAuthHandler` renamed to `TwoTenantTestAuthHandler`
  (static class holding OID constants + Personas dict; class name
  keeps the vestigial "Handler" suffix so ~25 call sites migrated
  transparently).
- `TwoTenantApiFixture` + `IdentityApiFixture` — `ConfigureTestServices`
  registers `TestAuthHandler` under `JwtBearerDefaults.AuthenticationScheme`
  so `[Authorize]`-gated endpoints authenticate via the same scheme
  the real API uses.
- `IdentityFlowTests` — 8 hits rename `devAuth: true/false` →
  `authenticated: true/false`.
- `TenantClaimWiringTests` (deleted — depended on
  `/api/v1/dev-auth/current-tenant`) replaced by 4 unit-test facts in
  `HasTenantRoleUnitTests.cs` covering `HasTenantRole` semantics
  against a mocked `IHttpContextAccessor`.

### Frontend surface — DELETED

- `web/src/lib/api/devAuth.ts` (entire persona-switcher + backdate
  fetch surface) — deleted.
- `web/src/components/DevPersonaSwitcher.tsx` (the floating dev-only
  banner) — deleted.
- `web/src/lib/api/booking.ts::backdateCheckedOutAt` export — deleted.
- `web/src/app/admin/bookings/[id]/page.tsx` — trimmed of the
  devAuth-probe useEffect, devAuth state, backdate button, backdate
  modal, and the "Backdate is a dev-only shortcut" blurb.
- `web/src/app/layout.tsx` — `DevPersonaSwitcher` import + JSX removed.

### Arch tests

- `OpsM14_NoDevAuthInProductionTests.cs` (Category=Unit, 8 facts) —
  reflection + source-substring assertions locking:
    1. No production assembly defines `DevAuthHandler`.
    2. No production assembly defines `DevAuthPersonas` / `DevAuthPersona`.
    3. No production assembly defines `DevAuthOptions`.
    4. `AuthExtensions.cs` source contains no "DevAuth" substring.
    5. `IdentityController.cs` source contains no `class DevAuthController`.
    6. `Program.cs` source contains no "DevAuth" substring.
    7. `infra/main.bicep` source contains no "DevAuth" substring.
    8. `.env.example` source contains no "DevAuth" substring.
- `OpsM14_ExternalObjectIdRenameTests.cs` (Category=Unit, 3 facts) —
    1. `ICurrentUser` has no `B2CObjectId` member (reflection).
    2. `ICurrentUser` exposes `ExternalObjectId` (reflection).
    3. No `src/**.cs` file contains identifier `B2CObjectId`
       (excludes EF migration Designer + ModelSnapshot files).

EF migration source files under
`src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/`
still contain literal "DevAuth" + "B2CObjectId" in filenames + doc
blocks (Slice5b_DevAuth_Default_Tenant_Membership,
OpsM10_2_F11_7_7_RetireDevAuthPersonas,
OpsM13_UserIdentitiesAndMigrationAudit,
OpsM10_2_F11_7_6_SoftDeleteDuplicateUsers). EF migration history is
immutable; renaming any of these corrupts the
`__EFMigrationsHistory` chain. Both arch tests whitelist them.

### Correction to M.13 close-out

M.13 close-out §4 bullet 6 listed `bootstrap-operator` under
"endpoint hardening" as a P1 follow-up. Post-M.14 the
`bootstrap-operator` surface is **deleted, not hardened** — dev-bridge
operator setup is no longer a supported staging gesture. Real operator
setup now flows through the Bicep `postgresBootstrap` job that seeds
tenant + PA membership via the migration pipeline. M.13 close-out
§4 bullet 6 is superseded by this line.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `d627383` | M.14 planning | Draft plan — architect brief |
| `10b7c88` | M.14 planning | Plan approval + §9 questions locked (§2 Option A: mock JwtBearer; §4: DELETE bootstrap-operator; §5 two-phase App:WebBaseUrl rename; §6 delete memory outright; §7 hard-cut) |
| `ff79389` | M.14.1 | Test fixture replacement — mock JwtBearer + rewire both fixtures |
| `7ed485c` | M.14.1 fixup | PA persona gets Owner+Admin role claims so pre-M.15 `[Authorize(Roles="Owner,Admin")]` gate resolves |
| `7881d0e` | M.14.2 | Delete DevAuth production surface + config keys + arch tests |
| `01c2f07` | M.14.3 | `ICurrentUser.B2CObjectId → ExternalObjectId` + delete `SetPersonaEmailCommand` + delete `User.SetEmail` |
| `1d9f2b8` | M.14.4 | Retire frontend DevAuth surface |
| `8391efa` | M.14.5 | Docstring cleanup + delete `feedback_check_devauth_before_ui_handoff` memory |
| _(this commit)_ | M.14.6 | Drop `DevAuth:WebBaseUrl` fallback + this close-out doc |

**Total: 7 code sub-commits + 2 planning commits over 1 session.**
Matches the plan §1 scale (6 sub-commits + 1 fixup).

---

## 3. Where the plan diverged

### Followed the plan exactly

- Every §9 answer landed as documented (mock JwtBearer scheme, DELETE
  bootstrap-operator, hard-cut, two-phase App:WebBaseUrl, memory delete,
  no LOCAL_DEV_ENTRA_SETUP.md).
- No mid-slice scope changes.

### Not planned but shipped

- **`7ed485c` (M.14.1 fixup)** — the plan §1 did not anticipate that
  `TwoTenantDevAuthHandler` set `Owner`+`Admin` role claims
  UNCONDITIONALLY on every persona, including PlatformAdmin. My
  `TestAuthHandler` sets them conditionally from persona flags —
  cleaner but broke the pre-M.15 token-level gate for the PA persona.
  Fix: give PA persona `IsOwner=true, IsAdmin=true` in test config
  (comment explains the vestigial semantics + notes M.15 drops this
  entirely).

### Discovered mid-slice

Nothing new. Every risk the plan §7 called out (immutable EF migration
files, dead-code hazards, config-key rename) was already documented
and the plan's mitigation held.

---

## 4. What was deferred

Explicitly deferred to follow-up slices:

- **App Roles cleanup** — drop the `[Authorize(Roles="Owner,Admin")]`
  decorators and the corresponding role-claim writes in the mock
  `TestAuthHandler`. Waiting for **OPS.M.15** which was already
  scheduled to replace these decorators with `MembershipRoles`-based
  checks. Once M.15 lands, the PA persona's `IsOwner=true, IsAdmin=true`
  config in `TwoTenantTestAuthHandler` becomes dead weight and drops.
- **Social IdPs** (Google / Microsoft federation via Entra) — **OPS.M.12**.
  The `ExternalObjectId` rename + `user_identities` table already-live
  from M.13 unblock this.
- **`_pre_m13_snap` schema cleanup** — inherited from M.13 close-out §4,
  scheduled **2026-08-04**. Unchanged by M.14.
- **`@unknown.local` synthetic-email row cleanup** — inherited from
  M.13 close-out §4. Unchanged by M.14.
- **Legacy `app_tenant_id` claim writes drop** — inherited from M.13
  close-out §4, blocked by M.15.

---

## 5. Rollback runbook

M.14 is code-only + config-key-only. No data heal, no migration
side-effects, no schema mutation.

Tier-1 (revert the commits + let CD roll a new revision):
- Revert `1d9f2b8` (frontend) and CD-web rolls in < 3 min.
- Revert the API commit chain (`ff79389..8391efa` + M.14.6) and
  CD-api rolls in < 10 min.
- Bicep `DevAuth__AllowAnonymous` env var — the revert restores it;
  Container Apps re-attaches it on next revision.

The only thing that can't be reverted is the `_M14.6_App_WebBaseUrl`
notification handler config-key path — but the fallback that was in
M.14.2 → M.14.6 restores it if needed. Post-revert, the notification
handler falls through to `"https://example.com"` for review deep links
which is the same failure mode as an unset `DevAuth__WebBaseUrl`.

No rollback rehearsal required. The deletions have no runtime state
to preserve.

---

## 6. Staging walk verification

Post-M.14 the staging walk is unchanged from M.13 — every path was
already Entra-only. No new UI to test. Two verifications for
regression safety:

- **`GET /api/v1/dev-auth/personas`** — 404 (endpoint deleted, was
  404 pre-M.14 too because `DevAuth__AllowAnonymous=false` on staging;
  M.14 makes the deletion permanent).
- **`POST /api/v1/dev-auth/bootstrap-operator`** — 404 (endpoint
  deleted). Previously 404 via config flag; now 404 because there's
  no endpoint to route to.
- **Real Entra sign-in flow** — unchanged from M.13.

No behavioural change for real users. This slice is a security
hardening + code-hygiene pass.

---

## 7. Prod cutover checklist

M.14 is prod-safe on merge to `main`. No data heal, no runtime
migration, no state carried between revisions. Bicep env-var change
takes effect on next Container App revision.

Pre-merge sanity check (not blocking):

1. Confirm `DevAuth__AllowAnonymous` env var reads `false` on prod
   Container App (should already be the case per M.13 close-out).
2. Confirm no other config key in prod Key Vault references
   `DevAuth:*`.
3. Verify prod Bicep template applies cleanly (`bicep build` +
   `az deployment group validate`).

The prod deployment window is any low-traffic hour — the switchover
is seamless from a user perspective (they were already using Entra;
they lose an endpoint they never had access to).

---

## 8. Session debt

Nothing outstanding from this slice. The plan §7 hard-cut order held
throughout; each sub-commit was CI-green before the next one started;
architect consultation happened up-front (plan approval commit
`10b7c88`) and no re-consult was needed mid-slice.
