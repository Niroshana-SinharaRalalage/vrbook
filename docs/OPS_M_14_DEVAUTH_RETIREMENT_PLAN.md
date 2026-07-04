# Slice OPS.M.14 — DevAuth retirement (handler + endpoints + config surface + fixture rewire)

- **Status:** APPROVED for execution. Reviewer sign-off received 2026-07-04. All §9 questions locked — see §9 for the final answers.
- **Date:** 2026-07-04.
- **Author (role):** Platform Enterprise Architect.
- **Predecessors:** [`OPS_M_13_CLOSE_OUT.md`](./OPS_M_13_CLOSE_OUT.md) (shipping baseline),
  [`OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md`](./OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md)
  (F11.7.7 draft that never shipped — much of the mechanism carries forward).
- **Scope:** ONE vertical slice. Retires the entire DevAuth surface — production
  handler + persona machinery + dev-bridge endpoints + config flags + web
  helpers + integration-test fixtures — and lands a mock-JwtBearer test
  authentication handler as the replacement. Bundles the
  `ICurrentUser.B2CObjectId → ExternalObjectId` rename and the
  `SetPersonaEmailCommand` deletion since both are load-bearing on DevAuth.
- **Explicitly NOT in this slice:**
  - App-role legacy claim reads / `[Authorize(Roles="Owner,Admin")]` drop — **OPS.M.15**.
  - Social IdPs (Google / Microsoft federation through Entra) — **OPS.M.12**.
  - `_pre_m13_snap` schema cleanup — scheduled 2026-08-04 follow-up.
  - `@unknown.local` synthetic-email row cleanup — maintenance follow-up.

---

## §0 What we're doing + why now

Post-M.13, the DevAuth handler + `[HttpPost("bootstrap-operator")]` +
`persona-email` + `switch` + `stub-stripe-readiness` + `backdate-checked-out-at`
+ `personas` + `current-tenant` endpoints are all **dead weight**. Every path
into the API from a real user now flows through JwtBearer against Entra
External ID (per [`OPS_M_13_CLOSE_OUT.md`](./OPS_M_13_CLOSE_OUT.md) §1). DevAuth
existed to unblock local dev and pre-Entra staging walks; both use cases are
now covered by real Entra sign-in.

