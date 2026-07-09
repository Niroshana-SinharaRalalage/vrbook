# Slice OPS.M.22 — Close-Out

**Status:** shipped end-to-end (8 sub-commits landed 2026-07-08 → 2026-07-09).
**Slice plan:** [`OPS_M_22_ADMIN_PRESEED_PLAN.md`](OPS_M_22_ADMIN_PRESEED_PLAN.md).
**Predecessors:** [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md) (admin-vs-social split — M.22 layers ON TOP: admins are Entra-local per M.12; M.22 adds the pre-seed requirement so admin lazy provisioning is refused).
**ADR:** [`0017-admin-preseed-required.md`](adr/0017-admin-preseed-required.md).

---

## 1. What shipped

### The gap this closed

Before M.22, the M.13 stack shipped **lazy provisioning** for every authenticated request — `UserProvisioningMiddleware` inserted an `identity.users` row on first sign-in regardless of who the caller was. Right for guests, wrong for admins:

- The very first admin sign-in on a fresh environment produced a `identity.users` row with `is_platform_admin=false` and zero memberships. Nothing distinguished "the owner just onboarded" from "a random Entra local signup."
- Admin authority requires an out-of-band `SeedTenantMembership` call OR SQL runbook after the fact — chicken-and-egg for the first admin.
- Nothing forced operator intent BEFORE first sign-in.

Owner policy locked 2026-07-07 (plan §7):

- Platform Admin + Tenant Admin — MUST have `identity.users` row **pre-seeded by an operator** BEFORE first sign-in.
- Guests — self-serve, lazy provisioning unchanged.

M.22 encodes that policy at **four surfaces**:

- **DB shape** — `identity.users.pre_seeded_at timestamptz NULL`. Non-null = operator vouched.
- **Application** — `SeedAdminUserCommand` + handler; new `POST /api/v1/admin/platform/users/seed` (PlatformAdmin).
- **Middleware gate** — `UserProvisioningMiddleware` reads the Entra flow marker; admin-flow tokens on unknown emails REFUSE with `AdminAccountNotProvisionedException` (401).
- **SPA UX** — `/auth/admin-not-provisioned` page + `AdminAuthGuard` extension route rejected admins to a clear "contact your operator" screen.

### Backend

**Domain + persistence (M.22.2):**
- `User.PreSeededAt` (nullable `DateTimeOffset`) property + `User.PreSeedForOperator(email, displayName, seededAt)` static factory + `User.CompletePreSeedLink()` idempotent linker method.
- `UserConfiguration` maps `pre_seeded_at` column.
- Migration `20260709010620_OpsM22_UsersAddPreSeededAtColumn` adds the nullable column.

**Application + endpoint (M.22.2):**
- `SeedAdminUserCommand` + `SeedAdminUserHandler` — auditable via `IAuditable` (`admin.pre-seed-user`); idempotent on normalized email; validates tenant ids exist before writing (transactional fail-fast); 409 on email collision with a NON-pre-seeded row; merges memberships including revival of soft-deleted rows (mirrors M.10.2 F11.3 pattern).
- Contract DTOs: `SeedAdminUserRequest`, `SeedAdminUserTenantMembership`, `SeedAdminUserResult` in `VrBook.Contracts.Dtos`.
- `UsersPlatformController.POST /api/v1/admin/platform/users/seed`, `[Authorize(Roles="PlatformAdmin")]`.

**Exception surface (M.22.2):**
- `AdminAccountNotProvisionedException : DomainException` — carries email + Entra oid (audit-log only; NOT in response body — account-enumeration protection).
- `ProblemTypes.AdminAccountNotProvisioned` URI constant.
- `ProblemDetailsConfig` maps to 401 with rule `admin_account_not_provisioned`. Registered BEFORE the generic `ForbiddenException` catch — Hellang first-match.

**Flow-marker accessor (M.22.3):**
- `ICurrentUser.EntraFlow` (nullable string) — the CIAM user-flow marker.
- `HttpCurrentUser.EntraFlow` reads `tfp` claim first (B2C legacy that Entra External ID inherited), falls back to `acr` (newer CIAM). Null when neither.
- `AnonymousCurrentUser.EntraFlow => null`.
- Claim-name constants: `HttpCurrentUser.EntraFlowTfpClaim`, `EntraFlowAcrClaim`.

