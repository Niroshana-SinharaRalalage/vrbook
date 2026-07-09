# OPS.2 — Playwright E2E Suite Plan

- **Status:** LOCKED — §5 policy questions all locked to architect recommendation per owner directive 2026-07-09 (adopt architect answers directly per `feedback_technical_decisions_are_architect_call`). §6 technical calls are the architect's own.
- **Date:** 2026-07-09.
- **Author:** Platform Enterprise Architect (agent) via OPS.2 planning consult.
- **Trigger:** MASTER_PLAN row 17 is the next slot; OPS.1 (Pact) shape-complete 2026-07-09 with follow-ups filed as OPS.1.9. `EXECUTION_PLAN.md` §8 mandates "Playwright E2E suite (~30 scenarios) — F1.1 deliverable" against the `BookingApp_Proposal.md` §18.1 test pyramid targets. `web/playwright.config.ts` skeleton + `@playwright/test@1.49.0` devDep landed in Slice 0; `web/tests/e2e/` is empty. Owner-locked constraints from ADR-0016 (admin Entra-local only) and ADR-0017 (admin pre-seed required) mean the suite MUST NOT invent a fake-auth backdoor on the admin surface.
- **Predecessors:** [`OPS_1_PACT_PLAN.md`](OPS_1_PACT_PLAN.md) + [`OPS_1_CLOSE_OUT.md`](OPS_1_CLOSE_OUT.md), [`OPS_M_22_ADMIN_PRESEED_PLAN.md`](OPS_M_22_ADMIN_PRESEED_PLAN.md) + [`OPS_M_22_CLOSE_OUT.md`](OPS_M_22_CLOSE_OUT.md), [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md), [`docs/adr/0016-admin-vs-social-idp-surface-split.md`](adr/0016-admin-vs-social-idp-surface-split.md), [`docs/adr/0017-admin-preseed-required.md`](adr/0017-admin-preseed-required.md).

---

## §0 What we're doing + why now

**OPS.2 requirement (`EXECUTION_PLAN.md` §8 + `BookingApp_Proposal.md` §18.1):** ship a Playwright E2E suite of ~30 scenarios covering the happy paths + key failure paths across the three product surfaces (guest, tenant admin, platform admin). Suite runs in CI on every push to develop, catches SPA-level regressions before staging deploy is called ✅, and does NOT loosen the Entra-only admin auth surface locked by ADR-0016 + ADR-0017.

**Why now:** Phase 1 (Slices 0–7) shipped continuously without an E2E harness; every regression to date has been caught either by Vitest, Pact (post-OPS.1), or the deployed smoke curl-sweep. The curl smoke covers *reachability* but not browser-level flow correctness — a route that returns 200 but renders a broken React tree passes smoke. That gap is exactly what E2E catches. Landing OPS.2 before Slice 8 (Phase 3 hotel-style rooms) means Slice 8's new checkout branches inherit E2E coverage from day one.

**The suite covers the seven §18.2 flows AND the surrounding guest / owner / admin day-in-the-life paths.** Pact (OPS.1) locks the *request/response shape* between FE and API; Playwright locks the *user journey* on top. Zero overlap.

**Session budget:** 2–3 sessions.

---

## §1 Sub-commit sequence

Eight sub-commits mirroring the OPS.1 / OPS.M.22 shape. NO merged-red on develop.

