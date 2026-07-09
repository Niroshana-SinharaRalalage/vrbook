# web/tests/e2e/ — Playwright E2E suite

Slice OPS.2 (`docs/OPS_2_PLAYWRIGHT_PLAN.md`) lands the E2E harness covering ~30 scenarios across three product surfaces (guest, tenant admin, platform admin) + an anonymous smoke sweep.

## Directory shape

| Path | Purpose | Lands in |
|---|---|---|
| `anonymous/*.smoke.spec.ts` | 5 anonymous scenarios. Matched by `smoke` project. | OPS.2.3 |
| `guest/*.spec.ts` | 10 authed guest scenarios. `guest-authed` project. | OPS.2.4 |
| `owner/*.spec.ts` | 9 owner / tenant-admin scenarios. `owner-authed` project. | OPS.2.5 |
| `platform-admin/*.spec.ts` | 6 platform-admin scenarios. `platform-admin-authed` project. | OPS.2.6 |
| `fixtures/personas.ts` | Persona records + env-var reads. | OPS.2.2 |
| `fixtures/auth.fixture.ts` | Storage state overlay fixture. | OPS.2.2 |
| `support/pageObjects/*.ts` | Thin POMs. Incremental. | OPS.2.3–OPS.2.6 |
| `support/testTenant.ts` | `uniqueRunId()`, `createTestProperty(page)`. | OPS.2.2 |
| `global-setup.ts` | MSAL sign-ins + storage state writes. | OPS.2.2 |
| `.auth/` | **Gitignored.** Session-local storage state. | OPS.2.2 |

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

1. **`cd-staging-web.yml` → `playwright-smoke` job** — anonymous smoke, blocking on every push (from OPS.2.3). Chromium only.
2. **`.github/workflows/nightly-playwright.yml`** — cron `0 6 * * *` UTC. Three authed projects. `continue-on-error: true` for OPS.2 landing window; blocking-flip target OPS.2.9.

## Auth model

Real MSAL redirect against staging Entra CIAM (owner-locked in plan §5-Q2-a). Three Entra-local personas, NO MFA, NO CA, seeded via `SeedE2eBackfill` (M.22.6 pattern).

**Google OAuth is out of scope for Playwright** (owner-locked in ADR-0019, plan §5-Q2-a rationale + §6). Weekly manual walk in `docs/runbooks/social_idp_setup.md`.

## Test data reset

`runId` namespacing (owner-locked in plan §5-Q3-a). Every mutating test stamps `runId = <ISO-timestamp>-<random-6>` on property titles + booking `guest_notes`. Isolated `e2e-tenant` scopes all pollution. Nightly janitor deferred (POLISH.6).

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