**Middleware admin-gate (M.22.4):**
- `UserProvisioningMiddleware` reads `EntraExternalId:AdminFlowName` config + the token's `tfp`/`acr`; compares (case-insensitive).
- When `isAdminFlow=true`: query users by lower(email) filtered on `PreSeededAt != null`.
  - Hit → let `ProvisionOrLinkUserCommand` run Branch 2 (email match) which links the arriving oid to the pre-seeded row.
  - Miss + whitelisted path (`/api/v1/me` or `/api/v1/me/tenants`) → skip provisioning, call `next(ctx)` (SPA rejection page needs the whitelist).
  - Miss + non-whitelisted path → throw `AdminAccountNotProvisionedException` — structured log at Warning with email/oid/flow/path.
- Guest-flow tokens AND tokens with no flow marker fall through to the unchanged Branch 3 lazy-provision path. Self-serve guest signup is unaffected.
- Top-level `catch (AdminAccountNotProvisionedException) { throw; }` sibling BEFORE the generic swallowing catch so the 401 propagates to the ProblemDetails mapper.

**Migrator backfill (M.22.6):**
- New `VrBook.Migrator.SeedPlatformAdminsBackfill` service reads `Bootstrap:SeedPlatformAdmins` config, iterates each `{Email, DisplayName}`, idempotently ensures a pre-seeded platform-admin row exists. Single transaction wraps the run; on failure the environment is left unchanged.
- Per email: SELECT ... FOR UPDATE by lower(email); INSERT with `pre_seeded_at=NOW()` when absent; UPDATE only `is_platform_admin=TRUE` when the row exists but the flag isn't set. NEVER overwrites `pre_seeded_at` on an existing row.
- Emits an `identity.migration_audit` row per run.
- `Program.cs` invokes after every module's `MigrateAsync`. Empty list = one no-op log line.

### Frontend (SPA)

**Guard extension (M.22.7):**
- `useAdminGuard()` new `AdminGuardStatus` variant `admin-not-provisioned` + optional `signInEmail` from `email` / `preferred_username` claim.
- Detection heuristic: any 401 OR 403 from `/api/v1/me` on an otherwise-authenticated session. Deterministic because the M.22.4 middleware whitelist allows /me through, and `GetMeHandler` throws `ForbiddenException` only when `UserId` is null (no DB row).

**Rejection page (M.22.7):**
- `/auth/admin-not-provisioned?email=<value>` — counterpart to the M.12.7 social-idp rejection page.
- Copy: "Your account hasn't been provisioned yet." Displays the signed-in email inline (helps operator find the right seed hint). Includes actionable snippets for both operator seeding paths (M.22.5 `vrbook-admin.ps1` and M.22.6 Bicep `seedPlatformAdmins`).
- "Sign out and try again" CTA + "Go to the homepage instead" link.

**AdminAuthGuard extension (M.22.7):**
- New effect branch redirects to `/auth/admin-not-provisioned?email=…` (email omitted when the claim is absent). Same shape as the M.12.7 social-idp redirect.
- Guard renders "Checking sign-in…" during the transient state so admin children don't briefly flash.

### Infra

**Bicep** (`infra/main.bicep`):
- New `seedPlatformAdmins` array parameter (`env=staging` default: `[{ email: 'niroshanaks@gmail.com', displayName: 'Niroshana' }]`; dev + prod default empty).
- `backfillEnvVars` flattens the array to Container Apps env vars `Bootstrap__SeedPlatformAdmins__N__Email` / `_DisplayName`. Only wired to the migrator job (not api/workers).
- `migratorEnvVars = concat(apiEnvVars, backfillEnvVars)` keeps the api/workers clean of config they don't consume.

### Operator tooling

