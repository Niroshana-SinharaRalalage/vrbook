# OPS Launch Completion Plan (Slice OPS.1–OPS.8 → Phase 1 Go-Live)

- **Status:** LOCKED — owner directive 2026-07-10 ("complete slice OPS asap"); architect completion plan adopted directly per [`feedback_technical_decisions_are_architect_call`](../.claude/projects/c--Work-BookingApp/memory/feedback_technical_decisions_are_architect_call.md) + [`feedback_speed_to_launch_over_slice_depth`](../.claude/projects/c--Work-BookingApp/memory/feedback_speed_to_launch_over_slice_depth.md).
- **Date:** 2026-07-10.
- **Author:** Platform Enterprise Architect (agent) via OPS launch-completion consult.
- **Objective:** Minimum-credible launch hardening, optimized for wall-clock-to-live. No slice gold-plated.
- **Predecessors:** [`docs/EXECUTION_PLAN.md`](EXECUTION_PLAN.md) §8, [`docs/MASTER_PLAN.md`](MASTER_PLAN.md) rows 17–22, [`docs/OPS_1_PACT_PLAN.md`](OPS_1_PACT_PLAN.md) + [`OPS_1_CLOSE_OUT.md`](OPS_1_CLOSE_OUT.md), [`docs/OPS_2_PLAYWRIGHT_PLAN.md`](OPS_2_PLAYWRIGHT_PLAN.md), [`docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md`](OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md).

---

## §0 TL;DR — the decisive calls

1. **Compress OPS.3 (k6) and OPS.4 (ZAP) from blocking-CI-gates to one-time pre-launch runs. Endorsed.** Same for OPS.5 (Trivy scan one-time; SBOM *signing* → backlog).
2. **OPS.1.9 (live provider verifier + 11 interactions) → POST-launch backlog.** The consumer pacts + CI drift gate + Playwright + smoke already cover launch risk. Do not yak-shave the WAF/Kestrel adapter before revenue.
3. **The INFRA.2 CI regression is a root-cause bug, not a timeout to nudge.** With `minReplicas=0`, `health=Healthy` can never converge at zero traffic. Fix = **warm-the-revision-first** (drive traffic so a replica spins up), belt-and-suspenders timeout bump. Ship it as its own micro-slice **OPS.INFRA.3** — it blocks *every* web/api deploy today, so it lands FIRST. Do NOT fold into OPS.2.7.
4. **Latent bug:** the OPS.2 plan's arch test asserts *30* scenarios; the suite actually has *31* (10 owner, not the planned 9). Pin the OPS.2.7 count assertion to **31** or it fails on landing.
5. **Fastest realistic path ≈ 6–8 focused engineering sessions**, gated by operator lead time on Stripe LIVE keys, DKIM/SPF DNS propagation, and the 3 Entra CIAM personas — all of which must start **today, in parallel**.

---

## §1 Critical-path sequencing

### (a) Engineering track — strict order

| Order | Item | Why this position |
|---|---|---|
| **E0** | **OPS.INFRA.3** — revision-convergence fix (web + api pipelines) + `ServerIsBusy` Bicep retry wrapper | Hard blocker: the web pipeline is red today and `playwright-smoke` (`needs: smoke`) is being skipped. Nothing else can be validated on staging until green. |
| **E1** | **OPS.2.7** — `nightly-playwright.yml` cron + `playwright-e2e-flake.md` runbook + cross-surface arch tests (**assert 31**, `.auth/` gitignored, no `[AllowAnonymous]`, no test middleware in prod `Program.cs`) | Completes the E2E CI surface. Independent of operator personas (nightly is informational). |
| **E2** | **OPS.2.8 (eng portion)** — close-out + ADR-0019 + MASTER_PLAN row 17 flip + `EXECUTION_PLAN §8` + `CLAUDE.md` footer | Depends on E1. The *operator persona walk* (turning authed specs green) is operator-gated — see track (b); do NOT block the eng close-out on it, record authed-green as a follow-up evidence step. |
| **E3** | **OPS.5 (Trivy)** — image scan of `vrbook-api` + `vrbook-web`, one-time pre-launch run, triage HIGH/CRITICAL-fixable, suppressions file | Fast, independent, no operator dependency. Do early to surface any base-image CVE that forces a rebuild. |
| **E4** | **OPS.3 (k6)** — 50 RPS / 5 min / P95<1s script + one-time run + evidence capture + short runbook | **Must run against a prod-sized target** (see §3 caveat). Sequence after the Stripe/env are stable so the run is representative. |
| **E5** | **OPS.4 (ZAP)** — baseline (passive) scan, one-time pre-launch, triage + suppression baseline file | Run last against the near-final deployed surface so newly-added routes are in scope. |
| **E6** | **Go/No-Go assembly** — collect evidence artifacts, verify §6 checklist, cutover | Convergence point. |

