# VrBook — Parallel Execution Plan

How to execute the 85-story backlog ([`../stories/INDEX.md`](../stories/INDEX.md)) with multiple agents/engineers concurrently **without merge conflicts**. Lanes own non-overlapping files; cross-cutting foundations are sequenced, not parallelised. Copy-paste kickoff prompts: [`AGENT-PROMPTS.md`](AGENT-PROMPTS.md).

**Method:** each **lane** owns a fixed set of files/directories and runs on its own git worktree + branch. Within a lane, stories run in the lane's stated order. Across lanes, integrate in **wave** order. A story may only touch files its lane owns; a cross-lane need becomes an explicit dependency + a contract handed off, never two lanes editing one file.

---

## Load-bearing shared surfaces (SEQUENCED — never parallelise these)

These files are touched by many stories; only ONE lane at a time may hold them, and changes to them gate dependents:
- **Config schema / DI wiring** — `src/VrBook.Api/Program.cs`, options classes → **Lane CONFIG** owns; lands first. **Wave-1 DI ownership rule (resolves the CONFIG↔SETTINGS↔PAY overlap the audit flagged):** after Wave 0, `Program.cs` is **frozen** — a feature lane never edits it. Each module registers its own services inside its module's `AddXxxModule(services, config)` extension method **which the lane owns** (in `src/Modules/VrBook.Modules.<Mod>/`); `Program.cs` already calls each `AddXxxModule` once, so adding a handler/option/service is an in-module, in-lane edit. Only adding a *brand-new module* touches `Program.cs`, and that is a sequenced CONFIG-held change. This keeps DI wiring collision-free across parallel Wave-1 lanes.
- **Design-system primitives** — `web/src/components/ui/*`, `tailwind.config.ts`, `globals.css` → **Lane DESIGN** owns; lands first (the UI lib is thin today — only `ConfirmActionModal`; every UI story needs primitives).
- **Payments/pricing engine** — `Modules/VrBook.Modules.Payment/*`, `StripeGateway.cs`, `RefundForBookingCommand.cs`, cancellation on `Booking.cs` → **Lane PAY** owns; internally sequenced.
- **Tenant/RLS core** — `TenantGucCommandInterceptor.cs`, `TenantAuthorizationBehavior.cs`, `PlaceBookingHandler.cs` → only the **Phase-3 foundation lane** touches these (Wave 3); no launch lane does.
- **CI/CD + infra** — `.github/workflows/*`, `infra/*` → **Lane DEVOPS** owns.
- **Contracts** (`VrBook.Contracts`: DTOs/events/enums) — shared; any change is a sequenced "contract commit" merged before consumers (mirrors the OpenAPI drift-gate discipline already in CI).

---

## Waves & lanes

### WAVE 0 — Foundations (must land before feature lanes; run these 4 in parallel)

| Lane | Stories | Owns | Why first |
|---|---|---|---|
| **TEST** | VRB-300 (API contract suite + endpoint-coverage gate) | `tests/VrBook.Api.IntegrationTests/Contract/*`, the coverage arch test in `tests/VrBook.Architecture.Tests` | The safety net every lane's per-endpoint tests plug into; "keep the API suite green" needs a suite to enforce. Touches no feature code — never collides. |
| **CONFIG** | VRB-200 (fail-fast), 201 (secrets), 203 (flags), 202/205 (matrix, CORS/rate-limit) | `Program.cs` config wiring, options classes, `infra/scripts/10-store-secrets.ps1`, `Directory.*.props` for validators | Everything reads config; fail-fast + secrets + the flag runtime unblock every other lane. |
| **DESIGN** | Design-system primitives + VRB-110 focus-trap primitive | `web/src/components/ui/*`, `tailwind.config.ts`, `globals.css` | Every UI story consumes these; building them once prevents copy-paste divergence (G20). |
| **DEVOPS-prereq** | VRB-301 (prod pipeline), 302 (rollback), 303 (migration strategy), 304 (restore drill), 306 (observability) | `.github/workflows/*`, `infra/*` (+ new `cd-prod.yml`) | "Secrets + staging pipeline + rollback must land early, not the week before launch." |

### WAVE 1 — Launch features (parallel lanes, after Wave 0)

| Lane | Stories (in order) | Owns |
|---|---|---|
| **PAY** (sequenced internally) | VRB-105 MoR fix → 104 fee-reversal → 103 Stripe Tax → 102 cancellation-engine → 113 deposit → 111 instant-book → 112 promos | `Modules/VrBook.Modules.Payment/*`, `Modules/VrBook.Modules.Pricing/*` (tax/fees), `Booking.cs` policy fields, `StripeGateway.cs`. **Depends on** SETTINGS for VRB-215/216 policy config (contract handoff). |
| **CATALOG** | VRB-101 photos | `Modules/VrBook.Modules.Catalog/*` image endpoints, `web/src/app/admin/properties/*`, `web/src/components/property/*`, blob wiring |
| **WEB-GUEST** | VRB-106 mobile-nav, 107 home, 108 profile, 109 SEO, 110 a11y-audit | `web/src/app/page.tsx`, `web/src/app/account/*`, `web/src/components/layout/*`, `web/src/app/sitemap.ts` + `robots.ts`. Consumes DESIGN primitives. |
| **SETTINGS** | VRB-210 UI-shell, 211 audit-trail, 212–220 product settings, 206–209 config-defect fixes | `web/src/app/admin/settings/*`, `Modules/VrBook.Modules.Admin/*` (build out the stub), settings APIs. Consumes CONFIG + DESIGN. Hands the policy-config contract to PAY. |