**`infra/scripts/vrbook-admin.ps1`** (M.22.5) — SQL-via-psql script with `-Action` verbs:
- `seed-platform-admin -Email X -DisplayName Y` — inserts + grants PA.
- `seed-tenant-admin -Email X -DisplayName Y -TenantId Z [-IsPrimary $true]` — inserts + tenant_admin membership; validates tenant exists.
- `list` — pretty-printed table of pre-seeded admins.
- `revoke -Email X` — drops PA + soft-deletes tenant_admin memberships.
- Fetches connection string from `kv-vrbook-<env>` via `az keyvault secret show`; refuses to run if the value is a `pending-…` placeholder OR doesn't include `Database=vrbook` (INFRA.1 A11 followup).
- Every SQL run is transactional with a `identity.migration_audit` row per action (operator + email + timestamp).

**Deleted** — `infra/scripts/grant-self-admin.ps1` (M.22.5). Wrote to Entra External ID extension attributes M.15 stopped reading + M.21 removed from the DB. Post-M.21 dead code.

### Config

New keys (env vars on the Container App):
- `EntraExternalId:AdminFlowName` — the CIAM user-flow marker value the middleware compares against. Operator sets to whatever `tfp`/`acr` the admin flow emits. Absent = admin-gate inert (single-flow tenants continue lazy-provisioning).
- `Bootstrap:SeedPlatformAdmins:[{Email, DisplayName}]` — Bicep-driven backfill list; migrator reads on every deploy.

### Arch tests

`OpsM22_AdminPreSeedShapeTests` (M.22.1) — 5 facts pinned intentionally RED on the M.22.1 landing; all GREEN by M.22.4:

1. `SeedAdminUserCommand` type exists (flipped M.22.2).
2. `AdminAccountNotProvisionedException` type exists AND `ProblemTypes.AdminAccountNotProvisioned` constant exists (flipped M.22.2).
3. `HttpCurrentUser.EntraFlow` public read-only string property (flipped M.22.3).
4. `UserProvisioningMiddleware` source references `EntraFlow`, `PreSeededAt`, `AdminAccountNotProvisionedException`, `AdminFlowName` — 4 load-bearing markers a refactor can't silently drop (flipped M.22.4).
5. A migration file references the `pre_seeded_at` column literal (flipped M.22.2).

`AdminAuthGuard.test.tsx` — 2 new tests (M.22.7) pinning the redirect with + without an email query param.

---

## 2. Sub-commit map

| # | Commit | Scope |
|---|---|---|
| M.22.1 | `b1f9c8e` | RED arch tests (5/5 fail) + CLAUDE.md + MASTER_PLAN.md refresh |
| M.22.2 | `538b834` | `SeedAdminUserCommand` + handler + migration + `pre_seeded_at` column + `AdminAccountNotProvisionedException` + `ProblemTypes` + endpoint (3/5 tests green) |
| M.22.3 | `5b2ae0d` | `HttpCurrentUser.EntraFlow` accessor + `ICurrentUser.EntraFlow` field (4/5 tests green) |
| M.22.4 | `a527444` | `UserProvisioningMiddleware` admin-gate + whitelist + arch-test widening (5/5 tests green) |
| M.22.5 | `3a14543` | `vrbook-admin.ps1` cmdlet + delete `grant-self-admin.ps1` |
| M.22.6 | `5948729` | Bicep-declarative backfill (`seedPlatformAdmins` + `SeedPlatformAdminsBackfill` service) |
| M.22.7 | `ea9b6aa` | SPA `/auth/admin-not-provisioned` page + `AdminAuthGuard` extension |
| M.22.8 | (this commit) | Close-out doc + ADR-0017 + MASTER_PLAN row flip |

---

## 3. Deviations from the plan

### 3.1 First-admin bootstrap uses direct DB SQL, not managed identity

Plan §6 said the `vrbook-admin --first` path would open a psql connection via **Container Apps managed identity**. Actual implementation opens psql with the KV-stored connection string (which is username + password, not managed identity token). Why:

- The Container App API is what carries the managed identity; the operator's local machine doesn't. Piping a workload-identity token to psql from a laptop is non-trivial.
- KV role-based access already gates who can pull `postgres-cs`. Any operator running `vrbook-admin` was already authorized to see that secret.
- Same audit trail either way — `identity.migration_audit` records operator + action.

