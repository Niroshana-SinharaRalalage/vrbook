# ADR-0020 — Tiered Definition-of-Done verification gate

- **Status:** Accepted (owner-approved 2026-07-19).
- **Context:** The owner mandated an 8-step per-story DoD gate (`C:/tmp/vrbook-coord/STORY-CHECKLIST.md`) whose step 6 required an **authenticated staging verification** for *every* story. Because admin auth is Entra External ID (CIAM) email+password with no headless token grant available *until the seeded test personas are provisioned* (ADR-0019 / OPS.2.8), step 6 became a per-story dependency on manual owner action, freezing the DONE-count while ~5 code-complete, deployed, wire-verified stories waited on authenticated observation.
- **Consulted:** system-architect (2026-07-19). Owner adopted the recommendation; deferred the persona provisioning (decision 2 below).

## Decision

Step 6 ("authenticated staging verification") is **tiered by risk**, not uniform. Every tier keeps the non-negotiable baseline; only Tiers S and A require live authenticated staging observation, and even then it is **headless via the seeded personas (ADR-0019), never via an owner-supplied token**.

**Baseline (every story, unchanged from ENGINEERING-RULES DoD):** TDD RED-first · unit + architecture tests green · **VRB-300 RouteMatrix row + integration/contract test added** · TL review+merge · green staging deploy · **anonymous wire-level gate check** (new authed routes return 401/403 = deployed + auth-gated).

| Tier | Classes | Binding DONE evidence (beyond baseline) | Live authed staging obs? |
|---|---|---|---|
| **S — Real-money** | Stripe pay/refund/webhook (VRB-102/104/105) | automated **headless** book→pay→refund on staging (Stripe test mode) via persona; assert Stripe state + webhook + DB/audit | **Yes, mandatory (headless)** |
| **A — New auth boundary** | stories that *add/change* an authZ rule, role check, tenant-isolation shape, IdP wiring | one headless authed 2xx on the authorized path (baseline already proves the negative: anon 401 / wrong-tenant 403) | **Yes, headless** |
| **B — Data-mutating settings/admin CRUD + audit** | VRB-211/215/216 | **`Category=Integration` Testcontainers round-trip** (real Postgres + RLS: PUT→GET→audit) is the binding evidence | No (live authed is confirmatory/nightly, non-blocking) |
| **C — Read / config GET** | config/read endpoints | integration contract test + anon wire-check | No |
| **D — Frontend / a11y / consent** | web-guest, a11y, consent | vitest + `check:e2e-suite` + anonymous Playwright smoke (blocking) + green web deploy | No (authed UI = nightly authed Playwright) |
| **E — Infra / DR (privileged)** | VRB-304 restore drill | scripted + dry-run-validated + runbook + scheduled | N/A — the one privileged execution is a **tracked owner op with sign-off, NOT a code-story DONE blocker** |

**Corollaries:**
1. **VRB-300 contract/integration suite flips informational → BLOCKING** (`ci.yml`, `cd-staging-api.yml`). This is its own overdue DoD and is what legitimately substitutes for live authed checks on authorization-negative + round-trip correctness (Testcontainers exercises real middleware + RLS). Live staging remains irreplaceable only for **environment wiring** (real Stripe/Entra/KV/migrations) and **real-money/new-auth positive paths** — i.e. Tiers S/A.
2. **Persona provisioning (OPS.2.8) is deferred** (owner 2026-07-19). Until done, Tier S/A stories keep their authed check pending (interactive-Playwright-captured persona token or a one-off manual money-path check — **never ROPC**, see Rejected); Tiers B/C/D/E flow normally. Already-DONE real-money stories (VRB-105/104) carry a tracked "authed money-path back-verify deferred to persona" note — not re-opened.
3. **No false-DONEs where money, auth, or data-integrity live; no DONE frozen on owner availability anywhere else.**

## Consequences
- The settings cluster (VRB-211/215/216) does **not** auto-flip: it currently has only `Category=Unit` tests, so Tier B requires adding the `Category=Integration` round-trip first — a coverage improvement, then flip.
- Rigor increases (VRB-300 blocking; payments/auth keep mandatory authed proof) while the DONE-count tracks real progress.
- Constraints honored: ADR-0016 (admin Entra-local — the personas are real email+password, no `[AllowAnonymous]` backdoor), ADR-0017 (pre-seed, per-env), ADR-0019 (persona/ROPC strategy). No test backdoor on a deployed surface (`OpsOps2_AdminSurfaceAndTestBackdoorTests` stays green).

## Rejected
- **Uniform strict gate** — freezes DONE on owner availability for low-risk stories; no added assurance over Testcontainers+wire-check for Tier B/C/D.
- **client_credentials / app-only token** — authenticates an app, not a role-bearing admin; can't exercise HasTenantRole/RLS.
- **Signed test-only token issuer on staging** — reintroduces the DevAuth backdoor retired in OPS.M.14; synthetic principals stay in-process (Testcontainers) only.
- **Owner-supplied browser token as standing policy** — the bottleneck being removed; kept only as a documented emergency fallback.
- **ROPC / `acquireTokenByUsernamePassword`** (rejected 2026-07-20, architect consult) — bypasses the CIAM user flow, so its tokens carry neither the `tfp`/`acr` admin-flow marker nor the flow's application-claims `email` mapping, and cannot satisfy the OPS.M.22 admin-preseed gate (ADR-0017). Making it "work" requires forging `tfp` via a claims policy, which fabricates the exact admin-flow signal the gate verifies. The only supported headless admin-token path is the interactive `AdminSignUpSignIn` capture (ADR-0019 §2). See `docs/runbooks/ops-2.8-e2e-personas.md`.

## Sequencing invariants (added 2026-07-21, architect-reviewed — the 07-20/21 stall postmortem)
These are load-bearing rules; violating any one caused the throughput stall. Do NOT re-derive.
1. **Foundation-before-gate.** Never flip a shared test-suite (VRB-300 integration) to a **blocking** gate until it is simultaneously **green + deterministic + de-masked**. A blocking gate on a flaky/masked suite converts every parallel lane into one random serialization point. #52 (migrate-first, deterministic CI DB) is the prerequisite; the blocking flip comes *after* residual failures hit zero — never before.
2. **No fake-green on a load-bearing suite.** A suite that gates DONE must not run `continue-on-error`. Green-that-isn't-green lets real failures accumulate unattributed and surface all at once. Keep informational only while the suite is being *built*; remove the mask the moment it becomes the gate.
3. **A foundation blocking N lanes escalates ONE lane, never a swarm.** One agent owns the foundation fix; the other N-1 keep claiming independent stories from the board. Converging all lanes onto one shared problem is a WIP collapse — forbidden. Route sub-clusters to their owning lanes.
4. **The rollup counter is derived, never hand-typed.** It equals the count of `| DONE |` rows; report `DONE` and `DONE-minus-reopens` with reopens annotated so a legitimate false-DONE removal never reads as a stall. TL advances it at every merge.
