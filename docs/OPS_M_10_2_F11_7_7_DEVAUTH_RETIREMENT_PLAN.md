# Slice OPS.M.10.2 F11.7.7 — DevAuth retirement (bridge + personas + config surface)

- **Slice**: OPS.M.10.2 F11.7.7
- **Status**: Design (locked; ready for TDD once F11.7.6 lands)
- **Author**: system-architect (2026-07-01)
- **Parent**: OPS.M.10.2 F11 staging enablement
- **Preceded by**: F11.7.6 (multi-row-per-email fix — planning gate committed `5873f16`)
- **Superseded artifacts**: `SetPersonaEmailCommand`, `BootstrapOperator`, `StubStripeReadiness`, `Switch`, `Personas`, `CurrentTenant`, `BackdateCheckedOutAt`, `DevAuthHandler`, `DevAuthPersonas`, `DevAuthOptions`, `TwoTenantDevAuthHandler`, `DevPersonaSwitcher`, `web/src/lib/api/devAuth.ts`

---

## §1 Executive summary

**Scope**: retire DevAuth *entirely* — every code path, every cookie, every config knob, every dev-bridge endpoint, every persona row in the staging DB, every test-side dependency on the `vrbook-dev-persona` cookie. After F11.7.7 lands, the ONLY authentication path against a VrBook API is a real Entra External ID bearer token (prod, staging) or a test-owned `AuthenticationHandler` that lives ONLY in the integration-test assembly (unit + integration tests).

**Recommended sequence relative to F11.7.6**: **HEAL first, then retire.** F11.7.6 (multi-row-per-email fix) ships as-designed, then F11.7.7 (this slice) removes DevAuth. Rationale:

1. F11.7.6's data-heal migration is written to work on the *current* staging shape (three DevAuth persona rows sharing `niroshanaks@gmail.com`). If DevAuth is retired first, the data-heal SQL still runs but the survivor logic no longer has to consider dev-persona OIDs. Cleaner sequencing.
2. F11.7.6's provisioning-upsert-by-email is architecturally forward-compatible with OPS.M.12 (social IdPs: Google + Microsoft with shared emails). Retiring DevAuth doesn't obviate that hazard — it still ships.
3. F11.7.7's `IsRealEntraOid = Guid.TryParse` guardrail in F11.7.6 §3 becomes moot for the DevAuth-side rewrite case, but the guardrail itself stays useful for OPS.M.12 (two real Entra OIDs both being GUIDs). §6 records exactly which lines of F11.7.6 get simplified in F11.7.7.
4. If we retire first, we would have to write a second data-heal for the DevAuth persona rows AND still write the F11.7.6 upsert. Two migrations, one slice — no win.