E3/E4/E5 are mutually independent and can interleave; the only ordering constraint is that k6 (E4) and ZAP (E5) want a stable, near-final deployed surface.

### (b) Operator-gated track — start TODAY, runs concurrently

| Item | Owner action | Lead-time risk | Gates |
|---|---|---|---|
| **OPS.6** — Stripe LIVE keys + webhook signing secret | Provision LIVE restricted key + webhook endpoint secret → store in KV | Low (minutes) but requires a live-mode test transaction to verify | Real payments. Hard gate. |
| **OPS.8** — ACS sender custom-domain DKIM/SPF | Publish DKIM CNAMEs + SPF TXT for the platform sender domain | **HIGH — DNS propagation hours-to-24h.** Start first. | Booking-confirmation email deliverability. Hard gate. |
| **OPS.2.8 personas** — 3 Entra CIAM personas + KV passwords | Provision Entra-local personas (no MFA/CA), set `e2e-*-password` KV secrets | Medium | Turns authed E2E green (evidence, *not* a launch gate). |
| Entra **prod cutover** | Follow `docs/identity/runbooks/entra-prod-cutover-checklist.md` | Medium | Prod auth. Hard gate. |
| Social IdP portal setup | Already deferred | — | NOT launch-blocking. |

**Operator directive:** kick off DKIM/SPF (OPS.8) and Stripe LIVE (OPS.6) *before* any engineering session starts — their latency, not the code, is the long pole.

---

## §2 Scope-compression call (opinionated)

The owner wants ASAP. Here is the honest gate/no-gate split.

### Compress to ONE-TIME pre-launch run (endorsed)

- **OPS.3 k6** — Blocking-CI load testing is wasteful and flaky for a pre-revenue product. **One credible run** proving 50 RPS / P95<1s, captured as an evidence artifact, is the launch gate. **Non-negotiable caveat:** staging is now **B1ms Burstable Postgres + scale-to-zero** (OPS.INFRA.2). A k6 run against that is *not* representative of prod and will fail or mislead. Run k6 against a **prod-sized target** — either the provisioned prod stack pre-cutover, or staging temporarily upsized (General Purpose PG, `minReplicas≥1`) for the duration of the run, then reverted. Record the target sizing in the evidence.
  - *Residual risk of deferring CI-gating:* perf regressions post-launch aren't caught automatically. **Compensating control:** App Insights P95 latency + failure-rate alerts (observability stack already exists per ADR-0010). Backlog: `POLISH` item to wire k6 as a pre-release/nightly job.

- **OPS.4 ZAP baseline** — Passive baseline scan is fast but noisy; per-push gating buys little pre-launch. **One authenticated-context baseline run + triage + a committed suppression baseline** is credible. Run it late (E5) against the near-final surface.
  - *Residual risk:* endpoints added after launch go unscanned. **Compensating control:** make "ZAP baseline delta" a pre-release checklist line item; the threat model (`docs/security/threat-model.md`) already covers the static surface. Backlog: CI job.

- **OPS.5 Trivy** — Image vuln scan is cheap; a **one-time pre-launch scan + triage** of both images is the gate. **SBOM signing → post-launch backlog** (supply-chain hygiene, not a go-live blocker for a single-tenant-staging/prod topology per ADR-0013).
  - *Residual risk:* new base-image CVEs post-launch. **Compensating control:** dependabot/base-image bump cadence + re-run at each release. Trivy-in-CI (non-blocking → blocking) is a strong early-backlog item since it's low-effort.

### Keep as HARD go-live gates (cannot compress)

- **OPS.2 anonymous Playwright smoke** — already BLOCKING in `cd-staging-web.yml`; keep. This is the only browser-level correctness gate and it's cheap. (Authed E2E stays *informational*.)
- **OPS.INFRA.3** — deploy pipeline must be green.
- **OPS.6 Stripe LIVE** — cannot take money without it.
- **OPS.8 DKIM/SPF** — booking-confirmation email is core product function; sending unauthenticated mail tanks deliverability from day one.
- **Entra prod cutover** — prod auth.

### Minimum credible launch-hardening set

> OPS.INFRA.3 green pipeline · anonymous Playwright smoke blocking · one-time k6 pass (prod-sized) · one-time ZAP baseline triaged · one-time Trivy scan triaged · Stripe LIVE verified · DKIM/SPF verified · Entra prod cutover complete · no remaining `TODO: production`.

Everything else (OPS.1.9, SBOM signing, recurring k6/ZAP/Trivy CI jobs, authed-E2E blocking-flip OPS.2.9, social IdP) is **post-launch backlog**.

---

## §3 OPS.1.9 — before or after go-live? **AFTER.**

