# web/tests/e2e/ — Playwright E2E suite

Slice OPS.2 (`docs/OPS_2_PLAYWRIGHT_PLAN.md`) lands the E2E harness covering ~30 scenarios across three product surfaces (guest, tenant admin, platform admin) + an anonymous smoke sweep.

## Directory shape

| Path | Purpose | Lands in |
|---|---|---|
| `anonymous/*.smoke.spec.ts` | 5 anonymous scenarios. Matched by `smoke` project. | OPS.2.3 |
| `guest/*.spec.ts` | 10 authed guest scenarios. `guest-authed` project. | OPS.2.4 |
| `owner/*.spec.ts` | 9 owner / tenant-admin scenarios. `owner-authed` project. | OPS.2.5 |
| `platform-admin/*.spec.ts` | 6 platform-admin scenarios. `platform-admin-authed` project. | OPS.2.6 |
| `fixtures/personas.ts` | Persona records + env-var password reads. | OPS.2.2 |
| `fixtures/auth.fixture.ts` | Re-injects the persona sessionStorage (MSAL token cache) on top of the project `storageState`. Import `test`/`expect` from here in authed specs. | OPS.2.2 |
| `support/pageObjects/*.ts` | Thin POMs (`BasePage`, `HomePage`). Incremental. | OPS.2.2+ |
| `support/testTenant.ts` | `RUN_ID`, `uniqueRunId()`, `scopedName()`, `E2E_TENANT_SLUG`. Data factories (`createTestProperty`/`createTestBooking`) land with the scenarios that consume them. | OPS.2.2 |
| `support/stripeTestCards.ts` | Stripe test-mode card constants. | OPS.2.2 |
| `global-setup.ts` | `setup` project — one real MSAL sign-in per persona; writes storage state + session snapshot. | OPS.2.2 |
| `.auth/` | **Gitignored.** Session-local `<persona>.storageState.json` + `.session.json`. | OPS.2.2 |

## Running locally

Smoke (no auth):
```bash
cd web
npx playwright test --project=smoke
```

Authed (requires KV persona passwords via `az keyvault secret show`):
```bash
export E2E_GUEST_PASSWORD=$(az keyvault secret show --vault-name kv-vrbook-staging --name e2e-guest-password --query value -o tsv)
export E2E_OWNER_PASSWORD=$(az keyvault secret show --vault-name kv-vrbook-staging --name e2e-owner-password --query value -o tsv)
export E2E_PLATFORM_ADMIN_PASSWORD=$(az keyvault secret show --vault-name kv-vrbook-staging --name e2e-platform-admin-password --query value -o tsv)
npx playwright test --project=guest-authed
```

## Running in CI

Two workflows:

1. **`cd-staging-web.yml` → `playwright-smoke` job** — anonymous smoke (5 specs), **blocking** on every push (shipped OPS.2.3). Runs after the curl `smoke` job; warms both web + API origins (staging scales to zero) before Playwright. Chromium only.
2. **`.github/workflows/nightly-playwright.yml`** — cron `0 6 * * *` UTC. Three authed projects. `continue-on-error: true` for OPS.2 landing window; blocking-flip target OPS.2.9.

## Auth model

Real MSAL redirect against staging Entra CIAM (owner-locked in plan §5-Q2-a). Three Entra-local personas, NO MFA, NO CA. The two admin personas (`e2e-owner`, `e2e-platform-admin`) are pre-seeded via `VrBook.Migrator.SeedE2EBackfill` (M.22.6 pattern, gated on `Bootstrap:E2e:Enabled`); the guest persona lazy-provisions on first sign-in like a real guest. Because MSAL uses `cacheLocation: 'sessionStorage'`, `global-setup.ts` persists the sessionStorage token cache alongside `storageState`, and `auth.fixture.ts` re-injects it — plain `storageState` alone would leave every authed API call with no bearer.

**Google OAuth is out of scope for Playwright** (owner-locked in ADR-0019, plan §5-Q2-a rationale + §6). Weekly manual walk in `docs/runbooks/social_idp_setup.md`.

## Seeded smoke fixture (OPS.2.3)

`SeedE2EBackfill` also seeds ONE deterministic public property (`slug='e2e-smoke-property'`, GUID `e2e00000-0000-0000-0000-000000000001`, `is_active=true`) + a single USD pricing plan under `e2e-tenant`. The anonymous detail-by-slug + quote smokes target it by the constants in `support/testTenant.ts` (`E2E_SMOKE_PROPERTY_SLUG` / `E2E_SMOKE_PROPERTY_ID`), which mirror the C# constants in `src/VrBook.Migrator/SeedE2EBackfill.cs` — **keep the two in sync**.

## Test data reset

`runId` namespacing (owner-locked in plan §5-Q3-a). Every mutating test stamps `RUN_ID` (via `scopedName()`) on property titles + booking notes. Isolated `e2e-tenant` scopes all pollution. Nightly janitor deferred (POLISH.6).

## Not tested here

- **Component rendering** — Vitest suite next to each component.
- **Request/response shape** — Pact contract tests (Slice OPS.1).
- **Load / performance** — Slice OPS.3 (k6).
- **Cross-browser rendering** — deferred to OPS.2.10 candidate. Chromium only in OPS.2.
- **Google OAuth** — manual weekly walk.

## References

- Plan: [`docs/OPS_2_PLAYWRIGHT_PLAN.md`](../../../docs/OPS_2_PLAYWRIGHT_PLAN.md)
- ADR-0019 (lands OPS.2.8).
- Runbook `docs/runbooks/playwright-e2e-flake.md` (lands OPS.2.7).
- Sibling contract tests: `contracts/pacts/README.md`.