### 3.2 SPA detection is heuristic (401/403), not a specific problem-type check

Plan §6 hinted at a whitelist-based /me response the SPA parses. Actual implementation checks the /me error status code (401 or 403) because:

- The M.22.4 middleware whitelists /me but `GetMeHandler` still throws `Forbidden` when `UserId` is null (the middleware skipped provisioning).
- No other code path produces a 403 from /me for an authenticated user (any provisioned user has `UserId` stamped) so the signal is deterministic in practice.
- Alternative — extending `UserDto` with an `isAdminNotProvisioned` flag — was rejected as more invasive for the same effective UX. Follow-up if we want a stronger contract.

### 3.3 No "First-admin --First" flag on the PowerShell cmdlet

Plan §1 mentioned a `--first` flag. Ended up unnecessary — the SQL-direct path is the ONLY path the cmdlet uses (there's no API-endpoint path in this deliverable), so the flag would be a no-op. Documented in-line.

### 3.4 Guest-flow bypass is claim-absence based, not explicit flow-name comparison

The middleware treats "no `tfp`/`acr` claim" and "flow marker doesn't match `AdminFlowName`" as identical — both skip the admin-gate. Simpler than plan §3's ordered fallback list. If a future CIAM policy emits a marker that doesn't match either config value, guest-flow behaviour holds (fail-safe).

### 3.5 Prod cutover checklist deferred

Plan §7 mentioned staging walk + prod cutover checklist. Staging walk is deferred to a follow-up (operator walk-through required). Prod cutover happens whenever the operator adds emails to Bicep + deploys — the mechanics are IaC, not a bespoke runbook.

---

## 4. Follow-ups (post-M.22)

- **OPS.M.22.9 — tenant-admin invite flow** (locked deferred in plan §5-Q5): thin wrapper on `SeedAdminUserCommand` for tenant_admin → tenant_admin invites. Not needed until we have real tenant admins onboarding sub-members.
- **OPS.M.22.10 — strong contract for /me during admin-not-provisioned**: extend `UserDto` or return a new `MeResponse` shape so the SPA has an explicit signal rather than the 401/403 heuristic.
- **OPS.M.22.11 — staging walk runbook**: operator walk-through validating the pre-seed → sign-in → admin surface loop end-to-end on staging.
- **OPS.M.12.11 (unblocked by M.22 but independent)** — get `idp` claim into the access token so federated Google guests classify as `provider=google` in the DB (today they land as `entra` because the access token doesn't carry idp — the ID token does).

---

## 5. Rollback

M.22 is additive to the M.13 shape. Rollback path:

1. `git revert ea9b6aa..b1f9c8e` (M.22.1–M.22.7 range) — restores the pre-M.22 middleware + SPA behaviour.
2. Migration `OpsM22_UsersAddPreSeededAtColumn` is Down-safe (drops the column). Run via `dotnet ef database update <PrevMigration>` on each env.
3. Bicep param `seedPlatformAdmins` becomes an unused array — no cleanup needed.

Middleware admin-gate is inert if `EntraExternalId:AdminFlowName` config is absent — a partial rollback (KV secret removal) neutralises the gate without touching code.

---

## 6. References

- [`OPS_M_22_ADMIN_PRESEED_PLAN.md`](OPS_M_22_ADMIN_PRESEED_PLAN.md) — pre-slice architect brief + owner-locked answers.
- [`adr/0017-admin-preseed-required.md`](adr/0017-admin-preseed-required.md) — this slice's architectural decision.
- [`adr/0016-admin-vs-social-idp-surface-split.md`](adr/0016-admin-vs-social-idp-surface-split.md) — M.12 admin-vs-social split (M.22 layers on top).
- [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md) — social IdP + admin split slice this one extends.
- `infra/scripts/vrbook-admin.ps1` — operator tool.
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SeedAdminUserCommand.cs` — handler.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs` — middleware admin-gate.
- `web/src/app/auth/admin-not-provisioned/page.tsx` — SPA rejection page.