| # | Slice | Scope |
|---|---|---|
| **OPS.2.1** | Plumbing + docs + directory skeleton | Add `docs/OPS_2_PLAYWRIGHT_PLAN.md` (this doc). Create `web/tests/e2e/` with `README.md`, `.gitkeep` on empty subfolders (`anonymous/`, `guest/`, `owner/`, `platform-admin/`, `support/`, `fixtures/`). Add `.gitignore` entries for `web/tests/e2e/.auth/`, `web/playwright-report/`, `web/test-results/`. Zero CI impact; zero tests fire. |
| **OPS.2.2** | Base infrastructure: personas + auth fixture + POM shell | `web/tests/e2e/fixtures/personas.ts` — three persona records reading passwords from `process.env.E2E_*_PASSWORD`. `web/tests/e2e/fixtures/auth.fixture.ts` — Playwright fixture that overlays storage state per project. `web/tests/e2e/global-setup.ts` — runs each persona MSAL sign-in once, writes `.auth/<persona>.storageState.json`. Base POMs. Update `web/playwright.config.ts`: `globalSetup` + three `-authed` projects + `setup` project. Bicep seeds three Entra CIAM personas + M.22 pre-seed rows via new `SeedE2eBackfill` service. Three new KV secrets `e2e-*-password` as `pending-identity-setup` placeholders. New `is_e2e` bool column on `identity.tenants`. |
| **OPS.2.3** | Anonymous smoke suite (5 scenarios) + CI wiring | 5 `.smoke.spec.ts` under `anonymous/`: home renders, property search, property detail by slug, unauthenticated quote calc, `/api/health` via browser context. New CI job `playwright-smoke` in `cd-staging-web.yml` after `smoke`. **Blocking from day one** (§5-Q4-a). Chromium only. |
| **OPS.2.4** | Guest authed flows — ~10 scenarios | `guest/*.spec.ts` covering §18.2 flows 1-tail, 4, 5, 7. Reuses `guest-authed` project. Nightly-informational. |
| **OPS.2.5** | Owner / tenant-admin flows — ~9 scenarios | `owner/*.spec.ts` covering §18.2 flows 1-head, 2, 3. `owner-authed` project. |
| **OPS.2.6** | Platform admin + auth edge cases — ~6 scenarios | `platform-admin/*.spec.ts`. Includes `/auth/admin-not-provisioned` + `/auth/admin-social-idp-rejected` end-to-end exercise. `platform-admin-authed` project. |
| **OPS.2.7** | Nightly workflow + runbook + arch tests | `.github/workflows/nightly-playwright.yml` cron `0 6 * * *`. `docs/runbooks/playwright-e2e-flake.md`. Cross-surface arch tests: (a) exactly 30 scenarios, (b) `.auth/` gitignored, (c) NO `[AllowAnonymous]` added to any admin controller during OPS.2, (d) NO test-only middleware registered in production `Program.cs`. |
| **OPS.2.8** | Close-out + ADR-0019 + MASTER_PLAN flip | `docs/OPS_2_CLOSE_OUT.md`. ADR-0019 `playwright-target-env-and-persona-strategy.md`. Flip MASTER_PLAN row 17 ✅. Update `EXECUTION_PLAN.md` §8. Update `CLAUDE.md` footer. |

**Total: 8 sub-commits, ~30 scenarios shipped.**

---

## §2 What survives, what needs new work

### Survives — no change

- **`web/playwright.config.ts` skeleton** — the two projects (`smoke`, `e2e`) + `PLAYWRIGHT_BASE_URL` clause survive. OPS.2.2 extends via three additional projects + `globalSetup`; nothing gets deleted.
- **Existing curl smoke sweep** in `cd-staging-web.yml`. Playwright smoke runs after curl smoke.
- **All production controllers** — no `[AllowAnonymous]` added, no route decorators changed. Arch test in OPS.2.7 pins this.
- **`UserProvisioningMiddleware` admin-gate** (M.22.4) — untouched. Playwright's admin personas ARE pre-seeded per §6.
- **`AdminSocialIdpRejectionMiddleware`** (M.12 Layer 2) — untouched. Explicitly exercised end-to-end.
- **`web/vitest.setup.ts` + `web/tests/pacts/*`** — different test surface, different config.

### Needs new work

- `web/tests/e2e/` directory tree — 5 subfolders + fixtures + support.
- Three persona records with env-var-injected passwords.
- Playwright auth fixture that overlays storage state per project.
- `global-setup.ts` running three real MSAL sign-ins per session.
- Bicep + `10-store-secrets.ps1` seed of three e2e password KV secrets.
- Migrator `SeedE2eBackfill` service (mirrors M.22.6 `SeedPlatformAdminsBackfill`).
- New `is_e2e` bool column on `identity.tenants` + migration.
- Two CI job wire-ups (`playwright-smoke` blocking + `nightly-playwright.yml` informational).
- `docs/runbooks/playwright-e2e-flake.md` + `docs/adr/0019-...`.