The **cost of keeping DevAuth is a permanent tail-risk security surface**.
The F8-era Production guard (`IHostEnvironment.IsProduction()` in every
dev-bridge endpoint) is the only thing keeping a staging config flip from
becoming an account-takeover primitive (`SetPersonaEmailCommand` rewrites any
user's email by oid — see `SetPersonaEmailCommand.cs:37-46` comment). Every
month DevAuth stays alive is another month where a stray env-var flip on the
staging container app (`az containerapp update --set-env-vars
DevAuth__AllowAnonymous=true` — the exact operator gesture the F11 walks
used) reintroduces the full DevAuth attack surface. Retire it while the code
is fresh.

**Ordering:** the F11.7.7 draft plan (superseded here) was designed to run
BEFORE M.13.3 — it depended on the multi-row-per-email data-heal shape. M.13.4
subsumed both the multi-row heal AND the DevAuth persona soft-delete
(`20260701030000_OpsM10_2_F11_7_7_RetireDevAuthPersonas.cs` shipped as part of
M.13.3 setup; the M.13.4 backfill collapsed the persona rows onto the
niroshanaks survivor). **This means M.14 has ZERO data-heal work.** The
retirement is code + config + fixture-rewire only — see §7.

### §0.1 One correction to M.13 close-out

The close-out doc §4 lists `bootstrap-operator` under "endpoint hardening" as
a P1 follow-up. Post-this-plan §4, the bootstrap-operator surface is
**deleted, not hardened** — see §4 for the decision. Update the close-out's
bullet #6 in M.14.6.

The user's problem statement also lists `TenantIdRolloutFixture.cs` under
"Test fixtures" that need rewiring. Verified false — that fixture is a
migration-only test harness with no DevAuth wiring
(`tests\VrBook.Api.IntegrationTests\Identity\TenantIdRolloutFixture.cs:1-68`,
zero DevAuth references). Excluded from M.14 scope.

---

## §1 What ships in each sub-commit

Sub-commit convention: `Slice OPS.M.14.N: <summary>`. All commits target
`develop`. Each ends CI-green under the standard filter
`dotnet test --filter "Category!=Integration"`. Sequence:

```
M.14.1 — GREEN. Test fixture replacement — mock JwtBearer handler + rewire both fixtures.
    Files (test-only):
    - tests/VrBook.Api.IntegrationTests/Auth/TestAuthHandler.cs (NEW)
      * See §5 for the shape. Reads a per-test claims-set from a
        subclass of AuthenticationSchemeOptions.
    - tests/VrBook.Api.IntegrationTests/Auth/TestPersona.cs (NEW)
      * record TestPersona(string Oid, string Email, string DisplayName,
        bool IsPlatformAdmin, IReadOnlyList<string> ExtensionRoles).
    - tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs
      * DELETE `bool DevAuthEnabled { get; set; }`.
      * DELETE `DevAuth:*` keys from ConfigureAppConfiguration.
      * ADD ConfigureTestServices that registers TestAuthHandler under
        JwtBearerDefaults.AuthenticationScheme (see §5 for why).
      * RENAME CreateClientWith(bool devAuth) → CreateClientWith(bool authenticated).
        - authenticated=true → sets Authorization: Bearer test AND
          per-request stamps the persona via header.
    - tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantDevAuthHandler.cs
      → RENAME to TwoTenantTestAuthHandler.cs; content rewrite to read
        X-Test-Persona header (not cookie). Same OID constants preserved
        so downstream test seeds don't renumber.
    - tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs
      * DELETE `DevAuth:AllowAnonymous` config key.
      * ConfigureTestServices — register TwoTenantTestAuthHandler under
        JwtBearerDefaults.AuthenticationScheme.
      * CreateClientAs(string? persona) — set X-Test-Persona: {persona}
        header. Cookie plumbing DELETED. Signature unchanged.
      * Update seed to reference TwoTenantTestAuthHandler.Owner{A,B,PA}Oid.
    - tests/VrBook.Api.IntegrationTests/IdentityFlowTests.cs — 8 hits: rename
      `devAuth: true/false` → `authenticated: true/false`. Assertion bodies
      unchanged.
    - tests/VrBook.Api.IntegrationTests/Identity/TenantClaimWiringTests.cs
      * 4 hits — rename devAuth → authenticated.
      * The 4 direct calls to /api/v1/dev-auth/current-tenant flip to
        inspecting `GET /api/v1/me` + a new HttpCurrentUser unit-test file
        (below) that covers HasTenantRole(defaultTenant,...) semantics
        that the diagnostic endpoint used to answer.
    - tests/VrBook.Modules.Identity.Tests/HasTenantRoleTests.cs (NEW — Unit)
      * 4 facts: matching tenant+role → true; wrong tenant → false;
        wrong role → false; null-tenant claim → false. Constructed
        directly against HttpCurrentUser + a mock IHttpContextAccessor.
    - tests/VrBook.Api.IntegrationTests/Multitenancy/JwtSmokeTests.cs
      * 2 hits: update the docstring citing DevAuth; the type reference
        (TwoTenantTestAuthHandler.OwnerAOid) is post-rename.
    - tests/VrBook.Api.IntegrationTests/Multitenancy/TenantAuthorizationBehaviorTests.cs
      * 1 hit — docstring edit.
    - tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixtureTests.cs
      * 4 hits — CreateClientAs("...") calls unchanged in shape;
        remove any DevAuth-cookie assertion if present (grep-verify).
    - tests/VrBook.Api.IntegrationTests/Multitenancy/CarveOutAppLayerTests.cs
      + CrossTenantEndpointMatrix.cs + PlatformAdminBypassFactPack.cs +
      CrossTenantRejectionAuditFactPack.cs + PlatformAdminPromoteRevokeSmokeTest.cs
      * ZERO source changes — the fixture surface (CreateClientAs) preserved
        the persona-string contract. 24+ call sites migrate transparently.
    Tests moved to GREEN:
    - Every existing IdentityFlowTests + TenantClaimWiringTests + Multitenancy
      test using CreateClientAs(persona) — pass under the mock JwtBearer path.
    - NEW HasTenantRoleTests × 4.
    Production code changes: ZERO. This is a test-only commit.
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green. Full multi-tenancy fact pack passes under the
    new handler.

M.14.2 — GREEN. Delete DevAuth production surface + config keys.
    Files (production):
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs
      → DELETE FILE (DevAuthOptions + DevAuthPersona + DevAuthPersonas + DevAuthHandler).
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs
      * DELETE `devAuthEnabled` variable (line 29) + `defaultScheme` ternary
        (30-32) → replace with `services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`.
      * DELETE the `if (devAuthEnabled) { auth.AddScheme<DevAuthOptions, DevAuthHandler>(...) }`
        block (lines 110-118).
      * Rewrite the class docstring (lines 10-16) — remove DevAuth
        references; describe JwtBearer-only wiring.
    - src/VrBook.Api/Controllers/IdentityController.cs
      * DELETE the entire `DevAuthController` class (lines 82-587):
        - GET /api/v1/dev-auth/personas
        - GET /api/v1/dev-auth/current-tenant
        - GET/POST /api/v1/dev-auth/switch
        - POST /api/v1/dev-auth/backdate-checked-out-at
        - POST /api/v1/dev-auth/persona-email
        - POST /api/v1/dev-auth/bootstrap-operator
        - POST /api/v1/dev-auth/stub-stripe-readiness
      * DELETE trailing records `StubStripeReadinessRequest` +
        `BootstrapOperatorRequest`.
      * KEEP `IdentityController` (lines 18-79) untouched — that's the real
        `/api/v1/me` surface.
    - src/VrBook.Api/Program.cs (line 92-94 comment)
      * Rewrite the "// Real AD B2C JWT bearer when AzureAdB2C:* is
        configured; falls through to the synthetic DevAuth principal..."
        comment. Now: "// JwtBearer against Entra External ID per ADR-0012.
        DevAuth retired in OPS.M.14."
    - src/Modules/VrBook.Modules.Notifications/Application/Handlers/BookingNotificationHandlers.cs
      * Line 89 reads `configuration["DevAuth:WebBaseUrl"]` for review deep
        link email. This is a PRODUCTION path (survives DevAuth
        retirement). RENAME the config key to `App:WebBaseUrl`. Fallback
        chain adds `"DevAuth:WebBaseUrl"` for one deploy cycle so a
        deployed API keeps working during the rolling replace; drop the
        legacy key in M.14.6 after Bicep is updated. See §7 for the
        rename-then-drop sequence.
    - infra/main.bicep:
      * DELETE env var `DevAuth__AllowAnonymous` (line 272 + surrounding comment).
      * RENAME env var `DevAuth__WebBaseUrl` → `App__WebBaseUrl` (line 275).
        The value shape is unchanged (dev-only web base URL, empty in
        staging + prod). Post-M.14 no `DevAuth__*` env var exists.
    - .env.example
      * DELETE `DevAuth__AllowAnonymous=true` (line 42) + surrounding comment.
    - .github/workflows/cd-staging-api.yml
      * Update comments at lines 427-428 + 502-508 to reference the M.14
        retirement doc + the Entra-only baseline. Assertion behavior
        unchanged (staging already 401s pre-M.14; M.14 preserves that).
    Tests moved to GREEN:
    - NEW tests/VrBook.Architecture.Tests/OpsM14_NoDevAuthInProductionTests.cs
      (Category=Unit) — 8 facts:
        1. No production assembly defines a DevAuthHandler type.
        2. No production assembly defines DevAuthPersonas type.
        3. No production assembly defines DevAuthOptions type.
        4. AuthExtensions source text contains NO "DevAuth" substring.
        5. IdentityController source text contains NO `class DevAuthController`.
        6. Program.cs source text contains NO "DevAuth" substring.
        7. infra/main.bicep source text contains NO "DevAuth" substring.
        8. .env.example source text contains NO "DevAuth" substring.
      Written RED-first as a single commit alongside the deletions so the
      TDD pair lands atomic. (F11.7.7-old plan split RED/GREEN across two
      commits; here we bundle because the deletions ARE the fix — no
      benefit to a stand-alone RED.)
    Local validation:
    - `dotnet test --filter "Category!=Integration"` — green.
    - `dotnet build src/VrBook.Api` — green.
    - `bicep build infra/main.bicep` — green.
    - Full grep sweep: `Grep pattern="DevAuth" glob="src/**/*.cs"` — 0 hits
      excluding immutable migration Designer.cs snapshots.
    CI expectation: green.

M.14.3 — GREEN. ICurrentUser.B2CObjectId → ExternalObjectId rename +
    delete SetPersonaEmailCommand + delete User.SetEmail.
    Files (production):
    - src/VrBook.Contracts/Interfaces/ICurrentUser.cs (line 12-13)
      * `string? B2CObjectId { get; }` → `string? ExternalObjectId { get; }`.
      * Update XML doc comment (drop "B2C" wording; describe as
        current identity provider's oid — Entra today, more via M.12).
    - src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs (line 15)
      * Rename property.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs (line 94-96)
      * Rename property. Body unchanged — still reads "oid" then
        NameIdentifier claims.
    - src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs (line 90)
      * `currentUser.Email ?? currentUser.B2CObjectId ?? "Guest"` →
        `currentUser.Email ?? currentUser.ExternalObjectId ?? "Guest"`.
    - src/VrBook.Contracts/Events/IdentityEvents.cs (line 15-23)
      * `UserOidRebound(Guid UserId, string OldOid, string NewOid)` — property
        NAMES already generic ("OldOid" / "NewOid"). Update the XML doc
        comment (lines 15-22) which says "B2CObjectId" — drop that word.
        Event shape unchanged; no wire-format break.
    - src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs
      → DELETE FILE (record + handler).
    - src/Modules/VrBook.Modules.Identity/Domain/User.cs (lines 79-88)
      * DELETE `public void SetEmail(Email newEmail)` — only caller was
        `SetPersonaEmailHandler` (deleted). Grep-verified: no other callers.
    - tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleEndpointsTests.cs (line 43)
      * Rename `user.B2CObjectId.Returns(OwnerB2C)` →
        `user.ExternalObjectId.Returns(OwnerB2C)`. Local const `OwnerB2C`
        can keep its name (test-local; not a symbol on the API).
    - tests/VrBook.Architecture.Tests/OpsM13_EmailCanonicalUsersShapeTests.cs
      (lines 20-23, 47-52)
      * The existing arch-facts assert the `User` aggregate has no
        `B2CObjectId` property (that's DB-side, already dropped in M.13.4).
        No change — this test still passes; it's asserting the *aggregate*
        property, not `ICurrentUser`.
    Tests moved to GREEN:
    - NEW tests/VrBook.Architecture.Tests/OpsM14_ExternalObjectIdRenameTests.cs
      (Category=Unit) — 3 facts:
        1. ICurrentUser has no B2CObjectId member (reflection).
        2. ICurrentUser has ExternalObjectId member.
        3. No `src/` source file contains identifier `B2CObjectId`
           (excluding migration Designer.cs snapshots).
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green.

M.14.4 — GREEN. Retire frontend DevAuth surface.
    Files:
    - web/src/lib/api/devAuth.ts → DELETE.
    - web/src/components/DevPersonaSwitcher.tsx → DELETE.
    - web/src/app/layout.tsx (lines 5, 48 per grep)
      * Remove `import { DevPersonaSwitcher }` + the JSX render.
    - web/src/lib/api/booking.ts (lines 186-193)
      * DELETE `backdateCheckedOutAt` export.
    - web/src/app/admin/bookings/[id]/page.tsx:
      * Remove `import { backdateCheckedOutAt }` (line 11).
      * Remove `import { getDevPersonas }` (line 20).
      * Remove `const [devAuth, setDevAuth] = useState(false)` + the effect
        that probes `getDevPersonas` (lines 67-82).
      * Remove `{devAuth && ...}` gates (lines 257, 283) and the entire
        "Backdate CheckedOutAt" button + modal block (lines 397-406,
        410-412).
      * Remove `await backdateCheckedOutAt(...)` (line 185) + surrounding
        error-state (`backdateOpen`, `backdateError`, setters).
    - web/src/lib/api/client.ts (line 149)
      * Comment edit — drop "DevAuth `vrbook-dev-persona` cookie" wording;
        `credentials: 'include'` still applies (MSAL cookies + Entra
        session).
    - web/src/components/layout/SiteHeaderAuth.tsx (line 11)
      * Docstring edit — drop DevAuth persona-switcher mention.
    Tests:
    - Update or delete any Vitest tests that exercise the backdate modal.
      (Grep-verify: `grep -r "backdateCheckedOutAt\|DevPersonaSwitcher"
      web/src/**/*.test.*` → address each hit.)
    - No new tests. Vitest arch fact: NEW
      `web/src/__tests__/no-devauth-imports.test.ts` (matching the src
      arch pattern) — scans `web/src/` for the strings `devAuth`, `DevAuth`,
      `dev-auth` — expect 0 hits post-M.14.4.
    Local validation: `cd web && npm run build && npm test`.
    CI expectation: `cd-staging-web.yml` green.

M.14.5 — GREEN. Retire ancillary references + memory update.
    Files:
    - Kill remaining docstring hits flagged by
      `Grep pattern="DevAuth" glob="src/**/*.cs"` (see §6 for the full
      list — 6 residual docstring mentions):
        · src/VrBook.Api/Common/ExemptFromCrossTenantMatrixAttribute.cs:10
          "DevAuth-only diagnostic" example → replace with
          "platform-admin promote handoff" (real production example).
        · src/Modules/VrBook.Modules.Payment/Application/Queries/GetPaymentIntentForBookingQuery.cs:36
          "signed in via DevAuth/Entra" → "signed in via Entra".
        · Rest of the residual comment hits documented in §6.
    - Delete memory file `feedback_check_devauth_before_ui_handoff.md`
      **outright** (per §9 Q6 reviewer decision — not rewritten in place).
      The memory is superseded by the "handoff URLs go through real Entra
      sign-in + `/select-tenant`" pattern established in M.13.5. The old
      rationale is preserved in git history + this plan doc; the memory
      file itself no longer surfaces as guidance to future sessions.
    Tests:
    - Extend OpsM14_NoDevAuthInProductionTests to also fail on the
      `src/**` docstring hits (case-sensitive `DevAuth` substring in
      any `.cs` file, excluding `**/Migrations/**`).
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green.

M.14.6 — GREEN. Doc close-out + Bicep second-pass cleanup.
    Files:
    - docs/OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md (this file) — mark §11 as
      shipped; log sub-commit chronology.
    - docs/OPS_M_13_CLOSE_OUT.md — update §4 bullet #6:
      "**bootstrap-operator endpoint** — deleted in M.14 (not hardened).
      Operator setup post-M.14 goes through direct SQL migrations (as
      proven during the M.13.6 walk-debug) or the OPS.M.8 promote runbook."
    - src/Modules/VrBook.Modules.Notifications/Application/Handlers/BookingNotificationHandlers.cs
      * Now that the M.14.2 rename cycle has been through a deploy
        (App:WebBaseUrl in prod config for one week minimum), drop the
        legacy `DevAuth:WebBaseUrl` fallback. Grep confirms only the
        BookingNotificationHandlers.cs:89 chain reads it.
    - infra/main.bicep — verify `App__WebBaseUrl` alone remains; no
      `DevAuth__*` env var lingers.
    - .env.example — verify no `DevAuth__*` keys.
    - docs/dev/LOCAL_DEV_ENTRA_SETUP.md — NOT created per §9 Q1 (reviewer
      decision). Local-dev flow relies on individual Entra accounts against
      the same tenant staging uses; no shared dev-tenant credentials
      needed. `dotnet test` covers backend-only iteration.
    - docs/OPS_M_2_PLAN.md, docs/OPS_M_7_PLAN.md, docs/OPS_M_8_PLAN.md,
      docs/OPS_M_10_PLAN.md, docs/OPS_M_10_2_F11_*.md,
      docs/OPS_M_9_1_GUEST_RESOLVER_PLAN.md, docs/MASTER_PLAN.md,
      docs/MULTI_TENANCY_OPS_PLAN.md, docs/OPS_M_12_SOCIAL_IDPS_PLAN.md,
      docs/identity/README.md, docs/identity/roles-architecture.md,
      docs/SLICE5_PLAN.md, docs/SLICE6_PLAN.md, docs/OtherDetails.md
      → prepend a top note: "Slice OPS.M.14 retired all DevAuth surfaces
      referenced below on 2026-07-XX. Historical context retained; see
      OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md."
      Doc-only edit; do NOT rewrite the history text itself.
    - docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md — append §7
      documenting the "post-M.14, first PA per env" flow. Recommendation
      is the direct-SQL migration path — see §4.
    Doc-only commit. Per memory `feedback_no_ci_for_doc_only_commits`,
    do NOT `gh run watch`. The previous code-commit (M.14.5) is the CI
    gate.
```

**Sub-commit count: 6.** No RED-only commit; each sub-commit ships tests +
code atomically.

---

## §2 Test fixture replacement design

Load-bearing decision. Three options:

| Option | Mechanism | Pro | Con |
|---|---|---|---|
| **A. Mock JwtBearer scheme** — `TestAuthHandler : AuthenticationHandler<TestAuthOptions>` registered under `JwtBearerDefaults.AuthenticationScheme` via `ConfigureTestServices`, reads persona from an `X-Test-Persona` header + a per-fixture persona dictionary | Handler runs INSIDE the same auth pipeline production uses (default scheme, `[Authorize]` decorator, JwtBearer challenge shape). `UserProvisioningMiddleware`, `HasBearer` observability, `TenantAuthorizationBehavior` all exercised end-to-end. No new production seam. | Slightly more verbose fixture setup (one `AddAuthentication(...).AddScheme<>(...)` line per fixture). |
| **B. Bypass-middleware — stamp `HttpContext.Items` directly** — a test middleware pre-populates `Items[AppUserIdItemKey]` etc. and `AuthenticateAsync` returns success unconditionally | Fastest to write; no header plumbing. | Skips the auth pipeline entirely; a regression that only fails INSIDE `HandleAuthenticateAsync` (e.g. an events-callback bug on the JwtBearer scheme itself, or a `UserProvisioningMiddleware` claim-read bug) would not be caught by any test that used this shape. The M.13 silent-401 debug cycle is exactly the kind of failure this fixture would miss. |
| **C. Real JWT minter** — test project holds an RSA private key; `WebApplicationFactory` config trusts a test issuer; each test constructs a real JWT | Highest fidelity — literal JwtBearer code path exercised. | Requires a **production-code trust seam** (a `ValidIssuers` addition gated on `IsDevelopment()` or a build-time constant). That production seam exists only for tests → smell + auth-critical review burden every time it changes. RSA key ceremony in test setup. |

**Decision: Option A (mock JwtBearer scheme).**

Rationale:
1. M.13's 4+ hour silent-401 debug (per `OPS_M_13_CLOSE_OUT.md` §8 root cause
   #1) validates the value of exercising the actual auth pipeline in
   integration tests. Option B skips it; Option C is overkill.
2. Zero production-code seam. The `TestAuthHandler` symbol never appears in
   `src/`. Enforced by an arch test (M.14.2 fact #4 — `AuthExtensions`
   source contains no test-only handler references).
3. `CreateClientAs(persona)` public surface preserved verbatim → 24+ test call
   sites migrate transparently. Only 8 hits in `IdentityFlowTests` need the
   `devAuth: bool` → `authenticated: bool` rename.
4. `JwtSmokeTests` (the existing 3-fact pack including 1 skipped scaffold at
   line 71) keeps its shape. The scaffold's future path — real JWT trust
   seam gated in a follow-up ops slice — is orthogonal to M.14.

**Why register under `JwtBearerDefaults.AuthenticationScheme` (not `"TestAuth"`
as the F11.7.7-old plan proposed):** the production `[Authorize]` attribute has
no explicit scheme, so ASP.NET uses the default authenticate scheme
(`JwtBearer`). If the test handler registers under a different scheme name,
every test's HTTP call would flow through the un-overridden production
JwtBearer scheme (which then 401s because there's no real token). Only by
replacing the JwtBearer scheme's handler-type via
`services.AddAuthentication(JwtBearer).AddScheme<...>(JwtBearer, ...)` in
`ConfigureTestServices` (which runs AFTER `AddVrBookAuthentication`) does the
handler-type override take effect. See §5 for the exact wiring.

---

## §3 `[Authorize(Roles="Owner,Admin")]` decision

**Question:** post-M.13.6 the middleware retained
`[Authorize(Roles="Owner,Admin")]` on `IdentityController.GetTenant` (line 55).
Should M.14 drop it, or is that M.15?

**Recommendation: keep in M.14; drop in M.15.**

Rationale:
- The `Owner` / `Admin` role claim writes come from the JWT's
  `extension_isOwner` + `extension_isAdmin` claims (via `HttpCurrentUser.IsOwner`
  reading `ReadBoolClaim(OwnerClaim)`) + the DevAuth handler's `ClaimTypes.Role`
  writes for `Owner` + `Admin`. Post-M.14 the DevAuth writes are gone, but the
  Entra token claims still populate the role.
- Dropping the decorator without also dropping the underlying Entra claim
  read (`IsOwner` / `IsAdmin` in `HttpCurrentUser`) leaves a semantic ambiguity
  the reader hits: "Roles are read but never enforced." Cleaner to bundle both
  drops into a single App-Roles cleanup slice (M.15) where the migration to
  `MembershipRoles`-based decisions is the ONE thing that ships.
- M.14 stays scoped to "delete DevAuth + rewire tests." Bundling the
  role-decorator drop expands surface (every `[Authorize(Roles="...")]` site
  on every controller needs an audit), invites overrun.

**Target state for M.14:** `[Authorize(Roles="Owner,Admin")]` on `GetTenant`
remains. It's satisfied by the token-level `Owner`/`Admin` claims Entra emits.
The `TwoTenantTestAuthHandler` also writes these claims (already does, per
`TwoTenantDevAuthHandler.cs:77-78` — carried forward). No behavior change.

**Target state for M.15:** replace with `[Authorize]` + a
`RequireMembershipRoleAttribute` (or an in-handler
`currentUser.HasTenantRole(tid, "tenant_admin")` guard) — the shape locked in
M.13.6's `MembershipRoles` dictionary.

---

## §4 `bootstrap-operator` fate

Three options:

| Option | Mechanism | Pro | Con |
|---|---|---|---|
| **A. Delete outright** | Endpoint gone; operator setup post-M.14 goes through direct SQL migrations (per M.13.6 walk debug) or the M.8 promote runbook. | Zero code path to secure. Matches the "post-M.13, only real auth is Entra" invariant. | Every new environment needs a hand-crafted "bootstrap PA" migration OR a documented manual `az postgres` SQL step. |
| **B. Convert to `[Authorize(Roles="PlatformAdmin")]` real endpoint** | Endpoint stays, guards become "signed-in PA can add members". | Reusable for future add-tenant flows. | Chicken-and-egg: no PA exists on a fresh env; the guard blocks the very first call. Also requires the `ProblemDetails-strips-body` M.13.6 pain (memory `reference_problem_details_strips_body`) to be fixed first for any useful error surface. |
| **C. Startup-time `IHostedService`** — reads `Ops:InitialPlatformAdminEmail` from config, idempotently promotes on each boot | Zero manual steps per env; the F11.7.7-old plan's chosen shape. | Adds a new auth-critical config knob. Anyone with Key Vault write can flip the initial-PA email and self-promote on next boot. Same-mitigation-as-K-V-access argument, but new surface. |

**Recommendation: Option A (delete outright).**

Rationale:
- The M.13.6 walk-debug already proved direct-SQL migrations work
  (`20260704032522_OpsM13_BootstrapPlatformAdminForNiroshanaksEmail.cs`
  shipped as the fix; 3 lines of SQL, idempotent, auditable via
  `identity.migration_audit`). Prod already has one PA; staging already has
  one PA. New environments (a hypothetical "prod-2" or ephemeral prod-scale
  test env) get a one-shot migration authored per-env.
- The F11.7.7-old plan proposed Option C (`InitialPlatformAdminPromoter`
  `IHostedService`). It's a viable shape but WIDENS the auth-critical surface
  by adding a config key that survives every deploy. Post-M.13 the real
  Entra sign-in flow is stable; there is no operational need for a
  boot-time re-promote.
- The M.13.6 walk-debug also demonstrated the failure mode of
  `bootstrap-operator`-shaped endpoints — 7 wasted CI cycles diagnosing a
  ProblemDetails-swallowed 404 through 3 layered guards. Deleting the shape
  removes the failure mode entirely.
- Runbook update in M.14.6: extend
  `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` §7 with a "How to grant
  first-PA to a fresh environment" section — the recipe is:
    1. Deploy code.
    2. Have the operator sign in via Entra (creates the `identity.users` row).
    3. Author a one-shot EF migration mirroring
       `OpsM13_BootstrapPlatformAdminForNiroshanaksEmail.cs`, parameterized
       on the operator's email. 20 lines of SQL.
    4. Deploy migration. Verify via `GET /api/v1/me` returning
       `IsPlatformAdmin=true`.
- This preserves the "three-named-humans manual audit" property for
  prod's first PA that OPS.M.8 already enforces.

---

## §5 Test fixture replacement — code sketch

Concrete target shape for M.14.1. Illustrates the JwtBearer-scheme override
pattern from §2 Option A.

```csharp
// tests/VrBook.Api.IntegrationTests/Auth/TestAuthHandler.cs (NEW)

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VrBook.Api.IntegrationTests.Auth;

public sealed class TestAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>Persona lookup — header value → snapshot. Fixture-owned.</summary>
    public required IReadOnlyDictionary<string, TestPersona> Personas { get; init; }
}

public sealed record TestPersona(
    string Oid,
    string Email,
    string DisplayName,
    bool IsOwner,
    bool IsAdmin);

/// <summary>
/// Test-only replacement for JwtBearer. Registered via
/// ConfigureTestServices under JwtBearerDefaults.AuthenticationScheme
/// so [Authorize] decorators route through it exactly as production does.
/// Reads X-Test-Persona header; unrecognised value → NoResult (401 from
/// pipeline). Absent Authorization: Bearer header → NoResult.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string PersonaHeader = "X-Test-Persona";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Require a Bearer header so tests exercise the same "no token → 401"
        // path production sees. Anonymous test callers set NO Authorization
        // header and get NoResult (which the pipeline turns into 401 for
        // [Authorize] endpoints, 200 for [AllowAnonymous]).
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var personaKey = Request.Headers[PersonaHeader].ToString();
        if (string.IsNullOrEmpty(personaKey)
            || !Options.Personas.TryGetValue(personaKey, out var p))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Emit the SAME claim shape production Entra tokens carry so
        // UserProvisioningMiddleware + HttpCurrentUser resolve identically.
        var claims = new List<Claim>
        {
            new("oid", p.Oid),
            new(ClaimTypes.NameIdentifier, p.Oid),
            new(ClaimTypes.Name, p.DisplayName),
            new("name", p.DisplayName),
            new(ClaimTypes.Email, p.Email),
            new("emails", p.Email),
            new("email_verified", "true"),
            new("extension_isOwner", p.IsOwner ? "true" : "false"),
            new("extension_isAdmin", p.IsAdmin ? "true" : "false"),
        };
        if (p.IsOwner) claims.Add(new(ClaimTypes.Role, "Owner"));
        if (p.IsAdmin) claims.Add(new(ClaimTypes.Role, "Admin"));

        var identity = new ClaimsIdentity(claims, "TestAuth",
            ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            JwtBearerDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

Fixture wire-up sketch (TwoTenantApiFixture — IdentityApiFixture mirrors):

```csharp
// tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs
// ConfigureWebHost — the parts that change in M.14.1:

builder.ConfigureAppConfiguration((_, cfg) =>
{
    cfg.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Postgres"] = ConnectionString,
        ["ConnectionStrings:Redis"] = string.Empty,
        ["EntraExternalId:Instance"] = string.Empty,
        ["EntraExternalId:TenantId"] = string.Empty,
        ["EntraExternalId:ClientId"] = string.Empty,
        // NOTE: DevAuth:AllowAnonymous key DELETED. Entra keys stay blank
        // so AuthExtensions falls through to "no JwtBearer registered" —
        // then ConfigureTestServices below adds the TestAuthHandler as
        // the JwtBearer scheme's handler-type.
        ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
    });
});

builder.ConfigureTestServices(services =>
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddScheme<TestAuthOptions, TestAuthHandler>(
            JwtBearerDefaults.AuthenticationScheme,
            opts =>
            {
                opts.Personas = new Dictionary<string, TestPersona>
                {
                    ["OwnerA"] = new(
                        TwoTenantTestAuthHandler.OwnerAOid,
                        "owner-a@vrbook.test", "Owner A", true, true),
                    ["OwnerB"] = new(
                        TwoTenantTestAuthHandler.OwnerBOid,
                        "owner-b@vrbook.test", "Owner B", true, true),
                    ["PlatformAdmin"] = new(
                        TwoTenantTestAuthHandler.PlatformAdminOid,
                        "platform-admin@vrbook.test", "Platform Admin",
                        false, false),
                };
            });
});

public HttpClient CreateClientAs(string? persona)
{
    var client = CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
    });
    // Bearer stays fake — TestAuthHandler doesn't validate token content,
    // only the Bearer scheme prefix presence. This mirrors production's
    // "no bearer = 401" behavior for AllowAnonymous coverage.
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", "test");
    if (!string.IsNullOrEmpty(persona))
    {
        client.DefaultRequestHeaders.Add(
            TestAuthHandler.PersonaHeader, persona);
    }
    return client;
}
```

Key wiring notes:
- `services.AddAuthentication(...)` in `ConfigureTestServices` REPLACES the
  default authentication scheme + associated handler-type. Any prior
  registration (from `AddVrBookAuthentication`) is overwritten. Since the
  Entra config keys are blank, no real JwtBearer registration happens
  upstream — the test scheme is unopposed.
- The persona dictionary is passed via `TestAuthOptions.Personas` (init-only,
  per-fixture) so tests share a single handler type but each fixture wires
  its own persona set.
- `TwoTenantDevAuthHandler` was reading a cookie; `TestAuthHandler` reads a
  header. Rename covered in M.14.1.
- The bearer prefix requirement is a deliberate integration-test invariant:
  it forces every test that expects auth to attach `Authorization: Bearer`
  header. Anonymous tests omit it — behaves same as production.

---

## §6 What breaks + how many tests need updating

Grep-verified counts (2026-07-04):

**Test-file hits by pattern:**

| Pattern | Files | Hits |
|---|---|---|
| `CreateClientAs\|CreateClientWith\|DevAuthEnabled` | 11 | 42 |
| `CreateClientAs` alone | 7 | 25 |
| `CreateClientWith\|DevAuthEnabled` | 3 | 15 |
| `vrbook-dev-persona` | 3 | 5 |
| `DevAuthHandler\|DevAuthPersonas\|DevAuthOptions` | 4 | 12 |

**Grouping by rewrite class:**

- **Trivial call-site rename (`devAuth: bool` → `authenticated: bool`)** — 3
  files (`IdentityFlowTests.cs`, `IdentityApiFixture.cs`, `TenantClaimWiringTests.cs`).
  15 total hits. Sed-style rename.
- **`/dev-auth/current-tenant` direct-URL hits** — 1 file
  (`TenantClaimWiringTests.cs`, 4 calls at lines 36/57/75/97). Replace with
  `/api/v1/me` + assertions against `HttpCurrentUser` semantics that move
  to `HasTenantRoleTests.cs`. 4 rewrite sites.
- **`CreateClientAs("OwnerA"|"OwnerB"|"PlatformAdmin")` fixture callers** —
  7 files (`CarveOutAppLayerTests`, `CrossTenantEndpointMatrix`,
  `PlatformAdminBypassFactPack`, `CrossTenantRejectionAuditFactPack`,
  `PlatformAdminPromoteRevokeSmokeTest`, `TwoTenantApiFixtureTests`,
  `JwtSmokeTests`). 25 total call sites. **Zero source changes** — surface
  preserved by design.
- **Cookie-plumbing rewrites** — 3 files (`TwoTenantApiFixture.cs`,
  `TwoTenantDevAuthHandler.cs` → renamed, `IdentityApiFixture.cs`). Structural.
- **Docstring / arch-test edits** — 2 files (`JwtSmokeTests.cs:13-21` +
  `TenantAuthorizationBehaviorTests.cs:20`). Comment-only.

**Total test files touched: 11.** Half are trivial call-site renames; the
fixture rewrites (§5) are the ONE structural change.

**Production-side residual hits by pattern (post-`DevAuth*.cs` file deletion):**

| Pattern | Files | Hits | Disposition |
|---|---|---|---|
| `DevAuth` docstring / comment | ~6 | ~12 | Line-edit (M.14.5) |
| `DevAuth:WebBaseUrl` config key | 2 | 2 | Rename App:WebBaseUrl (M.14.2 + M.14.6) |
| `B2CObjectId` identifier | 6 (src) | 12 | M.14.3 rename |
| `SetPersonaEmailCommand` type ref | 1 (src) + 1 (test) | 3 | M.14.3 delete |

**Total production files touched (source, not comments): 8.**

---

## §7 Risk + rollback

### §7.1 What breaks if M.14 ships and needs to be reverted

- **M.14.1 revert (test-only)** — trivial. Test fixture goes back to DevAuth
  cookie. No production impact.
- **M.14.2 revert (production deletes)** — 5-minute revert to the pre-M.14.2
  commit. The API redeploys with DevAuth handler + endpoints intact. Config
  keys need to be re-added to `main.bicep` if a Bicep deploy has already
  churned. Redeploy time < 10 min per M.13 tier-1 rollback.
- **M.14.3 revert (rename)** — trivial. Rename is symmetric; git-revert the
  commit.
- **M.14.4 revert (web deletes)** — trivial. Web-only, no auth impact.
- **M.14.5 / M.14.6 revert** — docstring / doc / config, no code impact.

**No tier-2 rollback needed.** M.14 ships ZERO schema changes and ZERO
data-heals. All state is code + config + tests. Contrast with M.13.4 which
required 30-day `_pre_m13_snap` schema retention.

### §7.2 What breaks in local dev

**This is the load-bearing user-impact hazard.** Pre-M.14 local dev flow:
1. Copy `.env.example` → `.env`. `DevAuth__AllowAnonymous=true` present.
2. `docker compose up`.
3. Hit any page. DevAuth Owner cookie auto-set. Everything works.

Post-M.14 local dev flow (unchanged .env.example):
1. Copy `.env.example` → `.env`. Entra config keys blank.
2. `docker compose up`.
3. Hit any page. `AddAuthentication(JwtBearerDefaults)` registered but
   `if (!string.IsNullOrWhiteSpace(entraInstance) && ...)` is false → NO
   JwtBearer handler wired → every `[Authorize]` returns "no handler
   registered" (or a 500).

**Mitigation (final — per reviewer decision on §9 Q1):**

**No dedicated local-dev credential doc ships in M.14.** Rationale:
- The operator (`niroshanaks`) confirmed UI testing will use real Entra
  accounts (regular guest account + admin account). No shared dev tenant is
  needed; each dev signs in with their own Entra credentials against the
  same Entra tenant staging uses.
- Backend-only iteration uses `dotnet test --filter "Category!=Integration"`
  which routes through the new `TestAuthHandler` from M.14.1 — no Entra
  credentials required for backend loops.
- The docker-compose "scripted-bearer" escape hatch (option (c) in the
  original draft) is explicitly rejected — it would reintroduce the exact
  attack surface DevAuth retirement is closing.

If a future dev complains about local dev friction, revisit with a
dedicated `OPS.M.14a` slice. Do not pre-emptively ship complexity that
matches nobody's actual usage.

### §7.3 Phase-out window

**Question:** hard-cut, or ship a deprecation-warning header + N-week retention?

**Recommendation: hard-cut.** Rationale:
- The DevAuth surface has been "deprecated in favor of Entra" for OPS.M.13's
  full duration (~5 sessions). Staging has been Entra-only since
  `DevAuth__AllowAnonymous=false` shipped in the OPS.M.0 close-out
  (`b21afc7`). Prod has never had DevAuth on. Local dev is the only remaining
  consumer.
- A deprecation-warning header (`Sunset: 2026-08-04` per RFC 8594) helps
  external consumers plan; DevAuth has zero external consumers.
- A retention window would keep the security surface alive without unlocking
  any additional user value.

**If pushback lands:** the phase-out becomes M.14.0 (a preparatory commit that
adds a `Warning` header + a log line every time a DevAuth-scheme auth resolves)
followed by M.14.1-6 landing after N weeks. Not the current recommendation.

---

## §8 Session budget

M.13 planned 2 sessions and took ~5 (per close-out §8). Overrun causes were
all walk-debug (MSAL init race + ProblemDetails + bootstrap-operator guards +
cross-schema migration trap). All four failure classes should NOT recur in
M.14:

- **MSAL 3.x init race** — closed by memory
  `reference_msal_browser_3x_init_pattern`. M.14 doesn't touch MSAL.
- **ProblemDetails-strips-body** — memory
  `reference_problem_details_strips_body` in place; M.14 doesn't add new
  endpoints so the pattern doesn't need re-tackling.
- **Bootstrap-operator guard chain** — the entire endpoint is deleted in
  M.14.2. This failure class disappears with the code.
- **Cross-schema migration trap** — M.14 ships zero migrations. Not
  applicable.

M.14 has one structural risk: the fixture rewrite could produce silent test
regressions (auth flowing through the wrong scheme). Mitigation: the arch
tests in M.14.2 (fact pack) + the `HasTenantRoleTests` in M.14.1 + the
existing multi-tenancy fact pack (25+ tests running under the new fixture)
form a strong verification net.

**Honest estimate: 2 sessions.**

- Session 1 — M.14.1 (fixture rewrite) + M.14.2 (production deletes). This
  is the load-bearing work. Budget: 4-6 hours including CI-wait cycles.
- Session 2 — M.14.3 + M.14.4 + M.14.5 + M.14.6. Mechanical changes;
  significant surface area but low per-hit thinking cost. Budget: 3-4 hours.

If M.14.1 hits an unexpected fixture-config integration issue (e.g. the
`ConfigureTestServices`-runs-after-`ConfigureServices` order is subtler than
§5's sketch anticipates), add a M.14.1a diagnostic sub-commit — still fits
in Session 1.

---

## §9 Open questions — DECIDED

Reviewer (`niroshanaks`) locked all six on 2026-07-04. Final answers:

1. **Local dev Entra credential requirement.** Ship neither a shared
   dev-tenant setup doc nor an escape-hatch bearer. Devs use their own
   Entra accounts (against the tenant staging uses) for UI iteration;
   `dotnet test` covers backend-only work. §7.2 updated to reflect this.
   No `docs/dev/LOCAL_DEV_ENTRA_SETUP.md` in M.14.6.

2. **`bootstrap-operator` disposition.** DELETE outright per §4 option
   (a). No hardening, no `IHostedService` replacement. First-PA-per-env
   goes through one-shot SQL migrations mirroring
   `OpsM13_BootstrapPlatformAdminForNiroshanaksEmail.cs`.

3. **`ICurrentUser.B2CObjectId → ExternalObjectId` rename.** Bundle into
   M.14.3 as originally planned. Symmetric rename; ~12 hit sites.

4. **Phase-out window.** HARD-CUT per §7.3. No deprecation header, no
   retention window. DevAuth was effectively deprecated for M.13's full
   duration; staging + prod already Entra-only.

5. **`App:WebBaseUrl` rename cycle.** TWO-PHASE per §1 M.14.2 → M.14.6.
   Fallback chain in `BookingNotificationHandlers.cs` reads `App:WebBaseUrl`
   first then falls back to `DevAuth:WebBaseUrl` for one deploy cycle;
   M.14.6 drops the fallback after Bicep migrated.

6. **`feedback_check_devauth_before_ui_handoff` memory disposition.**
   DELETE the file outright in M.14.5 (per reviewer preference — "delete
   unwanted files"). Rationale preserved in git history + this plan doc.
   Do not rewrite in place.

Execution starts at M.14.1 in the next session.

---

## §10 Appendix — key file:line pointers (as-is state today)

| Concern | File | Line |
|---|---|---|
| DevAuth handler + personas | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs` | 1-133 |
| DevAuth scheme wire-up | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs` | 29-32, 110-118 |
| DevAuth controller (7 endpoints) | `src/VrBook.Api/Controllers/IdentityController.cs` | 82-587 |
| `SetPersonaEmailCommand` | `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs` | 22 (command), 24-73 (handler) |
| `User.SetEmail` (dev bridge only caller) | `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 83-88 |
| `ICurrentUser.B2CObjectId` | `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` | 12-13 |
| `HttpCurrentUser.B2CObjectId` accessor | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` | 94-96 |
| `AnonymousCurrentUser.B2CObjectId` | `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs` | 15 |
| `PlaceBookingHandler` fallback | `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs` | 90 |
| `PricingRuleEndpointsTests` mock use | `tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleEndpointsTests.cs` | 43 |
| `DevAuth:WebBaseUrl` production read | `src/Modules/VrBook.Modules.Notifications/Application/Handlers/BookingNotificationHandlers.cs` | 87-90 |
| `DevAuth__AllowAnonymous` Bicep | `infra/main.bicep` | 268-273 |
| `DevAuth__WebBaseUrl` Bicep | `infra/main.bicep` | 275 |
| `DevAuth__AllowAnonymous=true` env sample | `.env.example` | 40-42 |
| `TwoTenantApiFixture` — cookie plumbing | `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs` | 303-347 |
| `TwoTenantDevAuthHandler` (test-only) | `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantDevAuthHandler.cs` | 1-84 |
| `IdentityApiFixture` — `DevAuthEnabled` toggle | `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs` | 26, 59-64, 82-98 |
| `TenantClaimWiringTests` — `/dev-auth/current-tenant` | `tests/VrBook.Api.IntegrationTests/Identity/TenantClaimWiringTests.cs` | 36, 57, 75, 97 |
| `DevPersonaSwitcher` root render | `web/src/app/layout.tsx` | 5, 48 |
| `DevPersonaSwitcher` component | `web/src/components/DevPersonaSwitcher.tsx` | 1-107 |
| `backdateCheckedOutAt` web client | `web/src/lib/api/booking.ts` | 187-193 |
| Admin bookings page DevAuth gates | `web/src/app/admin/bookings/[id]/page.tsx` | 11, 20, 67-82, 185, 257, 283, 397-412 |
| `devAuth` cookie mention | `web/src/lib/api/client.ts` | 149 |
| DevAuth site header mention | `web/src/components/layout/SiteHeaderAuth.tsx` | 11 |
| Feedback memory (to supersede) | `.claude/projects/c--Work-BookingApp/memory/feedback_check_devauth_before_ui_handoff.md` | — |

### §10.2 Authoritative references

- [`OPS_M_13_CLOSE_OUT.md`](./OPS_M_13_CLOSE_OUT.md) — shipping baseline.
- [`OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md`](./OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md)
  — the earlier draft superseded by this plan; mechanism carries forward.
- [`OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md`](./OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md)
  §1.4 — ID/access token separation (unchanged by M.14).
- Microsoft Learn — [WebApplicationFactory customization](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests) —
  `ConfigureTestServices` handler override pattern.
- Memory files (already indexed):
  `reference_problem_details_strips_body`, `reference_cross_schema_migration_trap`,
  `reference_msal_browser_3x_init_pattern`, `reference_email_ilike_translator`,
  `reference_rowversion_pattern`, `feedback_check_ci_after_every_push`,
  `feedback_use_ci_filter_locally`, `feedback_ship_complete_vertical_slices`,
  `feedback_no_ci_for_doc_only_commits`,
  `feedback_check_devauth_before_ui_handoff` (to be superseded in M.14.6).

---

## §11 Approval gate — CLEARED 2026-07-04

Reviewer (`niroshanaks`) signed off on §9 Q1-Q6 on 2026-07-04. Execution
starts at M.14.1 in the next session. No further approval gates until
M.14.6 close-out. Session budget: 2 sessions per §8.
