# Runbook — Playwright E2E flake / failure triage

Slice OPS.2.7. Covers the anonymous smoke (blocking, `cd-staging-web.yml` →
`playwright-smoke`) and the authed nightly (`nightly-playwright.yml`,
informational until the OPS.2.9 blocking-flip).

## Where things run

| Suite | Where | Gating |
|---|---|---|
| Anonymous smoke (5) | `cd-staging-web.yml` → `playwright-smoke`, after the curl `smoke` | **Blocking** |
| Authed guest/owner/platform-admin (26) | `nightly-playwright.yml`, cron `0 6 * * *` + `workflow_dispatch` | Informational |
| Suite invariants (count=31, `.auth/` gitignored) | `cd-staging-web.yml` → `frontend` → `check:e2e-suite` | **Blocking** |

Reports: download the `playwright-report-*` artifact from the run (7d smoke / 14d nightly).

## Decision tree

1. **`check:e2e-suite` failed (scenario count).** A spec was added/removed. If intended, update `EXPECTED_SCENARIOS` in `web/scripts/check-e2e-suite.mjs` and note it in the OPS.2 close-out. If not, restore the missing `test(`/`test.fixme(`.

2. **Anonymous smoke red, all 5 failing on `page.goto` timeout.** Staging cold start (scale-to-zero + burstable PG). The job's warm-up should prevent this; if it slipped through, re-run. Persistent → check `cd-staging-web.yml` `smoke`/convergence (OPS.INFRA.3) and staging health.

3. **`property-detail` / `quote` smoke 404 on `e2e-smoke-property`.** The seed fixture is missing — the migrator (`SeedE2EBackfill`) didn't run or the DB was reset. The warm-up step waits on `GET /api/v1/properties/e2e-smoke-property`; if that never 200s, re-run the API pipeline (migrator re-seeds). Confirm `Bootstrap:E2e:Enabled=true` on staging.

4. **Authed run: global-setup fails "E2E persona password missing".** The operator hasn't provisioned the persona / KV secret yet. Expected until the OPS.2.8 walk. Not a code bug.

5. **Authed run: global-setup fails at the Entra sign-in page (selector).** The CIAM hosted-page markup drifted from `loginfmt`/`passwd`/`idSIButton9`. Update the selectors in `global-setup.ts`; if the page proves un-drivable, fall back to the msal-node ROPC grant (plan §5-Q2-b). Do NOT add a fake-auth backdoor (ADR-0016).

6. **Owner specs redirect to `/select-tenant` / active-tenant errors.** The captured session lost `vrbook:active-tenant`. global-setup establishes it (single-membership auto-select + poll-before-capture); `ensureAdminContext` is the per-spec fallback. Re-run setup; if persistent, verify the auto-select chain in `/auth/callback`.

7. **`confirm`/`reject` owner spec skipped ("already consumed").** Expected on a nightly re-run without an intervening deploy — the seeded Tentative bookings are consumed by the action and re-armed only on the next migrator run. Deploy (or run the migrator) to re-seed. Not a failure.

8. **Flaky single spec, passes on retry.** `retries: 2` in CI already absorbs this. If a spec is chronically flaky, tighten its locator (prefer role/label, generous `expect` timeout) rather than adding sleeps.

## Guardrails (never do)

- No `[AllowAnonymous]` on admin controllers to ease auth (arch test
  `OpsOps2_AdminSurfaceAndTestBackdoorTests` fails loud).
- No committing `web/tests/e2e/.auth/` (persona sessions). The `.gitignore`
  entry + `check:e2e-suite` pin it.
- No test-only auth middleware in production `Program.cs`.