---

## §3 New surface

### Test tree

| Path | Purpose |
|---|---|
| `web/tests/e2e/anonymous/*.smoke.spec.ts` | 5 anonymous scenarios. `smoke` project. |
| `web/tests/e2e/guest/*.spec.ts` | 10 authed guest. `guest-authed` project. |
| `web/tests/e2e/owner/*.spec.ts` | 9 owner. `owner-authed` project. |
| `web/tests/e2e/platform-admin/*.spec.ts` | 6 platform-admin. `platform-admin-authed` project. |
| `web/tests/e2e/fixtures/personas.ts` | Persona records + env-var reads. |
| `web/tests/e2e/fixtures/auth.fixture.ts` | Storage state overlay fixture. |
| `web/tests/e2e/support/pageObjects/*.ts` | Thin POMs. |
| `web/tests/e2e/support/testTenant.ts` | `uniqueRunId()`, `createTestProperty`, `createTestBooking`. |
| `web/tests/e2e/support/stripeTestCards.ts` | Card constants. |
| `web/tests/e2e/global-setup.ts` | MSAL sign-ins + storage state writes. |
| `web/tests/e2e/.auth/` | Gitignored. Holds `<persona>.storageState.json`. |
| `web/tests/e2e/README.md` | Local + CI invocation. |

### `web/playwright.config.ts` update (OPS.2.2)

Adds `setup` project + `guest-authed`, `owner-authed`, `platform-admin-authed` each with `dependencies: ['setup']` + `storageState: 'tests/e2e/.auth/<persona>.storageState.json'`. Retires `e2e` catch-all project.

### CI wiring — `cd-staging-web.yml`

New job `playwright-smoke` after `smoke`. Blocking from OPS.2.3. Chromium only. `PLAYWRIGHT_BASE_URL` points at deployed staging web FQDN.

### CI wiring — `.github/workflows/nightly-playwright.yml` (new, OPS.2.7)

Cron `0 6 * * *` + `workflow_dispatch`. Fetches persona passwords from KV via `az keyvault secret show`. `continue-on-error: true` for the OPS.2 landing window — blocking-flip target OPS.2.9 after ~2 weeks of soak.

---

## §4 Risks + mitigations

| # | Risk | L | I | Mitigation |
|---|---|---|---|---|
| 1 | Entra CIAM sign-in via MSAL redirect blocks Playwright | H | H | Real Entra login pages ARE Playwright-drivable. Prototype in OPS.2.2; fallback to msal-node `acquireTokenByUsernamePassword` (§5-Q2-b) if needed. |
| 2 | Staging Entra CIAM has MFA / CA → Playwright can't complete sign-in | Certainty | H | Test personas configured Entra-local, NO MFA, NO CA. Runbook documents portal exclusions. Prod tenant never enables these personas. |
| 3 | Google OAuth flow can't be Playwright-driven | Certainty | M | Explicit carve-out. ADR-0019 records. Weekly manual walk in `social_idp_setup.md`. |
| 4 | Test data side-effects persist on staging → nightly runs pollute | H | M | `runId` namespacing + `e2e-tenant` isolation + backlog item POLISH.6 (janitor). |
| 5 | Stripe test-mode webhook delivery lag → "Tentative" too early | H | H | Poll `/account/bookings` API with 15s window + 500ms interval. |
| 6 | Storage-state files accidentally committed → credentials leaked | M | H | `.gitignore` entry lands OPS.2.1 (BEFORE state generated). Arch test pins the ignore. Pre-commit hook stub in runbook. |
| 7 | Password env vars accidentally logged in CI | M | H | GH Actions `::add-mask::` on every password. `personas.ts` NEVER logs. |
| 8 | Playwright browser install slows CI | M | L | Chromium-only + cache action. |
| 9 | MSAL redirect chain breaks in headless mode | M | H | MSAL redirect is headless-safe. Fallback to msal-node fixture if hard break surfaces. |
| 10 | Nightly runs collide with operator manual testing | L | L | `runId` namespacing. |
| 11 | Someone adds `[AllowAnonymous]` for e2e access | L | H | Arch test in OPS.2.7 + runbook callout. |
| 12 | Playwright report artifact spills sensitive DOM | M | M | Screenshots only on failure. `github-actions` retention 7d smoke, 14d nightly. |
| 13 | `e2e-tenant` seed accidentally runs in prod | L | H | Bicep gate `bootstrapE2eTenantEnabled: bool` — prod always `false`. Arch test asserts prod param. |
| 14 | Session budget overruns because Entra + storage-state debugging | M | M | Fallback (§5-Q2-b) documented in advance. |

