# VrBook — Go-Live Runbook (Staging → Production Cutover)

- **Status:** Executable procedure. Companion to the story epic [`docs/stories/EPIC-go-live.md`](../stories/EPIC-go-live.md) and the hardening plan [`docs/OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md).
- **Audience:** the launch coordinator + step owners. Written to be followed by someone other than the author.
- **Golden rule:** **do not skip a go/no-go line.** A red line = a no-go = the cutover stops.

> This runbook does not re-derive the hardening scope — it **sequences the cutover**. Story-level detail lives in the epic; the hardening gate list is reused from [`OPS_LAUNCH_COMPLETION_PLAN.md` §6](../OPS_LAUNCH_COMPLETION_PLAN.md).

---

## Roles

| Role | Who | Owns |
|---|---|---|
| **Launch Coordinator (LC)** | Owner | Calls go/no-go, drives the sequence, comms |
| **Release Engineer (RE)** | Lead eng | Pipeline, deploys, rollback, migrations |
| **Operator (OP)** | Owner | Stripe live keys, DNS, Entra cutover, persona provisioning |
| **On-call (OC)** | Eng | Watches dashboards, triages alerts during + after cutover |

---

## Operator long-poles — START ON DAY 0 (their latency is the critical path, not the code)

These have hours-to-days of external lead time. Kick all of them off **before** any cutover session. Each is a **hard gate** unless noted.

| # | Long-pole | Owner | Lead time | Runbook | Gate? |
|---|---|---|---|---|---|
| L1 | **DKIM + SPF DNS** for the ACS custom sender domain (booking-confirmation email deliverability) | OP | **HIGH — hours-to-24h DNS propagation. Start FIRST.** | [`docs/runbooks/acs-dkim-spf-setup.md`](../runbooks/acs-dkim-spf-setup.md) | **Hard gate** |
| L2 | **Stripe LIVE keys + webhook signing secret** → prod Key Vault | OP | Low (minutes) but needs a live-mode test txn to verify | [`docs/runbooks/stripe-key-rotation.md`](../runbooks/stripe-key-rotation.md) | **Hard gate** (VRB-309) |
| L3 | **Entra prod cutover** — prod tenant / app-reg / user-flows; replace `pending-identity-setup` KV placeholders | OP | Medium | [`entra-prod-cutover-prerequisites.md`](../identity/runbooks/entra-prod-cutover-prerequisites.md) → [`entra-prod-cutover-checklist.md`](../identity/runbooks/entra-prod-cutover-checklist.md); rollback [`entra-cutover-rollback.md`](../identity/runbooks/entra-cutover-rollback.md) | **Hard gate** |
| L4 | **Custom domain DNS + TLS** (apex A/ALIAS + `www` CNAME → Front Door; API CNAME) | OP | **HIGH — propagation.** Use low TTL to validate. | VRB-305 | **Hard gate** |
| L5 | **Prod-sized k6 target** window (prod pre-cutover, or staging temporarily upsized to GP PG + min-1) | RE | Medium | [`docs/runbooks/k6-load-test.md`](../runbooks/k6-load-test.md) | Should (VRB-308) |
| L6 | **KV secret pre-seed** — `stripe-publishable-key`, `acs-sender-address` (gap G6), all `entra-*`, `stripe-*` seeded (even as placeholders) **before** the Bicep deploy or `main.bicep` fails atomically | RE | Low | reference the KV-bind-before-deploy trap | **Hard gate** |

**LC action on Day 0:** confirm L1–L6 are all in flight before Session 1 starts.

---

## Phase A — Prerequisites (land these BEFORE the cutover window)

Nothing in Phase B is safe until Phase A is green. Order:

| Step | Owner | Action | Done-when |
|---|---|---|---|
| A1 | RE | **VRB-301** — `cd-prod.yml` exists; dry-run against `rg-vrbook-prod` with the approval gate proven blocking | Dry-run green; gate shows `Waiting`; no `rg-vrbook-staging` literal in the prod path |
| A2 | RE | **VRB-304** — Postgres restore drill performed; **RTO/RPO measured** and within ≤4h / ≤1h | `docs/ops/drills/restore-drill.md` has timings within target; geo-redundant backup on prod PG |
| A3 | RE | **VRB-303** — migration + forward-fix + idempotency drills; migration-safety checklist written (authoritative: [`docs/runbooks/migration-safety.md`](../runbooks/migration-safety.md); drills: [`docs/ops/drills/migration-drill.md`](drills/migration-drill.md)) | Drills green; migrator no-op on re-run; `Bootstrap:E2e:Enabled=false` confirmed for prod |
| A4 | RE | **VRB-302** — blue-green traffic-shift wired in `_deploy-container-app.yml`; **rollback drill exercised** (< 5 min revert proven) | `docs/ops/drills/rollback-drill.md` shows a proven revert; multi-revision mode on prod apps |
| A5 | OC | **VRB-306** — every alert armed in Bicep + **each validated by inducing the condition in staging**; owner assigned per alert | `az monitor metrics alert list` shows P95/5xx/webhook/PG-CPU/notif-drain/migrator alerts; each fired once in a drill |
| A6 | RE | **VRB-307** — WAF+rate-limit live on prod (Detection→Prevention); Trivy + ZAP triaged; **G5 config fail-fast landed** | No un-triaged HIGH; API refuses to boot without Entra config; secret scan green |
| A7 | RE/OP | **VRB-311** — analytics + conversion tracking live and **consent-gated**; legal pages (Terms/Privacy/Cancellation, Ohio law) owner-approved; data-subject flow verified | No analytics beacon before consent; legal pages footer-linked; owner sign-off on legal content |
| A8 | OP/RE | **VRB-305** — custom domain resolves, TLS valid, redirects (http→https, www↔apex) correct; robots.txt + sitemap.xml + canonical verified | Synthetic check green; no staging FQDN leak (gap G8 resolved) |
| A9 | RE | **VRB-308** — k6 50 RPS / **P95<1s** on a **prod-sized** target; CWV budget met on funnel | Evidence archived with target sizing recorded |

**Phase A exit:** LC confirms A1–A9. Anything red → not ready for the cutover window.

---

## Phase B — Cutover window (the launch)

Run in a scheduled maintenance window. LC narrates; each step has one owner; confirm each against the VRB-306 dashboard before proceeding.

| Step | Owner | Action | Verify | Rollback trigger |
|---|---|---|---|---|
| B0 | LC | **Announce window start** (comms §below); freeze non-launch merges | Team acked | — |
| B1 | OP | Confirm **L2 Stripe live keys** + **L3 Entra prod** + **L1 DKIM propagated** + **L6 KV seeded** | KV holds live values, not `pending-identity-setup`; `dig` shows DKIM CNAME resolves | Any missing → **STOP**, delay window |
| B2 | RE | Run **`cd-prod.yml`** to the approval gate (build→test→scan→staging→**gate**) | Pipeline pauses at `production` environment `Waiting` | Staging smoke red → STOP, fix |
| B3 | LC | **Go/No-Go review** (checklist §below). Sign off. | Every gate line green + owner sign-off recorded | Any red line → **NO-GO**, abort |
| B4 | LC | **Approve** the `production` environment gate | Approval recorded in the run | — |
| B5 | RE | Prod deploy: **migrator Job** runs first | Migrator Job exits 0 | Non-zero → halt before code deploy; run migration-recovery (VRB-303) |
| B6 | RE | Prod deploy: api + web revisions created at **canary %** (VRB-302), health-gated | New revisions `Healthy`; post-deploy smoke (`/health/ready`, `GET /properties`, one authed route) green | Health/smoke fail → traffic **not** shifted; investigate |
| B7 | RE | **Shift traffic** canary→100% on the new revisions | `az containerapp ingress traffic` shows 100% on new revision; smoke green | Errors on shift → revert traffic to last-good (< 5 min) |
| B8 | OP | **Real-money E2E** on prod: guest booking → live payment → webhook → **Confirmed** (VRB-309) | Booking Confirmed in UI; Stripe shows the charge; webhook 200 | Payment/webhook fail → set `IsConfigured=false` (payment-disabled), STOP |
| B9 | OP | **Refund path** verified incl. **executed application-fee reversal** (G37) | Refund + fee reversal visible in Stripe balance txn (not just metadata) | Fee reversal no-op → flag; owner decides go/no-go |
| B10 | OP | **Email deliverability**: trigger a booking-confirmation; confirm **DKIM/DMARC pass** | Test inbox shows aligned DKIM pass | DMARC fail → check L1 propagation; email is core-function gate |
| B11 | OP | **VRB-310 owner UAT** — 6 core flows + host lifecycle on prod; written sign-off | Owner sign-off recorded | Owner withholds → NO-GO on public announcement |
| B12 | OP/L4 | Point **custom domain** at prod; verify TLS + redirects live | Public URL serves prod over TLS | Cert/redirect fail → keep default hostname, delay announce |
| B13 | LC | **Public announcement** (comms §below) | Announcement sent | — |
| B14 | OC | **VRB-313 hypercare** begins — dashboards watched, on-call active | Hypercare day-1 review scheduled | Any P0 → invoke rollback triggers |

---

## Go / No-Go gate checklist

Reuses [`OPS_LAUNCH_COMPLETION_PLAN.md` §6](../OPS_LAUNCH_COMPLETION_PLAN.md). **Every line must be green.** LC signs.

**Pipeline & correctness**
- [ ] `cd-prod.yml` dry-run green; approval gate proven blocking; no `rg-vrbook-staging` literal in the prod path (VRB-301).
- [ ] OPS.INFRA.3 convergence green; `cd-staging-*` green on the launch build.
- [ ] `smoke` (curl) + `playwright-smoke` (anonymous, blocking) green on the launch build.
- [ ] Blue-green traffic-shift wired; **rollback drill exercised** (< 5 min revert) (VRB-302).
- [ ] Migration + restore + forward-fix drills green (VRB-303/304).
- [ ] OPS.2.7 arch tests green, count = **31**; `.auth/` gitignored; no `[AllowAnonymous]` on admin controllers; no test middleware in prod `Program.cs`.

**Hardening evidence (one-time runs archived)**
- [ ] k6: 50 RPS / 5 min, **P95 < 1s** against a **prod-sized** target; artifact attached; sizing recorded (VRB-308).
- [ ] ZAP baseline: run complete, triaged, suppression baseline committed, no un-triaged HIGH (VRB-307).
- [ ] Trivy: both images scanned, no un-triaged HIGH/CRITICAL-fixable; suppressions recorded (VRB-307).
- [ ] WAF + rate-limit live on prod; **G5 config fail-fast** landed (VRB-307).

**Operator-gated go-live**
- [ ] Stripe LIVE keys + webhook secret in prod KV; **real-money txn verified end-to-end** (VRB-309, B8).
- [ ] **Refund + executed application-fee reversal** verified (G37, B9).
- [ ] DKIM + SPF verified; test email passes DMARC alignment (L1, B10).
- [ ] Entra prod cutover complete; rollback path confirmed (L3).
- [ ] Custom domain + TLS + redirects live; robots/sitemap/canonical correct (VRB-305, B12).

**Compliance & product**
- [ ] Analytics + conversion tracking **live before first visitor**, consent-gated (VRB-311, G35).
- [ ] Cookie consent banner + Terms/Privacy/Cancellation pages live and owner-approved (VRB-311, G32).
- [ ] Data-subject ("delete my data") flow verified (VRB-311).
- [ ] **Owner UAT sign-off recorded** (VRB-310, B11).

**Observability & hygiene**
- [ ] All VRB-306 alerts armed with thresholds + owners; each validated in staging.
- [ ] Runbooks current (payment-webhook-failure, notification-dispatch-failures, api-5xx-spike, postgres-cpu-high).
- [ ] No remaining `TODO: production` in the codebase.
- [ ] MASTER_PLAN + CURRENT-GAPS reflect final state; deferrals filed as backlog.

**Explicitly NOT gates** (per OPS plan §2): OPS.1.9 · SBOM signing · recurring k6/ZAP/Trivy CI jobs · authed-E2E blocking flip · social IdP portal · dispute-workflow (G16, documented limitation).

**Go/No-Go decision:** ______  (LC signature + timestamp) → recorded at: ______

---

## Rollback triggers & actions

A trigger fires → **execute the action immediately, then notify**. Rehearsed in the Phase A drills.

| Trigger | Action | Owner | Target |
|---|---|---|---|
| Bad revision at/after traffic shift (5xx spike, smoke fail) | `az containerapp ingress traffic set --revision-weight <lastGood>=100` (VRB-302) | RE | < 5 min |
| Migration failure mid-run | Halt pipeline before code deploy; PITR restore to pre-migration timestamp (VRB-304) **or** forward-fix migration (VRB-303) | RE | ≤ 4h (RTO) |
| Data corruption / loss | PITR restore to ≤1h-old timestamp; repoint `postgres-cs`; restart revisions (VRB-304) | RE | RPO ≤1h, RTO ≤4h |
| Real-money payment/webhook failure | Set Stripe `IsConfigured=false` (payment-disabled mode); STOP launch (VRB-309) | OP | Immediate |
| Application-fee reversal not executing (G37) | Flag; owner decides go/no-go; refunds paused if material | OP | Immediate |
| Auth outage / Entra misconfig | Execute [`entra-cutover-rollback.md`](../identity/runbooks/entra-cutover-rollback.md) | OP | ≤ 30 min |
| WAF blocking legitimate traffic | Flip WAF Prevention→Detection (VRB-307) | RE | < 10 min |
| Email DMARC failing | Investigate DKIM propagation (L1); confirmation email is a core gate — consider pause | OP | Same window |
| Owner withholds UAT sign-off | NO-GO on public announcement; fix blocking defects | LC | — |
| Consent banner not gating analytics | Disable analytics loader (never ship unconsented tracking) | RE | Immediate |

---

## Comms plan

| Moment | Audience | Channel | Owner | Message |
|---|---|---|---|---|
| Window start (B0) | Team | Internal channel | LC | "Prod cutover window open, merges frozen." |
| Go/No-Go (B3) | Team | Internal channel | LC | Decision + who approved. |
| Traffic live (B7) | Team | Internal channel | RE | "Prod serving on new revision, smoke green." |
| Public launch (B13) | Guests / property owners | Public announcement + email | LC | Launch announcement (only after B1–B12 green). |
| Incident (any rollback) | Team + affected owner | Internal + status | OC | Severity, impact, action taken, ETA. |
| Hypercare daily (week 1) | Owner + eng | Daily review | OC | Uptime, P95, webhook %, email %, funnel, incidents. |
| Hypercare exit (T+2w) | Team | Review doc | LC | Steady-state decision + residual backlog. |

---

## Phase C — Hypercare (VRB-313, begins at B14)

- **Duration:** 2 weeks. **Daily review in week 1** (dashboard walk, error triage, webhook + email + funnel check). Then twice-weekly in week 2.
- **On-call:** OC primary, RE secondary, OP for payment/DNS/auth. Every VRB-306 alert routes here.
- **Severity:** **P0** = data loss / payment failure / auth outage → page + rollback trigger. **P1** = degraded (P95 breach, elevated errors) → same-day. **P2** = cosmetic/edge → backlog.
- **First-week review (end of week 1):** uptime vs **99.5%**, P95 vs **<1s**, webhook success vs **≥99.9%**, DKIM/DMARC pass vs **≥99%**, funnel conversion, incident list (PRD §10). Recorded as a review doc.
- **Hypercare exit (T+2w):** go/steady-state decision recorded; residual items filed as backlog (OPS.1.9, SBOM signing, recurring scan CI jobs, dispute workflow G16, social IdP, FE polish G17–G20).

---

## Appendix — key paths

- Pipeline: `.github/workflows/cd-prod.yml` (new, VRB-301), `_deploy-container-app.yml` (blue-green, VRB-302), `cd-staging-{api,web}.yml`.
- Infra: `infra/main.bicep` (`isProd` ladders — Front Door `:111,640`, HA `:230`, backup `:85`, replicas `:91`), `infra/modules/front-door.bicep` (WAF), PG module (backup/geo).
- Drills: `docs/ops/drills/{restore,rollback,migration}-drill.md`.
- Runbooks: `docs/runbooks/{migration-safety,migrator-job-failure,acs-dkim-spf-setup,stripe-key-rotation,k6-load-test,zap-baseline,payment-webhook-failure,notification-dispatch-failures,api-5xx-spike,postgres-cpu-high}.md`; `docs/identity/runbooks/entra-prod-cutover-checklist.md` + `entra-cutover-rollback.md`.
- Gaps closed here: G23 (VRB-301), G24 (VRB-302), G25 (VRB-304), G5 (VRB-307), G37 (VRB-309), G32/G35 (VRB-311), G8 (VRB-305).
