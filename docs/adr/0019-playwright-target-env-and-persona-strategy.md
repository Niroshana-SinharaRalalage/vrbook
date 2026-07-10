# ADR-0019 — Playwright E2E: target env + persona/auth strategy

- **Status:** Accepted (2026-07-10). Slice OPS.2.
- **Context:** OPS.2 shipped a 31-scenario Playwright suite. The load-bearing decisions were locked in [`docs/OPS_2_PLAYWRIGHT_PLAN.md`](../OPS_2_PLAYWRIGHT_PLAN.md) §5 (owner-adopted architect recommendations) and refined by two OPS.2 architect consults (2026-07-10). This ADR records the durable ones.

## Decisions

1. **Target = deployed staging, not a local server.** The suite runs against the live staging web + API + real Entra CIAM (`PLAYWRIGHT_BASE_URL` = staging web FQDN; `webServer` undefined). Local runs are opt-in via the same env var. Rationale: the curl smoke proves reachability; Playwright proves browser-level flow correctness on the *real* deployed surface, catching SSR/hydration/CORS/auth-wiring breakage a localhost run would miss.

2. **Real MSAL sign-in, never a fake-auth backdoor.** Authed personas sign in through the actual Entra CIAM hosted flow driven by the browser (`global-setup.ts`, setup project). ADR-0016 (admin surface Entra-local only) + ADR-0017 (admins pre-seeded) forbid an `[AllowAnonymous]`/DevAuth shortcut on the admin surface — enforced by arch test `OpsOps2_AdminSurfaceAndTestBackdoorTests`. Fallback (only if the hosted page proves un-drivable): msal-node ROPC grant (plan §5-Q2-b) — staging-only, still real credentials.

3. **sessionStorage token cache is captured + re-injected.** MSAL uses `cacheLocation: 'sessionStorage'`, which Playwright's `storageState` does NOT capture. global-setup persists the sessionStorage snapshot alongside storageState; `auth.fixture.ts` re-injects it. Without this every authed API call would 401 with no bearer.

4. **Three personas, distinct surfaces.** `e2e-guest` (guest flow, lazy-provisioned like a real guest — NOT seeded); `e2e-owner` (admin flow, pre-seeded `tenant_admin` on the isolated `e2e-tenant`); `e2e-platform-admin` (admin flow, pre-seeded `is_platform_admin`, 0-membership → platform-scoped, no tenant picker). Passwords live only in Key Vault (`e2e-*-password`), read via env, never logged/committed.

5. **Deterministic seed fixture over live data.** `VrBook.Migrator.SeedE2EBackfill` (staging-only, `Bootstrap:E2e:Enabled`) seeds the `e2e-tenant`, the two admin personas, a public `e2e-smoke-property` + pricing plan (anon detail/quote), and two Tentative bookings (owner confirm/reject, reset on each deploy). Deterministic fixed GUIDs; self-healing on every deploy. Rejected: derive-from-live-data (a blocking gate that self-skips on empty data is a fake gate).

6. **CI gating split.** Anonymous smoke = BLOCKING on every push (`cd-staging-web.yml` → `playwright-smoke`). Authed (guest/owner/platform-admin) = INFORMATIONAL nightly (`nightly-playwright.yml`), blocking-flip deferred to OPS.2.9 after soak. Chromium only (other browsers → OPS.2.10 candidate).

7. **Google/social OAuth is out of scope for Playwright.** Third-party consent screens aren't reliably drivable and re-shape without notice; covered by a manual weekly walk (`docs/runbooks/social_idp_setup.md`).

## Consequences

- The authed suite goes green only after the operator provisions the three Entra CIAM personas + KV passwords (OPS.2.8 walk). Until then global-setup fails fast with a secret-free message; the nightly is red-but-non-gating.
- Staging must run in scale-to-zero-tolerant fashion for the smoke gate — see OPS.INFRA.3 (warm-the-revision-first).
- The 30→31 scenario count is pinned by `web/scripts/check-e2e-suite.mjs` (honest authored total incl. the one owner `test.fixme`).