---

## §5 Owner-lock questions (POLICY only — all locked per owner directive 2026-07-09)

### Q1 — [POLICY] Target env: staging only, local only, or both

- **(a) — Locked.** Staging is canonical; suite runs against deployed staging web + staging API + real Entra CIAM. Local dev CAN run same specs via `PLAYWRIGHT_BASE_URL=http://localhost:3000` — opt-in convenience, no local-only assertions in the shape.
- (b) Local only. Rejected: doubles setup surface.
- (c) Two separate suites. Rejected: 2× maintenance.

### Q2 — [POLICY] Auth in Playwright: real MSAL redirect vs programmatic token

- **(a) — Locked.** Real MSAL redirect through staging Entra CIAM, driven by Playwright's browser context. Persona = Entra-local email+password, no MFA, no social IdP. If Q2-a hits a hard block during OPS.2.2 discovery, fall back to (b).
- **(b) — Fallback only.** msal-node `acquireTokenByUsernamePassword` (ROPC grant). Requires enabling ROPC on staging Entra (portal-level config, staging only, never prod).
- (c) `[AllowAnonymous]` on admin API for e2e. **REJECTED — violates ADR-0016.**
- (d) Locally-signed JWT + JWKS-mock. **REJECTED — production-config-risk.**

### Q3 — [POLICY] Test data reset strategy

- **(a) — Locked.** Isolated `e2e-tenant` + `runId` namespacing. Cleanup deferred to nightly janitor (POLISH.6, out of OPS.2 scope).
- (b) `POST /admin/e2e/reset` endpoint. Rejected: permanent test-only production surface.
- (c) Direct Postgres from Playwright. Rejected: DB creds in CI + maintenance burden.
- (d) Delete + re-seed every run. Rejected: slow + orphans in-flight bookings.

### Q4 — [POLICY] CI failure gate policy

- **(a) — Locked.** Anonymous smoke blocking on every push from OPS.2.3. Authed nightly informational for OPS.2 landing; blocking-flip target OPS.2.9 after 2 weeks of stable soak.
- (b) Everything blocking immediately. Rejected: authed flakiness guaranteed.
- (c) Everything informational forever. Rejected: not a gate.

### Q5 — [POLICY] Playwright browser matrix

- **(a) — Locked.** Chromium only. Firefox + WebKit deferred to OPS.2.10 candidate.
- (b) All three browsers. Rejected: 3× runtime.

### Q6 — [POLICY] Nightly workflow trigger

- **(a) — Locked.** Cron `0 6 * * *` (06:00 UTC). `workflow_dispatch` for manual re-runs.
- (b) Every push to develop. Rejected: 30 authed scenarios add ~10 min per CI.

---

## §6 Technical answers I resolve directly (architect's call)

- **Playwright version:** stay on `@playwright/test@1.49.0`.
- **Browser install strategy:** `--with-deps chromium` + `actions/cache`.
- **`webServer` config:** stays `undefined` — target is deployed staging FQDN, not localhost.
- **Playwright projects:** `smoke` + `guest-authed` + `owner-authed` + `platform-admin-authed` + `setup`. Retire the old blanket `e2e`.
- **Storage state format:** JSON via `context.storageState()`. Session-local; regenerated in CI on every run; regenerated locally if >6h old.
- **Persona shape:**
  - `E2E_GUEST`: `e2e-guest@vrbook.test`, guest flow, lazy-provisioned on first sign-in.
  - `E2E_OWNER_TENANT_ADMIN`: `e2e-owner@vrbook.test`, admin flow, pre-seeded with `tenant_memberships` `role='tenant_admin'` on `e2e-tenant`.
  - `E2E_PLATFORM_ADMIN`: `e2e-platform-admin@vrbook.test`, admin flow, pre-seeded with `is_platform_admin=true` + `pre_seeded_at=NOW()`.