**Decision: post-launch backlog.** Justification:

- The **consumer harness + 1 interaction + CI drift gate** already landed (5/8 sub-commits). The drift gate protects the FE↔API contract shape on every push — the primary launch risk Pact exists to cover.
- **Playwright anonymous smoke** (blocking) + the deployed **curl smoke** hit the *real* API, providing live end-to-end coverage that overlaps the highest-value provider behaviors.
- The deferred work — a **WAF-vs-Kestrel adapter for PactNet** because `WebApplicationFactory` is in-process `TestServer`, plus a **PactV3 mock-server lifecycle refactor**, plus 11 interactions — is a genuine engineering yak-shave (per ADR-0018's carve-out reasoning) that gates **zero** user-facing behavior.
- *Residual risk:* the provider could drift from consumer expectations without live verification. **Mitigated** by (a) the published consumer pacts + drift gate, (b) E2E against the real API, (c) the existing `docs/runbooks/pact-contract-drift.md`. Acceptable for launch.

Estimate: **2–3 sessions, scheduled after go-live** (before Slice 8, so Phase 3 inherits verified contracts).

---

## §4 INFRA.2 revision-convergence regression — the fix

### Root cause (not a timeout problem)

The "Wait for active revision… (max 3 min)" step (`cd-staging-web.yml` L194–214, mirrored in `cd-staging-api.yml`) waits for the active revision to report `state==Running && health==Healthy`. After OPS.INFRA.2 set `minReplicas=0`, **at zero traffic there is no running replica**, so the health probe backing `health==Healthy` has nothing to pass — the condition can *never* reliably converge within any fixed window. Bumping the loop count only lengthens the guaranteed failure. The burstable-PG cold readiness probe compounds the cold-start latency once a replica *does* spin up.

### Recommended fix — **warm-the-revision-first** (+ modest ceiling bump)

Do NOT raise `minReplicas` back to 1 — that discards the entire cost win of OPS.INFRA.2, which is the point of the slice.

1. **Insert a warm-up step before the convergence wait:** issue `curl` to the deployed web FQDN and to the API `/api/health` to *drive traffic*, forcing KEDA to scale the new revision from zero to one. Retry the curl for ~60–90s to absorb cold start.
2. **Then** run the existing convergence poll — a warmed replica passes probes and `health==Healthy` converges quickly in the common case.
3. **Belt-and-suspenders:** raise the ceiling from 36×5s (180s) to ~96×5s (~8 min) *with early-exit* to cover burstable-PG cold-readiness tail without slowing the happy path.
4. Optionally relax the gate: accept `state==Running` with a successful warm-up 200 as the convergence signal, treating `health` as advisory — but the warm-up curl is the load-bearing fix.

### Packaging — its own micro-slice **OPS.INFRA.3**, NOT folded into OPS.2.7

- It fixes **both** `cd-staging-api.yml` and `cd-staging-web.yml`; OPS.2.7 is Playwright-scoped. Coupling a pipeline-infra fix to the nightly-E2E landing is wrong altitude and wrong risk profile.
- It is **blocking all deploys today**, so it must land *ahead* of OPS.2.7, not inside it.
- Two commits: **INFRA.3.1** warm-first convergence fix (both pipelines); **INFRA.3.2** `ServerIsBusy` retry wrapper.

### `ServerIsBusy` Bicep flake — **yes, add a retry wrapper**

The transient `ServerIsBusy` on `psql-vrbook-staging-v2` applying `require_secure_transport` occurs because burstable servers serialize control-plane config writes and reject concurrent ones mid-operation. Wrap the `az deployment group create` (or the specific PG config module) in a **bounded retry with backoff** (e.g. 3 attempts, 30s backoff, only on `ServerIsBusy`/`Conflict`). Low effort, folds into INFRA.3.2. Document in the deploy runbook.

---

## §5 Effort estimates (focused work-sessions)

| Item | Track | Estimate | Notes |
|---|---|---|---|
| **OPS.INFRA.3** (warm-first both pipelines + ServerIsBusy retry) | Eng | **0.5–1** | Highest priority; unblocks everything. |
| **OPS.2.7** (nightly cron + flake runbook + arch tests, assert **31**) | Eng | **1** | Watch the 30→31 count bug. |
| **OPS.2.8** (eng: close-out + ADR-0019 + plan flips) | Eng | **0.5–1** | Authed-green is operator-gated evidence, not eng-blocking. |
| **OPS.5 Trivy** (one-time scan + triage + suppressions) | Eng | **0.5–1** | Do early to catch base-image CVEs. |
| **OPS.3 k6** (script + prod-sized run + evidence + runbook) | Eng | **1–1.5** | Requires prod-sized target provisioning window. |
| **OPS.4 ZAP** (baseline + triage + suppression file) | Eng | **1** | Run late against near-final surface. |
| **OPS.6 Stripe** (KV key swap + webhook secret + live-mode verify) | Eng+Op | **0.5** eng | Code assumed ready; verify one live txn. |
| **OPS.8 DKIM/SPF** (ACS custom domain config + verify) | Eng+Op | **0.5** eng | **DNS propagation is the long pole — start today.** |
| Entra prod cutover | Op | — | Follow existing cutover runbooks. |
| **OPS.1.9** (verifier adapter + refactor + 11 interactions) | Eng | **2–3** | **POST-launch.** |

### Fastest-realistic path to launch

Assuming operator items land promptly:

- **Day 0 (operator, parallel):** kick off DKIM/SPF DNS (OPS.8), provision Stripe LIVE keys + webhook (OPS.6), provision 3 Entra personas (OPS.2.8) and start the prod-cutover runbook.
- **Session 1:** OPS.INFRA.3 → pipeline green → confirm `playwright-smoke` runs.
- **Sessions 2–3:** OPS.2.7 + OPS.2.8(eng). Trivy (OPS.5) interleaved.
- **Sessions 4–5:** provision/borrow a prod-sized target; run k6 (OPS.3); capture evidence.
- **Session 6:** ZAP baseline (OPS.4) against near-final surface; triage.
- **Session 7–8:** verify Stripe live txn + DKIM propagation + Entra cutover; Go/No-Go assembly (§6); cutover.

**Net: ~6–8 focused engineering sessions**, wall-clock gated primarily by DKIM propagation and prod-sized-target availability, not by code.

---

## §6 Go/No-Go launch checklist

Every line must be green before flipping to production.

**Pipeline & correctness**
- [ ] OPS.INFRA.3 landed; `cd-staging-web.yml` + `cd-staging-api.yml` green on develop; convergence step passes with scale-to-zero.
- [ ] `smoke` (curl) + `playwright-smoke` (anonymous, blocking) green on the launch build.
- [ ] OPS.2.7 arch tests green, count assertion = **31**; `.auth/` gitignored; no `[AllowAnonymous]` on admin controllers; no test-only middleware in prod `Program.cs`.
- [ ] (Evidence, non-gating) ≥1 authed E2E run green after persona provisioning.

**Hardening evidence (one-time runs archived)**
- [ ] OPS.3 k6: 50 RPS / 5 min, **P95 < 1s** against a **prod-sized** target; artifact attached; target sizing recorded.
- [ ] OPS.4 ZAP baseline: run complete, findings triaged, suppression baseline committed, no un-triaged HIGH.
- [ ] OPS.5 Trivy: both images scanned, no un-triaged HIGH/CRITICAL-fixable; suppressions recorded.

**Operator-gated go-live**
- [ ] OPS.6 Stripe LIVE keys + webhook secret in KV; one live-mode test transaction verified end-to-end (payment → webhook → booking Confirmed).
- [ ] OPS.8 DKIM + SPF verified for the platform ACS sender (DNS propagated; test email passes DMARC alignment).
- [ ] Entra prod cutover complete per `docs/identity/runbooks/entra-prod-cutover-checklist.md`; rollback path (`entra-cutover-rollback.md`) confirmed.

**Hygiene**
- [ ] No remaining `TODO: production` in the codebase.
- [ ] Runbooks current (payment-webhook-failure, notification-dispatch-failures, api-5xx-spike, postgres-cpu-high).
- [ ] App Insights P95-latency + failure-rate alerts armed (compensating control for deferred k6/ZAP CI gating).
- [ ] MASTER_PLAN rows 17–22 reflect final state; deferrals (OPS.1.9, SBOM signing, OPS.2.9, social IdP) filed as backlog.

**Explicitly NOT gates:** OPS.1.9 · SBOM signing · recurring k6/ZAP/Trivy CI jobs · authed-E2E blocking flip · social IdP portal.

---

## §7 Residual-risk register (deferrals)

| Deferral | Residual risk | Compensating control |
|---|---|---|
| OPS.3 k6 not CI-gated | Perf regression uncaught in CI | App Insights P95/error alerts; pre-release re-run |
| OPS.4 ZAP not CI-gated | New endpoint unscanned | Pre-release checklist line; static threat model |
| OPS.5 SBOM signing deferred | Weaker supply-chain provenance | Single-tenant topology (ADR-0013); Trivy at each release |
| OPS.1.9 post-launch | Provider drift not live-verified | Consumer pacts + drift gate + E2E on real API |
| Authed E2E informational | Authed regression not deploy-blocking | Anonymous smoke blocking; nightly authed alerting |
| Scale-to-zero staging | Cold-start deploy latency | OPS.INFRA.3 warm-first; cost win retained |