**Hardest replacement mechanism: bootstrap-operator.** Three real jobs DevAuth was doing collapse into one non-obvious question — how does the first PlatformAdmin land in a fresh environment? The retirement doc picks option (a) from §5.2: **one-shot EF data migration that reads `Ops:InitialPlatformAdminEmail` from config and promotes the first `identity.users` row whose email matches**. Config lives in Bicep + Key Vault + GitHub secrets; the migration is idempotent (it re-runs on every deploy but only writes if no PA exists for that email). This solves the chicken-and-egg without a manual-DB-flip runbook and keeps the "first Entra sign-in wins" property. Persona switching (job #2) moves to a test-only handler; Stripe stub (job #3) is deleted — the F11.4 real endpoint `POST /api/v1/admin/tenants/{tid}/stripe/refresh-readiness` already replaces it.

**Not shipped in F11.7.7 (deferred to OPS.M.12)**: real Stripe sandbox onboarding for staging tenants that don't yet have a real Connect account. Currently staging leans on `stub-stripe-readiness` to skip the Stripe-hosted onboarding form. Post-F11.7.7, staging tenants that need Stripe readiness must EITHER (a) run the real onboarding form via `POST /api/v1/tenants/{tid}/stripe/onboard` + follow the Stripe-hosted link, OR (b) wait for OPS.M.12 which unbreaks the flow with a Stripe test-clock fixture. Documented in §8.

---

## §2 DevAuth surface — verified inventory (2026-07-01)

### 2.1 Code paths (all to be deleted)

**Production `src/` code**:

| File | Lines | Purpose | Disposition |
|---|---|---|---|
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs` | 1-133 | `AuthenticationHandler<DevAuthOptions>` + `DevAuthOptions` + `DevAuthPersona` enum + `DevAuthPersonas` static class (three persona snapshots + cookie name + resolve helper) | **Delete file.** |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs` | 28-32, 64-72 | Reads `DevAuth:AllowAnonymous`, sets default scheme to DevAuth when on, calls `auth.AddScheme<DevAuthOptions, DevAuthHandler>(...)` | **Delete DevAuth branch; default scheme stays JwtBearer unconditionally.** Docstring at line 10-16 also references DevAuth — rewrite. |
| `src/VrBook.Api/Controllers/IdentityController.cs` | 66-579 (all of `DevAuthController` + `BootstrapOperatorRequest` + `StubStripeReadinessRequest` records) | Endpoints: `personas`, `current-tenant`, `switch`, `backdate-checked-out-at`, `persona-email`, `bootstrap-operator`, `stub-stripe-readiness` | **Delete entire `DevAuthController` class + both trailing request records.** File still contains the real `IdentityController` (`/api/v1/me` surface) at lines 18-63 — keep that. |
| `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs` | 1-65 | Command + handler that repoints a User row's email by `B2CObjectId` | **Delete file.** |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260626203937_Slice5b_DevAuth_Default_Tenant_Membership.cs` + `.Designer.cs` | (auto-gen) | Seeds `tenant_memberships` rows for the DevAuth Owner + Admin personas against the default tenant | **Do NOT delete or amend.** Immutable history. The new F11.7.7 data-heal migration (§7.2) supersedes its effect by soft-deleting the persona rows; the migration's `Up` becomes a no-op the moment the persona rows are gone. |
| `src/Modules/VrBook.Modules.Identity/Domain/User.cs:80-84` | `SetEmail` public method | Used only by `SetPersonaEmailHandler` | **Delete method.** (Verified with grep: no other callers.) |

**Web `web/src/`**:

| File | Purpose | Disposition |
|---|---|---|
| `web/src/lib/api/devAuth.ts` | Client wrappers for `/dev-auth/personas` + `/dev-auth/switch` + typed `DevPersona` union | **Delete file.** |
| `web/src/components/DevPersonaSwitcher.tsx` | Floating persona switcher UI, mounted at root layout | **Delete file.** |
| `web/src/app/layout.tsx:5, 48` | Imports and renders `<DevPersonaSwitcher />` at root | **Delete import + render.** |
| `web/src/lib/api/booking.ts:186-193` | `backdateCheckedOutAt` client wrapper for `/dev-auth/backdate-checked-out-at` | **Delete export.** |
| `web/src/app/admin/bookings/[id]/page.tsx:11, 20, 68-81, 175, 247, 273, 397-406` | Imports `backdateCheckedOutAt` + `getDevPersonas`; renders a "Backdate CheckedOutAt" button gated on `devAuth` state (a `/dev-auth/personas` probe) | **Delete probe + gate + button + modal + calls to `backdateCheckedOutAt`.** The 24-hour-completion-sweep same-day verification stops working via the UI; docs a manual SQL alternative in §8. |
| `web/src/lib/api/client.ts:126` | Comment mentions the DevAuth cookie for `credentials: 'include'` | **Comment-only edit; the `credentials: 'include'` semantic still applies (MSAL cookies + Entra-managed session).** |
| `web/src/components/layout/SiteHeaderAuth.tsx:11` | Docstring mentions DevAuth persona switcher | **Comment-only edit.** |

**Config**:

| Setting | Location | Disposition |
|---|---|---|
| `DevAuth__AllowAnonymous` | `infra/main.bicep:272` (env var on the `ca-vrbook-api-*` container app) | **Delete key + line.** |
| `DevAuth__WebBaseUrl` | `infra/main.bicep:275` | **Delete key + line.** |
| `DevAuth__AllowStripeStub` | Not in Bicep — set only via `az containerapp update --set-env-vars` at operator time (see `IdentityController.cs:287, 509`) | **No infra change; the env-var-flip runbook step is removed from the F11 operator walk doc.** |
| `DevAuth:AllowAnonymous`, `DevAuth:FakeOid`, `DevAuth:FakeEmail`, `DevAuth:FakeDisplayName`, `DevAuth:IsOwner`, `DevAuth:IsAdmin` | `.env.example`, `appsettings.Development.json` (if present) | **Delete keys.** |
| `Cors__AllowedOrigins__*` | `infra/main.bicep:278-279` | **Unchanged** — the CORS list didn't depend on DevAuth. |

### 2.2 Test-project surfaces

| File | Purpose | Disposition |
|---|---|---|
| `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs:26, 59-64, 82-97` | Sets `DevAuth:AllowAnonymous` + `FakeOid/FakeEmail/…` config, exposes `CreateClientWith(bool devAuth)`. Every test in `IdentityFlowTests.cs` + `Identity/TenantClaimWiringTests.cs` uses this API. | **Rewrite** — replaces the DevAuth cookie path with a `TestAuthenticationHandler` registered via `ConfigureTestServices`. `CreateClientWith(bool authenticated)` sets an in-test claims principal (owner/anonymous). See §5.1 for the handler design. |
| `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs:304, 311-319, 336` | Registers `TwoTenantDevAuthHandler` under `SchemeName="DevAuth"`. `CreateClientAs(persona)` sets the `vrbook-dev-persona` cookie. | **Rewrite** — same pattern: replace with a `TestAuthenticationHandler` that reads a test-owned request header (`X-Test-Persona: OwnerA`) instead of a production-shape cookie. The scheme is registered under a NEW, test-only name (`TestAuth`). No production file mentions `TestAuth`. |
| `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantDevAuthHandler.cs` | Test-only `AuthenticationHandler` that reads `vrbook-dev-persona` cookie | **Rename + rewrite** as `TwoTenantTestAuthHandler` (reads `X-Test-Persona` header). Same OID constants (`OwnerAOid`, `OwnerBOid`, `PlatformAdminOid`) preserved so downstream test seeds don't renumber. |
| `tests/VrBook.Api.IntegrationTests/Identity/TenantClaimWiringTests.cs:36, 57, 75, 97` | Calls `GET /api/v1/dev-auth/current-tenant` (the debug endpoint being deleted) | **Rewrite** — the debug endpoint's data (`TenantId`, `HasTenantRole(defaultTenant, ...)`, `HasTenantRole(randomTenant, ...)`) is available through `GET /api/v1/me` + `GET /api/v1/me/tenant` + an integration-test-only helper that reads `HttpContext.RequestServices.GetRequiredService<ICurrentUser>()` from a delegating handler. Rewrite the four tests to hit `GET /api/v1/me/tenant` and inspect the returned tenant + primary-role. `HasTenantRole` coverage is preserved via a new dedicated unit test in `tests/VrBook.Modules.Identity.Tests/HasTenantRoleTests.cs` (moving the check off the debug endpoint). |
| `tests/VrBook.Api.IntegrationTests/IdentityFlowTests.cs:16-142` | Every test uses `CreateClientWith(devAuth: bool)` | **Update sites only** — rename call to `CreateClientWith(authenticated: bool)`. Assertion bodies unchanged. |
| `tests/VrBook.Api.IntegrationTests/Multitenancy/JwtSmokeTests.cs:13-21, 71-78` | Docstring cites DevAuth as the "primary matrix path"; test at line 71 depends on `TwoTenantDevAuthHandler.OwnerAOid` | **Docstring rewrite; OID constant reference stays** (it's now `TwoTenantTestAuthHandler.OwnerAOid`, same value). |
| `tests/VrBook.Api.IntegrationTests/Multitenancy/TenantAuthorizationBehaviorTests.cs:20` | Docstring cites DevAuth | **Docstring rewrite.** |
| `tests/VrBook.Api.IntegrationTests/Multitenancy/PlatformAdminBypassFactPack.cs`, `CarveOutAppLayerTests.cs`, `CrossTenantEndpointMatrix.cs`, `CrossTenantRejectionAuditFactPack.cs`, `PlatformAdminPromoteRevokeSmokeTest.cs`, `TwoTenantApiFixtureTests.cs`, `RouteMatrix.cs` | Use `fixture.CreateClientAs(persona)` — behavior identical after §5.1's rewrite | **No source changes** — the fixture's public surface stays. |

Verified with `grep -rln "CreateClientAs\b"` (7 files) + `grep -rln "CreateClientWith"` (3 files).

### 2.3 Data — the three DevAuth persona rows in staging

Per `DevAuthPersonas` snapshots, staging `identity.users` has three rows for the following OIDs whenever a DevAuth session was ever exercised:

| OID | Email (as seeded) | Purpose |
|---|---|---|
| `dev-owner-00000000` | `dev@vrbook.local` OR rewritten to `niroshanaks@gmail.com` via `persona-email` | Historical seeded Owner (matches earlier bookings on Beach Villa) |
| `dev-guest-00000001` | `dev-guest-00000001@vrbook.local` OR rewritten to `niroshanaks@gmail.com` | Guest for walk-through bookings |
| `dev-admin-00000002` | `dev-admin-00000002@vrbook.local` OR rewritten | Guest with elevated flags (also acts as Owner) |

Prod is not affected (DevAuth was `false` in `main.bicep:272` for staging AND prod since OPS.M.0 close-out `b21afc7` per the current comment). Only staging may have these rows.

FK references from other schemas that hold `guest_user_id` / `owner_user_id` / `actor_user_id` values equal to those three user ids:

- `booking.bookings.guest_user_id` — the DevAuth Guest and Admin users placed bookings on the seeded Beach Villa; must survive.
- `booking.bookings.owner_user_id` (via property.owner_user_id join, not a direct column) — the seeded Beach Villa's OwnerUserId is `dev-owner-00000000`'s `identity.users.Id`. **Property.OwnerUserId is a plain uuid column — cannot NULL it.**
- `reviews.reviews.guest_user_id` — any reviews left by dev personas.
- `messaging.threads.guest_user_id` — any threads.
- `identity.audit_log.actor_user_id` — every audit row for DevAuth-personified actions.
- `identity.tenant_memberships.user_id` — the `Slice5b_DevAuth_Default_Tenant_Membership` migration seeded rows for `dev-owner-*` and `dev-admin-*`.

**Correct disposition: soft-delete the three DevAuth persona `identity.users` rows** (`DeletedAt = NOW()`, `DeletedBy = NULL` — system-initiated). Rationale identical to F11.7.6 §5's soft-delete-losers strategy: the id stays valid as a uuid reference so the historical FK-shaped columns still resolve; the row itself is hidden by the `IdentityDbContext` global query filter; the row's `B2CObjectId` (`dev-owner-*`) is still unique, so no future sign-in accidentally lands on it. Their membership rows also get soft-deleted (§7.3 SQL).

The Beach Villa property poses an edge case: `owner_user_id` points at `dev-owner-00000000`'s user id. Soft-deleting the user does NOT break the property's FK (which is a uuid, not a hard FK to a soft-delete-filtered global query). But `/api/v1/me` for a REAL Entra sign-in as `niroshanaks@gmail.com` post-F11.7.6 will resolve to the survivor row (not `dev-owner-*`), so the "owner viewing their own property" path no longer works because `Property.OwnerUserId != CurrentUser.UserId`. **F11.7.7 fix**: the data-heal SQL also UPDATEs `catalog.properties.owner_user_id = <survivor_user_id>` for every row currently owned by any of the three DevAuth OIDs — see §7.3.

---

## §3 Design invariants (locked)

1. **Prod build carries zero DevAuth code.** No `DevAuthHandler`, no `DevAuthPersonas`, no `SetPersonaEmailCommand`, no `SetEmail` domain method. Post-F11.7.7 `grep -r "DevAuth" src/` returns zero hits (excluding immutable migration file).
2. **Test build has its own authentication handler**, registered under a scheme name that does not appear in production source (`TestAuth`).
3. **First platform admin per env is stamped from config**, not from a network call. The chicken-and-egg is broken by making the promotion a data migration, not an API call.
4. **No config knob toggles authentication modes.** Prod, staging, and dev all use JwtBearer against Entra External ID. Local development uses either a mocked Entra tenant OR the test-only `TestAuthenticationHandler` if the developer opts into an integration-test-style run — never a production-code auth path.
5. **F11.6.1's `bootstrap-operator` API contract disappears.** Web has no consumer (verified with grep). Any operator runbook that referenced it is redirected to (a) sign in via Entra, (b) let the data migration promote them, OR (c) sign in as an existing PA and call the F11.3 `POST /api/v1/admin/platform/tenants/{tid}/memberships`.
6. **Historical staging DB data (bookings, reviews, messages authored under DevAuth personas) is preserved.** Soft-delete of the user row does not break historical FK references.

---

## §4 Commit sequence

Each sub-commit is a full vertical slice per the F11 convention (code + tests + green CI). Slice budget: **10 commits** including the close-out. All commits are `git push origin develop`; `gh run watch` after each until conclusion=success per `feedback_check_ci_after_every_push`. Local test filter is `dotnet test --filter "Category!=Integration"` per `feedback_use_ci_filter_locally`.

**Ordering constraint**: F11.7.7 depends on F11.7.6 being GREEN in CI (both provisioning-upsert AND the data-heal migration for the multi-row hazard). Landing F11.7.7 while F11.7.6 is red would leave staging with (a) the DevAuth personas gone, (b) no `IUserRepository.GetActiveByEmailAsync` in place, and (c) no data-heal — the operator walk would break in a different way.

### F11.7.7.1 — RED: architecture tests pinning DevAuth is gone

**Scope**: TDD entry gate. New arch-test facts that will fail today (DevAuth is present) and pass after F11.7.7.9. Zero production changes.

**Files**:
- `tests/VrBook.Architecture.Tests/NoDevAuthInProductionTests.cs` (new)

Assertions (regex source-text scans, matching the `PropertyActivateObsoleteBridgeTests.cs` style):

1. **`No_production_module_defines_DevAuthHandler_type`** — reflect over the assemblies list in `PropertyActivateObsoleteBridgeTests.cs:67-78`; assert `assembly.GetType("VrBook.Modules.Identity.Infrastructure.Auth.DevAuthHandler")` returns null for every entry.
2. **`No_production_module_defines_DevAuthPersonas_type`** — same shape, checks `DevAuthPersonas` type.
3. **`No_production_module_defines_SetPersonaEmailCommand_type`** — same shape.
4. **`AuthExtensions_registers_no_DevAuth_scheme`** — source scan on `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs`: MUST NOT contain the substring `"DevAuth"` (case-sensitive).
5. **`IdentityController_defines_no_DevAuthController_class`** — source scan on `src/VrBook.Api/Controllers/IdentityController.cs`: MUST NOT contain `class DevAuthController`.
6. **`No_Bicep_env_var_references_DevAuth`** — source scan on `infra/main.bicep`: MUST NOT contain the substring `"DevAuth"`.

**Tests**: the arch facts themselves.
**Local**: `dotnet test tests/VrBook.Architecture.Tests --filter "FullyQualifiedName~NoDevAuthInProductionTests"` → expect all 6 to FAIL.
**CI expectation**: **RED**. This is the RED half of the TDD pair. Slice OPS.M.10.2 convention: RED commits are pushed alone so the CI history shows the exact commit that goes green.

### F11.7.7.2 — GREEN scaffold: test-only `TestAuthenticationHandler`

**Scope**: introduce the replacement handler for integration tests. Same file adds it in BOTH fixtures.

**Files**:
- `tests/VrBook.Api.IntegrationTests/Auth/TestAuthenticationHandler.cs` (new, shared) —
  - Sealed class inheriting `AuthenticationHandler<AuthenticationSchemeOptions>`.
  - `public const string SchemeName = "TestAuth"`.
  - `public const string TestPersonaHeader = "X-Test-Persona"`.
  - Reads the header value; matches against a set of registered persona snapshots (Owner / Guest / Admin for `IdentityApiFixture`; OwnerA / OwnerB / PlatformAdmin for `TwoTenantApiFixture`).
  - The persona snapshot dictionary is passed in via `AuthenticationHandler<T>` options (subclass `TestAuthenticationSchemeOptions : AuthenticationSchemeOptions { public IReadOnlyDictionary<string, TestPersona> Personas }`).
  - Emits the same claim shape as production Entra (`oid`, `emails`, `email_verified`, `name`, `role` claims). No `dev_persona` claim.
- `tests/VrBook.Api.IntegrationTests/Auth/TestPersona.cs` (new) — `record TestPersona(string Oid, string Email, string DisplayName, IReadOnlyList<string> Roles)`.

**Tests**: no new tests yet — the handler is scaffolding.

**Local**: `dotnet build` succeeds; no test additions to fail yet.

**CI expectation**: green (no test changes). Six NoDevAuthInProductionTests still red — expected.

### F11.7.7.3 — GREEN: `IdentityApiFixture` migrates onto `TestAuthenticationHandler`

**Scope**: replace the DevAuth cookie plumbing in `IdentityApiFixture` with the test handler. Production `DevAuthHandler` is untouched (still needed as a comparison target for F11.7.7.9 delete).

**Files**:
- `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs`:
  - Remove `DevAuthEnabled` public property, remove all `DevAuth:*` config keys from `ConfigureAppConfiguration`.
  - Add `ConfigureTestServices` that registers `TestAuthenticationHandler` under scheme `TestAuth`, seeded with a single persona `test-owner-aaaa` matching the current `FakeOid` value (so downstream test assertions like `dto.Email.Should().Be("owner@vrbook.test")` keep passing).
  - `CreateClientWith(bool authenticated)`:
    - `authenticated=true` → set the `X-Test-Persona: TestOwner` header.
    - `authenticated=false` → no header (anonymous path).
  - Delete `bool devAuth` parameter and rename to `authenticated`.
- `tests/VrBook.Api.IntegrationTests/IdentityFlowTests.cs`: rename `devAuth` → `authenticated` at each call site (7 sites). No assertion changes.
- `tests/VrBook.Api.IntegrationTests/Identity/TenantClaimWiringTests.cs`:
  - Rename `devAuth` → `authenticated`.
  - Replace `client.GetAsync("/api/v1/dev-auth/current-tenant")` with the equivalent through `/api/v1/me/tenant` (returns `MeTenantDto` — which is the `IsTenantAdminOfDefault` answer via `Role` inspection).
  - The two `IsTenantAdminOfDefault` / `IsTenantAdminOfRandom` semantic checks (`HasTenantRole` for the default tenant + a random one) move to a new unit test file — see F11.7.7.4.

**Tests**: existing `IdentityFlowTests` + `TenantClaimWiringTests` still pass under the new handler (behavioral equivalence).

**Local**: `dotnet test --filter "Category!=Integration"` green.

**CI expectation**: green. Arch tests from F11.7.7.1 still red.

### F11.7.7.4 — GREEN: `ICurrentUser.HasTenantRole` unit-test coverage

**Scope**: `HasTenantRole(Guid tenantId, string role)` coverage previously ran through the `/api/v1/dev-auth/current-tenant` debug endpoint. That endpoint disappears; the coverage moves to a proper unit test.

**Files**:
- `tests/VrBook.Modules.Identity.Tests/HasTenantRoleTests.cs` (new) — Category=Unit.
  - Constructs an `HttpCurrentUser` directly (or via a minimal test double), seeded with the appropriate claims + `HttpContext.Items` for two tenants + one role.
  - Facts: `HasTenantRole_returns_true_for_matching_tenant_and_role`; `_returns_false_for_wrong_tenant`; `_returns_false_for_wrong_role`; `_returns_false_for_null_tenant_claim`.

**Tests**: the four facts.

**Local**: `dotnet test --filter "Category=Unit&FullyQualifiedName~HasTenantRoleTests"` green.

**CI expectation**: green. Six arch facts still red.

### F11.7.7.5 — GREEN: `TwoTenantApiFixture` migrates onto `TestAuthenticationHandler`

**Scope**: parallel rewrite for the two-tenant fixture.

**Files**:
- `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantDevAuthHandler.cs` → **rename to** `TwoTenantTestAuthHandler.cs`. Content rewrite: read `X-Test-Persona` header instead of `vrbook-dev-persona` cookie. Same OID constants (`OwnerAOid`, `OwnerBOid`, `PlatformAdminOid`).
- `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs`:
  - `ConfigureAppConfiguration` — delete `DevAuth:AllowAnonymous` key. Everything else unchanged.
  - `ConfigureTestServices` — register `TwoTenantTestAuthHandler` under scheme `TestAuth`.
  - `CreateClientAs(string? persona)` — set `X-Test-Persona: {persona}` header instead of cookie. Signature unchanged.
  - `SeedAsync` — use `TwoTenantTestAuthHandler.Owner{A,B}Oid` instead of `TwoTenantDevAuthHandler.Owner{A,B}Oid`. Update usings.
- `tests/VrBook.Api.IntegrationTests/Multitenancy/JwtSmokeTests.cs:71` — reference `TwoTenantTestAuthHandler.OwnerAOid`.
- All other files using `CreateClientAs` (7 files enumerated in §2.2) — **no changes**; the public surface preserved.

**Tests**: 7 files' tests all pass under the header-driven handler.

**Local**: `dotnet test --filter "Category!=Integration"` green. (Integration tests run in CI's Integration step; they still pass under Docker.)

**CI expectation**: green. Six arch facts still red.

### F11.7.7.6 — GREEN: bootstrap-operator replacement — `InitialPlatformAdmin` data migration

**Scope**: **the load-bearing replacement for `POST /api/v1/dev-auth/bootstrap-operator`**. See §5.2.

**Files**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_OpsM10_2_F11_7_7_InitialPlatformAdminPromotion.cs` (new) —
  - `Up`: reads `Ops:InitialPlatformAdminEmail` from config via a companion `IMigrationConfigReader` (see below); if present + `identity.users` has a row with that email + that row is not PA yet, sets `is_platform_admin = true`, writes to `identity.audit_log`. Idempotent — re-runs are no-ops.
  - `Down`: no-op with a comment (`-- Down is a no-op: we do not un-promote a manually-verified operator on a rollback.`).
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IMigrationConfigReader.cs` (new) — one-method interface `string? GetString(string key)`. This is the seam that lets us test the migration against a fake config in the integration test.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/MigrationConfigReader.cs` (new) — impl that reads from `Microsoft.Extensions.Configuration.IConfiguration`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IdentityDbContext.cs` — no change; the migration reads config through its own injected reader (EF migration APIs support constructor injection via the DbContext factory pattern).
- `infra/main.bicep` — add env var `Ops__InitialPlatformAdminEmail` with a Key Vault reference (`ops-initial-platform-admin-email`), populated per-env by the CI pipeline. Staging value is `niroshanaks@gmail.com` (the user's real Entra email); prod is unset by default (prod's first PA is provisioned manually with three-named-humans audit per the OPS.M.8 promote runbook).
- `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` — append §7 documenting the new migration-driven path AND the prod-still-manual policy.

**Tests**:
- `tests/VrBook.Api.IntegrationTests/Identity/InitialPlatformAdminMigrationTests.cs` (new, Category=Integration) — three facts:
  1. Migration runs with `Ops:InitialPlatformAdminEmail` unset → no changes.
  2. Migration runs with email set + user row exists → user's `is_platform_admin=true` AND audit row present.
  3. Migration runs with email set + user row missing → no changes (idempotent — will apply on next migration if user later signs in and migration re-runs). **Note**: EF migrations only run once per generated id; the operator flow will need to trigger a REPEAT via a small "migration marker" trick — see below.

**Repeat-on-next-deploy trick**: EF migrations run once by design. The InitialPlatformAdmin promotion needs to be idempotent across deploys because the user might not have signed in yet on the first deploy. Two options:

- **(a) Use `MigrationBuilder.Sql` with a `NOT EXISTS` guard in a plain SQL migration**, and let subsequent deploys skip it (the migration is already applied per `__EFMigrationsHistory`). BUT: this means the operator MUST sign in BEFORE the InitialPlatformAdmin migration is applied. On a fresh env, that's impossible (they can't sign in until the DB is migrated).
- **(b) Use a startup-time hosted service (`IHostedService`) that runs after `db.Database.MigrateAsync()` at every boot**. The service checks config, checks the DB, and idempotently promotes. Re-runs are cheap. **This is the chosen shape.** The "migration" title in the sub-commit name is a slight misnomer for a `IHostedService` — accepted trade-off for the cleaner semantics.

**Revised file list** (correcting the shape):
- ~~`YYYYMMDDHHMMSS_OpsM10_2_F11_7_7_InitialPlatformAdminPromotion.cs`~~ (no migration file)
- `src/Modules/VrBook.Modules.Identity/Infrastructure/InitialPlatformAdminPromoter.cs` (new) — `IHostedService` that runs on startup post-migration. Reads `Ops:InitialPlatformAdminEmail`, promotes idempotently.
- `src/Modules/VrBook.Modules.Identity/IdentityModule.cs` — register the hosted service.
- Everything else in this sub-commit still applies (Bicep, runbook, integration test).

**Local**: `dotnet test tests/VrBook.Api.IntegrationTests --filter "FullyQualifiedName~InitialPlatformAdminMigrationTests"` green.

**CI expectation**: green (Integration step covers the new fact pack). Six arch facts still red.

### F11.7.7.7 — GREEN: web-side deletes

**Scope**: strip DevAuth references from `web/`.

**Files**:
- `web/src/components/DevPersonaSwitcher.tsx` — **delete**.
- `web/src/lib/api/devAuth.ts` — **delete**.
- `web/src/app/layout.tsx:5, 48` — remove the import + render.
- `web/src/lib/api/booking.ts:186-193` — delete `backdateCheckedOutAt` export.
- `web/src/app/admin/bookings/[id]/page.tsx` — remove `import { getDevPersonas }` (line 20), remove the `useState(false) devAuth` state (line 71) + the effect that probes `getDevPersonas` (lines 76-82), remove the two `{devAuth && ...}` gates (lines 247, 273), remove the entire backdate modal (lines 397-406), remove `import { backdateCheckedOutAt }` (line 11) + `await backdateCheckedOutAt(...)` at line 175 + surrounding state (`backdateOpen`, `backdateError`, setters). Adjust JSX accordingly.
- `web/src/lib/api/client.ts:126` — comment edit (drop DevAuth mention; keep `credentials: 'include'`).
- `web/src/components/layout/SiteHeaderAuth.tsx:11` — comment edit.

**Tests**:
- `web/src/app/admin/bookings/[id]/__tests__/*` — remove any tests that exercise the backdate button (verify with `grep`).
- No new tests. Vitest gate already covers the admin bookings page without a backdate path.

**Local**: `cd web && npm run build && npm test` green.

**CI expectation**: `cd-staging-web.yml` green. Six arch facts still red.

### F11.7.7.8 — GREEN: Bicep + config surface deletes

**Scope**: infrastructure config that references DevAuth.

**Files**:
- `infra/main.bicep`:
  - Delete lines 268-275 (the `DevAuth__AllowAnonymous` + `DevAuth__WebBaseUrl` env vars + surrounding comment block).
- `.env.example` — delete `DevAuth:AllowAnonymous`, `DevAuth:FakeOid`, `DevAuth:FakeEmail`, `DevAuth:FakeDisplayName`, `DevAuth:IsOwner`, `DevAuth:IsAdmin` keys.
- `appsettings.Development.json` (if present) — same cleanup.
- `.github/workflows/cd-staging-api.yml` — the smoke block comments at lines 502-508 explicitly reference the DevAuth-disabled path; update the comment to reference the F11.7.7 retirement doc AND the Entra-only baseline (the `assert "GET /api/v1/me" 401` behavior is unchanged).

**Tests**: none directly. F11.7.7.1's arch fact `No_Bicep_env_var_references_DevAuth` (fact #6) — flips from RED to GREEN with this commit.

**Local**: `bicep build infra/main.bicep` green.

**CI expectation**: green. **Five of six arch facts still red** (Bicep one flipped by this commit; other five flip in F11.7.7.9).

### F11.7.7.9 — GREEN: production `src/` deletions

**Scope**: **the load-bearing production code deletion**. Every file listed in §2.1 that carries a "Delete" verdict.

**Files**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs` — **delete**.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs` —
  - Remove `devAuthEnabled` variable (line 28), `defaultScheme` ternary (line 29-31) → hard-code `defaultScheme = JwtBearerDefaults.AuthenticationScheme`.
  - Remove the `if (devAuthEnabled)` block (lines 64-72).
  - Rewrite the class docstring (lines 10-16) to describe JwtBearer-only.
- `src/VrBook.Api/Controllers/IdentityController.cs` — delete the entire `DevAuthController` class + `StubStripeReadinessRequest` + `BootstrapOperatorRequest` records. Keep the `IdentityController` at lines 18-63.
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs` — **delete**.
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs:80-84` — delete `SetEmail` method.
- `src/VrBook.Api/Program.cs:15` — `using VrBook.Modules.Identity.Infrastructure.Auth;` no longer needed for DevAuthHandler; verify grep whether other Identity.Auth types are used. **YES** (AuthExtensions.AddVrBookAuthentication) — keep the using.

**Tests**: NoDevAuthInProductionTests's remaining 5 facts flip from RED to GREEN with this commit.

**Local**: `dotnet build` succeeds; `dotnet test --filter "Category!=Integration"` green. **Explicitly run**: `dotnet test tests/VrBook.Architecture.Tests --filter "FullyQualifiedName~NoDevAuthInProductionTests"` — all 6 green.

**CI expectation**: green. **All 6 arch facts green.** Full test suite (arch + unit + integration) green.

### F11.7.7.10 — GREEN: data heal for the three DevAuth persona rows

**Scope**: soft-delete the persona user rows + their `tenant_memberships` + repoint any `catalog.properties.owner_user_id` that referenced them onto the F11.7.6 survivor row.

**Files**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_OpsM10_2_F11_7_7_SoftDeleteDevAuthPersonas.cs` (new) — see §7 for the migration body.
- `src/Modules/VrBook.Modules.Catalog/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_OpsM10_2_F11_7_7_RepointPropertyOwners.cs` (new) — the Catalog schema portion (UPDATE `catalog.properties.owner_user_id`).

Cross-schema migration rationale: identity's migration handles identity's tables; catalog's migration handles catalog's. Both are deployed by `VrBook.Migrator/Program.cs` in the same run.

**Tests**:
- `tests/VrBook.Api.IntegrationTests/Identity/DevAuthPersonaSoftDeleteMigrationTests.cs` (new, Category=Integration) — seeds the three persona rows + a survivor row + a Beach Villa property owned by `dev-owner-*`, runs both migrations via `db.Database.MigrateAsync()`, asserts:
  1. All three DevAuth persona `identity.users` rows have `deleted_at IS NOT NULL`.
  2. All `identity.tenant_memberships` for those users have `deleted_at IS NOT NULL`.
  3. `catalog.properties.owner_user_id` was repointed to the survivor's user id.
  4. Historical `booking.bookings.guest_user_id` values are UNCHANGED (survives soft-delete because uuid column isn't a real FK).

**Local**: `dotnet test tests/VrBook.Api.IntegrationTests --filter "FullyQualifiedName~DevAuthPersonaSoftDeleteMigrationTests"` green.

**CI expectation**: green.

### F11.7.7.11 — Close-out doc

**Scope**: §11 close-out per slice-completion policy. Documents:

- Verification commands: sign in as `niroshanaks@gmail.com` via real Entra → `GET /api/v1/me` returns 200 + `IsPlatformAdmin=true` (verifies F11.7.7.6 hosted-service ran on startup). `GET /admin/bookings/{tentative-id}/confirm` → 200 (verifies the surviving path).
- API contract that shipped: REMOVED endpoints listed (`/api/v1/dev-auth/*` all gone). No new endpoints (the F11.3 `POST /api/v1/admin/platform/tenants/{tid}/memberships` from `c37ac0a` and F11.4 `POST /api/v1/admin/tenants/{tid}/stripe/refresh-readiness` from `8a3521d` were the pre-existing real-API replacements).
- Residual risk: staging's `stub-stripe-readiness` is gone; new staging tenants that don't have a real Stripe account MUST use the real Stripe onboarding form OR wait for OPS.M.12.
- Manual `backdate-checked-out-at` UI is gone; the same-day 24h-completion-sweep verification now requires operator SQL (documented in the runbook).

**File**: `docs/OPS_M_10_2_F11_7_7_DEVAUTH_RETIREMENT_PLAN.md` (this file) — append §11.

**This is a doc-only commit. Per memory `no-ci-for-doc-only-commits`: do NOT `gh run watch`.** The previous code-commit (F11.7.7.10) is the binding CI gate.

---

## §5 Replacement design for each of the 3 DevAuth jobs

### §5.1 Persona switching for integration tests

**Options considered**:

| Option | Mechanism | Pro | Con |
|---|---|---|---|
| **A. In-test `AuthenticationHandler` reading `X-Test-Persona` header** | Test-only handler under scheme `TestAuth`; header value maps to a snapshot dictionary; header is set on the `HttpClient` per-request | No cookie surface; no production shape (real Entra never emits `X-Test-Persona`); trivially swappable per-request without state | Test-code shape different from production auth (header vs bearer JWT). Mitigation: `JwtSmokeTests.cs` scaffold already covers the "real JWT" path — that scaffold gets a follow-up in OPS.M.10.1 to unskip. |
| **B. Mint self-signed JWTs in-test** | Test project holds a signing key; each test builds a JWT with the right `oid` + `emails` claims; `WebApplicationFactory` config trusts the test issuer | Production-shape fidelity — tests exercise the exact JwtBearer code path | Requires config wiring + trust seam in production `AuthExtensions.cs` (`ValidIssuers` gains a test issuer under a build-time constant OR test-only override). That's a production-code seam JUST for tests — smell. |
| **C. Delegating handler on the client side that stamps claims into an `AuthenticationTicket` via a shortcut** | Uses ASP.NET's `AllowSynchronousIO` + `TestServer` mechanics | Fewer moving parts | Very fragile across ASP.NET versions; behavior implicit. |

**Decision: Option A.**

Reasoning:
- Zero production-code seam. `TestAuth` scheme name never appears in `src/`.
- Header-driven persona swap is atomic and per-request (no cookie propagation to worry about).
- `JwtSmokeTests.cs` continues to cover the "we didn't break the JWT path" invariant (its 2 already-green tests + 1 skipped scaffold stay in place).
- Preserves the existing `CreateClientAs(persona)` public surface — 24 test-site call points unchanged.

**File shape**: see F11.7.7.2. The handler + subclass options bag live under `tests/VrBook.Api.IntegrationTests/Auth/`.

### §5.2 Bootstrap-operator (staging first-PA seed)

**Options considered**:

| Option | Mechanism | Pro | Con |
|---|---|---|---|
| **A. Config-driven startup promoter (`IHostedService`)** | `InitialPlatformAdminPromoter` reads `Ops:InitialPlatformAdminEmail` at every boot; idempotently promotes the row if present + not already PA; writes to `identity.audit_log` | Zero manual steps per env; chicken-and-egg broken (user signs in via Entra → user row exists → next deploy or next API boot promotes them); the config lives in Bicep + Key Vault so it's auditable | Env config becomes part of the auth-critical surface. Someone with Key Vault access can flip the initial PA. **Mitigation**: Key Vault access is already three-named-humans-only per OPS.M.8 promote runbook; this doesn't widen the trust surface. |
| **B. CLI in `VrBook.Tools`** | Separate console app; DBA runs it with a KeyVault-backed secret; emits SQL | No auth surface change; runbook step is scriptable | Requires a new project + deploy pipeline; the very same secret access still needed. Introduces a maintenance surface. |
| **C. Real admin API `/api/v1/admin/platform/promote`** | New endpoint gated on `[Authorize(Roles="PlatformAdmin")]` | Reuses existing auth patterns | Chicken-and-egg: no PA exists on a fresh env to call it. |
| **D. Manual DB flip via KeyVault-backed psql** | Three-named-humans runbook; operator runs a specific UPDATE | Zero code | High operational cost every env; every PA-add ceremony is a manual event. |

**Decision: Option A.**

Reasoning:
- Chicken-and-egg cleanly broken. First deploy after F11.7.7.6 lands: staging Bicep already has `Ops__InitialPlatformAdminEmail=niroshanaks@gmail.com`; the user's `identity.users` row already exists (post-F11.7.6 provisioning-upsert converged); the hosted service on next boot promotes them.
- Idempotent: the service checks `IsPlatformAdmin` before writing, so subsequent boots are no-ops.
- Auditable: the promotion writes to `identity.audit_log` with `actor_user_id = user.Id` (self-promotion via config) and `action = "user.grant-platform-admin"` + `metadata = {"source": "initial-platform-admin-hosted-service", "config-key": "Ops:InitialPlatformAdminEmail"}` — differentiable from a real API-driven promote.
- Prod policy: `Ops__InitialPlatformAdminEmail` is unset in prod Bicep by default. Prod's first PA still goes through the three-named-humans manual SQL per OPS.M.8's existing runbook. F11.7.7 doesn't change prod's auth-critical policy.
- Follow-up: OPS.M.11 hygiene may swap the hosted-service to a one-shot SQL migration once every env's initial PA is stamped. But for F11.7.7, the hosted service is the correct shape for both a fresh env AND an existing env where the PA row happens to be missing.

**Why NOT Option B**: adds a project + deploy pipeline for a one-time-per-env need. Not worth the maintenance surface.

**Why NOT Option C**: the recursive-permission requirement (need a PA to appoint a PA) is a hard blocker for the first PA. Would need a config-driven backdoor anyway. Option A is that backdoor, done cleanly.

**Why NOT Option D**: still the correct fallback for prod (F11.7.7 keeps it). For staging, the ceremony every deploy is friction that Option A removes.

### §5.3 Stub Stripe readiness

**Options considered**:

| Option | Mechanism | Pro | Con |
|---|---|---|---|
| **A. Real Stripe sandbox onboarding** | Staging tenants go through `/api/v1/tenants/{tid}/stripe/onboard` → the Stripe-hosted form → `account.updated` webhook fires → real state machine transitions | Production-shape fidelity; forces us to fix any Stripe onboarding flakes | Stripe-hosted form is manual per operator walk; adds ~5min per staging setup |
| **B. Integration-test-only `IConnectAccountReadinessUpdater` double** | Test project registers a fake impl that flips the tenant to Active without calling the real Stripe gateway | Zero manual steps for integration tests | Only helps integration tests, not staging operator walks; staging still needs option A |
| **C. Delete the stub AND rely on the existing real endpoint `POST /api/v1/admin/tenants/{tid}/stripe/refresh-readiness` (F11.4)** | Post-F11.7.7: to flip a staging tenant to Active without going through onboarding, an operator seeds the `StripeAccountId` on the tenant via `POST /admin/platform/tenants/{tid}/memberships` OR via a small `dotnet ef` seed script, then calls F11.4 to re-pull readiness from Stripe | Real Stripe gateway; no config knob; runbook-driven | Requires the tenant to have a valid `StripeAccountId` first. For a fresh staging tenant, that means real Stripe onboarding — same friction as (A) |

**Decision: Option A + Option B combined.**

- **Integration tests**: Option B. Register a fake `IConnectAccountReadinessUpdater` via `ConfigureTestServices` in `TwoTenantApiFixture` so cross-tenant fact packs and matrix tests don't hit real Stripe. This double already conceptually exists (integration tests don't exercise real Stripe today); F11.7.7 makes it explicit.
- **Staging operator walks**: Option A. `stub-stripe-readiness` is deleted; the operator uses real Stripe sandbox onboarding. If Stripe's sandbox form flakes (the reason `stub-stripe-readiness` existed), the OPS.M.12 slice unbreaks the flow with a Stripe test-clock fixture per its plan.

**Why NOT deprecate `stub-stripe-readiness` but keep it Bicep-gated**: the `DevAuth:AllowStripeStub` gate exists precisely because we're afraid of a config flip. The retirement removes the code path so no flip is possible. That's a strict win on prod safety.

**Cost**: staging operators lose the 30-second stub. F11.7.7 close-out documents the runbook change ("if Stripe sandbox is flaking, wait for OPS.M.12; do NOT reintroduce a stub"). Staging operator workflow becomes: sign in as PA → complete Stripe onboarding form once per staging tenant → tenant flips to Active via real webhook.

---

## §6 F11.7.6 disposition — simplify but ship

F11.7.6 (planning gate committed `5873f16`) ships as-designed with **two simplifications after F11.7.7 lands**:

### 6.1 Ships as-designed

Full F11.7.6 plan executes. Both the provisioning-upsert-by-email AND the data-heal migration run in staging. The multi-row-per-email problem does NOT go away when DevAuth is retired — OPS.M.12 (Google + Microsoft social IdPs against the same Entra tenant) introduces the SAME multi-row hazard. F11.7.6 is the structural fix; it ships first.

### 6.2 Simplifications applied AFTER F11.7.7.9 lands

Once F11.7.7.9 removes DevAuth from production, F11.7.6's `Guid.TryParse` guardrail (F11.7.6 §3, `IsRealEntraOid` check) is **no longer needed to distinguish DevAuth from Entra OIDs**, because DevAuth OIDs no longer exist in the codebase. The guardrail's purpose shifts from "distinguish `dev-*` prefixed OIDs from real Entra OIDs" to "handle the case where two real Entra OIDs (both GUIDs) collide on the same email." That's the OPS.M.12 use case.

**Options considered for the simplification**:

| Option | Mechanism |
|---|---|
| **α. Keep the `Guid.TryParse` check unchanged** | Zero code change; the check just always returns true post-F11.7.7 (all OIDs are Entra GUIDs); the throw case (`email_already_claimed`) fires whenever two Entra sign-ins collide on an email — exactly what OPS.M.12 needs. |
| **β. Delete the `Guid.TryParse` check and always throw on multi-row-hit** | Simplest code; but changes behavior for `SetPersonaEmail` bridge (which is being deleted anyway, so moot post-F11.7.7). |
| **γ. Keep the check but rename the helper to `IsClaimedByOtherEntraIdentity`** | Reflects the intent; small refactor. |

**Decision: α (keep the check unchanged).**

Reasoning: F11.7.6's guardrail was already the right shape for the OPS.M.12 use case. The DevAuth-specific bit (`dev-*` prefix heuristic — rejected in the F11.7.6 doc's own §3.3) never made it into the final design. Post-F11.7.7, the `Guid.TryParse` check works correctly for the "two real Entra OIDs collide" case. No F11.7.6 code changes needed. **No F11.7.6 supersede or defer.**

**No commit needed against F11.7.6** — its behavior is correct as-shipped.

### 6.3 F11.7.6.4 data-heal migration order relative to F11.7.7.10

Two data-heal migrations exist:

- F11.7.6.4 — soft-deletes duplicate rows sharing an email (per §5 survivor policy).
- F11.7.7.10 — soft-deletes the three DevAuth persona rows (regardless of whether they share an email with any other row).

Order in the migrator: F11.7.6.4 runs FIRST (already committed by the F11.7.6 gate). F11.7.7.10 runs SECOND. This is the correct order because:

- After F11.7.6.4, the "survivor" for `niroshanaks@gmail.com` is either the real Entra row (`is_platform_admin=true` from F11.7.5.10's widening) OR one of the DevAuth persona rows if the real Entra row is missing.
- F11.7.7.10 then soft-deletes the three DevAuth persona rows unconditionally. If the survivor picked by F11.7.6.4 was a DevAuth persona (edge case: user never signed in via real Entra yet), F11.7.7.10 undoes that survivor pick. This is CORRECT because after DevAuth is retired, there's no way for a `dev-owner-*` OID to sign in anyway.

**Edge case**: what if a Beach Villa property's `owner_user_id` points at a DevAuth persona row that F11.7.7.10 is about to soft-delete, AND there's no survivor? The F11.7.7.10 migration checks for this: if it's about to soft-delete a user row that owns properties AND the target survivor (per email match against `Ops:InitialPlatformAdminEmail` or the F11.7.6 survivor lookup) doesn't exist, the migration **throws** with a clear error message. Prevents a data-loss silent failure. See §7 SQL.

---

## §7 Data-migration plan for the three DevAuth persona rows

### §7.1 Diagnostics query (run first)

```sql
-- Diagnose the current shape before running F11.7.7.10.
SELECT u."Id", u.b2c_object_id, u.email, u.is_platform_admin, u.deleted_at,
       (SELECT COUNT(*) FROM identity.tenant_memberships tm
          WHERE tm.user_id = u."Id" AND tm.deleted_at IS NULL) AS active_memberships,
       (SELECT COUNT(*) FROM catalog.properties p
          WHERE p.owner_user_id = u."Id" AND p.deleted_at IS NULL) AS owned_properties,
       (SELECT COUNT(*) FROM booking.bookings b WHERE b.guest_user_id = u."Id") AS historic_bookings
  FROM identity.users u
 WHERE u.b2c_object_id IN
   ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002');
```

**Expected in staging** (as of 2026-07-01, per user's earlier walks):
- All three rows present. Emails may be rewritten to `niroshanaks@gmail.com` (F11.7.5-era `persona-email` calls).
- `dev-owner-*` owns the seeded Beach Villa property.
- `dev-guest-*` has ~4 historic bookings on the Beach Villa.
- `dev-admin-*` has 0-1 bookings.

### §7.2 Identity migration — soft-delete users + memberships

```sql
-- F11.7.7.10a — soft-delete the three DevAuth persona user rows +
-- their tenant_memberships. Runs AFTER F11.7.6.4 (which handles the
-- multi-row-per-email general case); this migration is DevAuth-specific.

-- Precondition: for every DevAuth persona row that OWNS a property in
-- catalog.properties (owner_user_id), a survivor user row MUST exist
-- with the same email. If not, we would orphan the property. Throw.
DO $$
DECLARE
    orphaned_property_owners INT;
BEGIN
    SELECT COUNT(*) INTO orphaned_property_owners
      FROM catalog.properties p
      JOIN identity.users u ON u."Id" = p.owner_user_id
     WHERE u.b2c_object_id IN
       ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
       AND p.deleted_at IS NULL
       AND NOT EXISTS (
         SELECT 1 FROM identity.users survivor
          WHERE survivor.email = u.email
            AND survivor.b2c_object_id NOT IN
              ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
            AND survivor.deleted_at IS NULL
       );
    IF orphaned_property_owners > 0 THEN
        RAISE EXCEPTION
          'F11.7.7.10 blocked: % catalog.properties row(s) owned by a DevAuth persona would be orphaned. Provision the real-Entra survivor first (sign in as niroshanaks@gmail.com against staging) or roll forward with an explicit orphan-property policy.',
          orphaned_property_owners;
    END IF;
END $$;

-- Soft-delete the three persona user rows.
UPDATE identity.users
   SET deleted_at = NOW(),
       deleted_by = NULL,   -- system-initiated
       updated_at = NOW()
 WHERE b2c_object_id IN
   ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND deleted_at IS NULL;

-- Soft-delete their memberships (the Slice5b migration seeded some;
-- F11.7.5.10a's widened bootstrap may have added more).
UPDATE identity.tenant_memberships tm
   SET deleted_at = NOW(),
       updated_at = NOW()
  FROM identity.users u
 WHERE tm.user_id = u."Id"
   AND u.b2c_object_id IN
     ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND tm.deleted_at IS NULL;
```

**Bypass domain method rationale**: as in F11.7.6.4, the raw SQL bypasses `User.Deactivate(reason, actorId)` because:
- Deactivate raises `UserDeactivated`; no handler registered for the system-initiated heal.
- Deactivate needs a non-nullable `actorId Guid`; F11.7.7.10 has no actor.
- One-shot data-heal, not part of normal lifecycle.

### §7.3 Catalog migration — repoint property owners

```sql
-- F11.7.7.10b — for every catalog.properties row currently owned by a
-- DevAuth persona, repoint owner_user_id to the survivor real-Entra
-- row with the same email. Precondition already validated by 7.2's
-- guard block; this is the write.

UPDATE catalog.properties p
   SET owner_user_id = survivor."Id",
       updated_at = NOW()
  FROM identity.users devauth
  JOIN identity.users survivor ON survivor.email = devauth.email
 WHERE p.owner_user_id = devauth."Id"
   AND devauth.b2c_object_id IN
     ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND survivor.b2c_object_id NOT IN
     ('dev-owner-00000000', 'dev-guest-00000001', 'dev-admin-00000002')
   AND survivor.deleted_at IS NULL
   AND p.deleted_at IS NULL;
```

### §7.4 What survives unchanged

Deliberately NOT repointed:

- `booking.bookings.guest_user_id` — historical bookings under DevAuth Guest keep their guest user id. The soft-deleted row still resolves as a uuid; the reader path uses `IgnoreQueryFilters()` for admin views. Historic bookings are read-only anyway.
- `reviews.reviews.guest_user_id` — same reasoning.
- `messaging.threads.guest_user_id` — same.
- `identity.audit_log.actor_user_id` — historical audit rows must never be rewritten. Auditor invariant.

### §7.5 Down migration

Both migrations have a no-op `Down` with a comment: `-- Cannot reverse: soft-deletes lose the "which oid did the row have when it was live" information. Restore via pg_dump if a rollback is needed.`

### §7.6 Reversibility for the staging window

Same pattern as F11.7.6.4:
1. Before applying F11.7.7.10 on staging: `pg_dump --schema=identity --schema=catalog --data-only --table=identity.users --table=identity.tenant_memberships --table=catalog.properties > f11_7_7_pre_heal.sql`.
2. If a regression: truncate + restore from the dump, revert the F11.7.7.9 + F11.7.7.10 deploys.

### §7.7 Prod safety

Prod's `identity.users` should have zero `dev-*` prefixed OIDs (DevAuth was `false` on prod since OPS.M.0). The migration runs on prod but affects zero rows. The catalog UPDATE affects zero rows. No prod risk.

---

## §8 Residual risks + out-of-scope

### Residual risks

1. **`Ops:InitialPlatformAdminEmail` becomes an auth-critical config key.** Anyone with Key Vault write access can grant themselves PA by flipping the email + waiting for the next boot. Mitigation: Key Vault access is already three-named-humans-only per OPS.M.8. Additional detection: the hosted-service writes an audit row with `source: "initial-platform-admin-hosted-service"` — SIEM can alert on any promote from that source that isn't the pre-known first-PA email.
2. **Staging operator setup for new tenants is friction-heavy.** Real Stripe sandbox onboarding must complete for every new staging tenant that needs `Active` state. Mitigation: OPS.M.12 unbreaks with a Stripe test-clock fixture per its plan.
3. **Same-day 24h-completion-sweep UI verification is gone.** The `backdate-checked-out-at` button no longer exists. Same-day verification requires operator SQL:
   ```sql
   UPDATE booking.bookings SET checked_out_at = NOW() - INTERVAL '25 hours' WHERE "Id" = '<booking-id>' AND status = 'CheckedOut';
   ```
   Documented in `docs/runbooks/slice6-seed.md` (add §5).
4. **Test cross-tenant matrix and Entra JWT smoke fidelity remains PARTIAL** post-F11.7.7. The `JwtSmokeTests.cs:65-80` scaffold is still `[Skip]`'d pending env-var wiring; the primary matrix path is `TestAuthenticationHandler`, not real JWT. This gap existed before F11.7.7 and is unaffected. OPS.M.10.1 already owns closing it.
5. **`Ops:InitialPlatformAdminEmail` mismatch on prod deploy.** If prod's Bicep accidentally gains this env var (misconfig at deploy time), a prod-user could be silently promoted on next boot. Mitigation: F11.7.7.6's Bicep guard: `Ops__InitialPlatformAdminEmail` value is ONLY set for `!isProd`; for `isProd`, the env var is entirely absent from `apiEnv[]`. Bicep `if (isDev || isStaging)` wrapping. Documented in the CI runbook.
6. **`InitialPlatformAdminPromoter` startup order.** The hosted service must run AFTER `db.Database.MigrateAsync()`. `IHostedService` starts run in registration order; F11.7.7.6 registers it AFTER `VrBook.Migrator`'s migration flow finishes. Cross-service startup coordination lives in `Program.cs`. Note that in `VrBook.Api` the migrator is NOT the api itself — migrations are applied by `VrBook.Migrator` in a separate container app job. So the promoter must be idempotent + tolerate the case where `identity.users` doesn't yet have the target email row. **Handled**: if the row is missing, the promoter logs a warning ("target row not yet provisioned; will retry on next boot") and no-ops. The user's next real-Entra sign-in provisions the row; the NEXT api container restart runs the promoter and it succeeds.

### Out of scope

- **Renaming `IGuestTenantResolver` → `IResourceTenantResolver`.** F11.7.5 residual risk item; still deferred.
- **Cross-schema FK enforcement** (`booking.bookings.guest_user_id REFERENCES identity.users`). Same deferral as F11.7.6 §8.
- **Deleting the immutable `Slice5b_DevAuth_Default_Tenant_Membership` migration.** Never delete a migration; its `Up` becomes a no-op the moment the DevAuth persona rows are gone.
- **Prod bootstrap changes.** Prod's first PA still uses the three-named-humans manual SQL runbook. F11.7.7 doesn't touch that policy.
- **Retiring the `PlatformAdmin` role.** ADR-0014 stays; the role is preserved.

---

## §9 Open questions (for user confirmation before execution)

1. **`Ops:InitialPlatformAdminEmail` for staging = `niroshanaks@gmail.com`?** Default: yes; matches the user's real Entra sign-in email. Confirm this is the correct choice for staging's operator identity.
2. **Bicep guard shape**: should prod entirely OMIT the env var (proposed), or should it be present but set to an empty string (belt-and-braces so the hosted-service branch is uniform)? Proposed: OMIT — the promoter reads `null` from `IConfiguration` and no-ops without a code branch.
3. **`InitialPlatformAdminPromoter` failure mode**: if the DB row exists but is soft-deleted (edge case), should the promoter (a) revive + promote, (b) provision a fresh row, or (c) fail loudly? Proposed: (c) fail loudly; a soft-deleted row indicates operational anomaly.
4. **Test project header name**: `X-Test-Persona` (proposed) or `X-VrBook-Test-Persona` (more distinctive but longer)? Proposed: `X-Test-Persona` — never seen in production requests.
5. **Order of F11.7.7.10 relative to F11.7.7.9**: proposed puts the data-heal AFTER the code deletion (F11.7.7.9 → F11.7.7.10). This means for the brief interval between F11.7.7.9 landing and F11.7.7.10 landing on staging, the DevAuth persona rows still exist in `identity.users` but no code references them. Acceptable (they're just idle rows). Alternative: F11.7.7.10 first (data gone), then F11.7.7.9 (code gone). Both valid; proposed order matches the "code-first, data-heal-second" pattern F11.7.6 established.

---

## §10 Time estimate

| Commit | Estimate |
|---|---|
| F11.7.7.1 (arch tests RED) | 45 min |
| F11.7.7.2 (TestAuthenticationHandler scaffold) | 30 min |
| F11.7.7.3 (IdentityApiFixture migration) | 60 min |
| F11.7.7.4 (HasTenantRoleTests unit) | 20 min |
| F11.7.7.5 (TwoTenantApiFixture migration) | 60 min |
| F11.7.7.6 (InitialPlatformAdmin promoter + Bicep) | 90 min |
| F11.7.7.7 (web deletes) | 45 min |
| F11.7.7.8 (Bicep + env deletes) | 20 min |
| F11.7.7.9 (production `src/` deletes → arch tests GREEN) | 45 min |
| F11.7.7.10 (data-heal migrations) | 60 min |
| F11.7.7.11 (close-out doc) | 30 min |
| **Total** | **~8 hours execution + CI wait between each push** |

---

## §11 Close-out

_To be filled in after CI green on F11.7.7.10._