- **Password source:** `process.env.E2E_*_PASSWORD`. CI fetches from KV via `az keyvault secret show` + `$GITHUB_ENV`. Local devs `.env.local` (gitignored) or manual KV pull.
- **`e2e-tenant` seed:**
  - Bicep param `bootstrapE2eTenantEnabled: bool` (default `false`; staging `true`; prod `false`).
  - `SeedE2eBackfill` service in Migrator (mirrors `SeedPlatformAdminsBackfill` shape). Idempotent.
  - `is_e2e` bool column on `identity.tenants` — NULL for real; `true` for e2e.
- **Page object shape:** thin POMs — no deep inheritance. Semantic locators preferred; `data-testid` only where CSS is brittle.
- **Runner parallelism:** `fullyParallel: true` local; CI worker cap 1.
- **Retry policy:** `retries: process.env.CI ? 2 : 0`. Trace on first retry.
- **Reporter:** `[['github'], ['html', { open: 'never' }]]` CI; `html` local.
- **Google OAuth carve-out:** documented in ADR-0019. Weekly manual walk in `social_idp_setup.md`.
- **Stripe test cards:** `4242 4242 4242 4242` happy path; `4000 0000 0000 9995` auth-required edge case.
- **`AdminSignUpSignIn` flow:** currently NOT created per CLAUDE.md footer. OPS.2.2 verifies during global-setup implementation; if flow still absent, OPS.2 blocks on it being created (M.22 close-out drift, not OPS.2 scope).
- **30-count arch test:** parses `web/tests/e2e/**/*.spec.ts` for `test(` + `test.describe(` and asserts count == 30.

---

## §7 Close-out checklist

- [ ] `web/tests/e2e/` directory tree matches §3 shape.
- [ ] `web/tests/e2e/.auth/` gitignored + arch test pins the ignore.
- [ ] Three persona records + env-var password reads + no committed credentials.
- [ ] `global-setup.ts` runs three MSAL sign-ins + writes storage state.
- [ ] `web/playwright.config.ts` updated: `setup` + three `-authed` projects.
- [ ] Exactly 30 scenarios ship; arch test pins the count.
- [ ] `cd-staging-web.yml` `playwright-smoke` job blocking + runs against deployed staging.
- [ ] `.github/workflows/nightly-playwright.yml` scheduled + informational.
- [ ] `docs/runbooks/playwright-e2e-flake.md` covers triage.
- [ ] ADR-0019 committed.
- [ ] `docs/OPS_2_CLOSE_OUT.md` written.
- [ ] MASTER_PLAN row 17 flipped ✅.
- [ ] EXECUTION_PLAN §8 OPS.2 line updated.
- [ ] CLAUDE.md footer refreshed.
- [ ] Cross-surface arch tests: (a) 30 scenarios exactly, (b) `.auth/` gitignored, (c) no new `[AllowAnonymous]`, (d) no test-only middleware in production `Program.cs`, (e) `is_e2e` never true in prod appsettings.
- [ ] Bicep + `10-store-secrets.ps1` seed three e2e password KV secrets as placeholders.
- [ ] `SeedE2eBackfill` idempotent + Down-safe.
- [ ] Operator staging walk: creates three Entra CIAM personas, sets KV passwords, runs nightly manually via `workflow_dispatch`, 30/30 green.
- [ ] Runbook link in `social_idp_setup.md` for Google OAuth carve-out.
- [ ] Playwright reports archived (7d smoke, 14d nightly retention).

---

Ready to execute Slice OPS.2.1.