### WAVE 2 — Launch-week (operator-gated, after Wave 1)

| Lane | Stories | Notes |
|---|---|---|
| **DEVOPS-launch** | VRB-305 DNS/TLS, 307 security, 308 load, 309 payments-live, 312 cutover, 313 hypercare | Operator long-poles (DKIM DNS, Stripe live keys, Entra cutover) start Day 0 in parallel. |
| **COMPLIANCE** | VRB-311 analytics+consent+legal, 310 owner-UAT | Analytics must be live before the first real visitor. |

### WAVE 3 — Phase 3 (post-launch; SEQUENCED spine — mostly one lane at a time)

- **P3-FOUNDATION** (one lane, strictly ordered): VRB-400→401→402→403→404. Touches the tenant/RLS core + `Booking.cs` reshape — nothing else runs against those files during this wave.
- **P3-ROOMS** (after foundation): VRB-405→406 (rename wave, sequenced across catalog/pricing/reviews/sync/messaging) → then 407/408/409 fan out in parallel → 410/411 UI.
- **P3-CART** (after rooms): VRB-420…431. The StripeGateway/PlaceHandler/interceptor edits (422/423/424/425) are one sub-lane, sequenced; UI (430) + observability (431) fan out.

### WAVE 4 — Phase 4 (after Phase-3 cart)

- **P4-OTA**: VRB-500…512. Reuses the Phase-3 engine; new `travel`/`ordering` overlays + `identity.tenant_supplier_relationships`. Package-builder UI (510) + guest itinerary (511) parallel to the domain work.

---

## Integration order

1. **Wave 0 merges first**, in this order: a **contract commit** (any new enums/DTOs) → CONFIG → DESIGN → DEVOPS-prereq. Each merges to `develop` green (CI blocking gates: format, build, unit, arch, web lint/typecheck/`check:e2e-suite`, pact drift, smoke, playwright-smoke).
2. **Wave 1 lanes** rebase on the merged Wave 0, then merge in dependency order: SETTINGS' policy-config contract before PAY's VRB-102; otherwise any order (non-overlapping files).
3. **Wave 2** after Wave 1 is green on staging.
4. **Waves 3–4** are post-launch; P3-FOUNDATION merges before P3-ROOMS before P3-CART before P4.
5. Every merge: `gh run watch` to green before the next lane rebases (the repo's standing rule). Migrations are forward-only; the rename wave (VRB-406) merges as one atomic PR.

## Git / worktree strategy

- One **worktree + branch per lane** off `develop`: `git worktree add ../vrbook-<lane> -b lane/<lane>`. (Repo convention: work on `develop`, not `main`.)
- Lanes never edit files another lane owns; a cross-lane need is a **dependency** (wait for the owning lane's merge) or a **contract** (owning lane ships the interface first).
- Small, frequent commits; rebase on `develop` before each merge; `gh run watch <id> --exit-status` to green before merging (CHARSET/analyzer traps per `CLAUDE.md`).
- The sequenced shared-surface lanes (PAY, P3-FOUNDATION) hold their files exclusively for the wave — schedule them so no other lane needs those files concurrently.

## TDD + quality gates (every lane, every story)

Failing test → minimal impl → refactor. Each story's TDD plan names the Unit / Integration (Testcontainers Postgres) / E2E (Playwright) tests to write first. Before merge: `dotnet format --verify` + `dotnet publish -c Release` (catches CA1822/CHARSET), `Category!=Integration` filter, `web` lint/typecheck/vitest/`check:e2e-suite`. Use `superpowers:test-driven-development` + `requesting-code-review` per story; `frontend-design` for UI stories.

## Known cross-lane touchpoints (resolved — not overlaps)

The audit flagged three apparent file overlaps. Each is resolved by assigning a single owner + a dependency, so no two lanes edit one file concurrently:

- **`web/src/app/sitemap.ts` + `robots.ts`** — **WEB-GUEST owns them** (VRB-109 builds the SEO engine). VRB-305 (DEVOPS, custom domain/DNS) only *supplies the domain value* VRB-109 consumes; VRB-305 does **not** edit `sitemap.ts`/`robots.ts`. Dependency: VRB-109 reads the prod domain from config once VRB-305 sets it.
- **SEO surface** — VRB-109 (WEB-GUEST) owns the *engine* (sitemap/robots/canonical/JSON-LD in `web/src/app/*`); VRB-219 (SETTINGS) owns the per-property SEO **settings** UI + storage (`web/src/app/admin/settings/*` + the settings API). Different files, different lanes; VRB-109 renders what VRB-219 stores. No shared file.
- **`Program.cs` in Wave 1** — resolved by the module-`AddXxxModule` self-registration rule under "Load-bearing shared surfaces" above.

## Conflict-avoidance summary

The only files >1 story touches are the **load-bearing shared surfaces** above, and each is owned by exactly one lane per wave. Everything else is partitioned by module/route so lanes are truly independent. If two stories ever both need a shared file, the later one is re-scoped to depend on the earlier — never merged concurrently. `.github/CODEOWNERS` makes a cross-lane edit to a shared surface require the owner's review — **once branch protection is enabled** ([`../runbooks/branch-protection.md`](../runbooks/branch-protection.md)); until then the partition is documented, not enforced. Apply branch protection before launching unattended agents.
