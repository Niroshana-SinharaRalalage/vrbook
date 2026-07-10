# OPS.2 — Playwright E2E Suite — Close-Out

- **Status:** Shipped 2026-07-10 (eng-complete). Authed specs go green after the operator persona walk (§7).
- **Plan:** [`docs/OPS_2_PLAYWRIGHT_PLAN.md`](OPS_2_PLAYWRIGHT_PLAN.md). **ADR:** [`adr/0019-playwright-target-env-and-persona-strategy.md`](adr/0019-playwright-target-env-and-persona-strategy.md). **Runbook:** [`runbooks/playwright-e2e-flake.md`](runbooks/playwright-e2e-flake.md).

## What shipped

| Sub-commit | Scope | Commit |
|---|---|---|
| OPS.2.1 | Plan + `web/tests/e2e/` skeleton | `21f5075` |
| OPS.2.2 | Base infra: personas + auth fixture (sessionStorage re-inject) + `global-setup` (setup project, real MSAL) + `SeedE2EBackfill` + `is_e2e` column + Bicep `bootstrapE2eTenantEnabled` + KV placeholders | `30e9730` (+ `ca2a764` BOM fix) |
| OPS.2.3 | 5 anonymous smoke specs + **blocking** `playwright-smoke` job + seed property/plan (architect consult) | `3b5a27f` (+ `9c534fe`) |
| OPS.2.4 | 10 guest authed specs | `3c26705` |
| OPS.2.5 | 10 owner specs + 2 seeded Tentative bookings + global-setup tenant-context fix (architect consult) | `c783b9b` |
| OPS.2.6 | 6 platform-admin + auth-edge specs | `e2f523f` |
| OPS.2.7 | Nightly workflow + suite-invariant guard + arch tests + flake runbook | `27519cc` |
| OPS.2.8 | This close-out + ADR-0019 + MASTER_PLAN flip | (this commit) |

**Suite = 31 scenarios**: anonymous 5 (blocking), guest 10, owner 10 (incl. 1 `test.fixme` — full property-create submit), platform-admin 6. All authed = informational nightly.

## Verified

- **Anonymous smoke: live-validated 5/5 against deployed staging** (home, search, detail-by-slug, unauthenticated quote auto-calc, `/api/health`). Blocking gate confirmed green in CI.
- Seed fixture validated live: `GET /api/v1/properties/e2e-smoke-property` → 200; anon quote → 200 total $200 (proves the pricing-plan seed + RLS tenant-match). 2 Tentative bookings committed (transactional migrate success).
- Arch tests 2/2; `check:e2e-suite` = 31 scenarios; all workflow YAML parses; migrator publish + format + strict e2e tsc clean.
- **Authed 26 specs: authored + `playwright --list` + strict tsc verified; NOT run live this session** (personas operator-gated — §7).

## Divergences from plan

- **Scenario count 31, not 30.** Owner shipped 10 (plan said ~9); the architect ruled the count assertion should reflect the honest authored total incl. the fixme, not pad to 30. `check:e2e-suite` pins 31.
- **`SeedE2EBackfill` grew beyond §6.** Now seeds a public property + pricing plan (OPS.2.3a) + 2 Tentative bookings (OPS.2.5) via raw cross-schema SQL (migrator has BYPASSRLS) — both from architect consults; enables the anon detail/quote + owner confirm/reject scenarios deterministically.
- **`global-setup` capture-race fix** (OPS.2.5): the single-membership auto-select writes `vrbook:active-tenant` asynchronously; global-setup now polls it before snapshotting.
- **Health smoke targets the web `/api/health` Next route**, not the backend (the plan's wording was imprecise).
- **Class renamed `SeedE2eBackfill` → `SeedE2EBackfill`** (Sonar S101).

## Follow-ups / backlog

- **OPS.2.8 operator walk (§7): provision the 3 Entra CIAM personas + set `e2e-*-password` KV secrets**, then run `nightly-playwright.yml` via `workflow_dispatch` → expect 26/26 (25 + the fixme skipped). First run will shake out any Entra hosted-page selector drift + the property-create fixme selectors.
- **OPS.2.9**: blocking-flip the authed nightly after ~2 weeks stable soak.
- **OPS.2.10**: Firefox/WebKit matrix.
- The one `test.fixme` (full property-create submit): flip live in the walk.

## §7 Operator persona-provisioning walk (pending owner)

1. Create three Entra-local CIAM personas (no MFA, no CA): `e2e-guest@vrbook.test` (guest flow), `e2e-owner@vrbook.test` (admin flow), `e2e-platform-admin@vrbook.test` (admin flow).
2. Set their passwords into KV: `az keyvault secret set --vault-name kv-vrbook-staging --name e2e-{guest,owner,platform-admin}-password --value <pw>`.
3. The DB side (tenant, owner membership, platform-admin flag, property, bookings) is already auto-seeded by `SeedE2EBackfill` on every staging deploy.
4. Trigger `nightly-playwright.yml` via `workflow_dispatch`; triage any failures with `runbooks/playwright-e2e-flake.md`.
