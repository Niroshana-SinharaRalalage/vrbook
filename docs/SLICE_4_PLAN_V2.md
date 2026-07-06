# Slice 4 v2 — Notifications residuals + post-M.21 reshape

- **Status:** LOCKED — §7 architect-recommended answers adopted per memory rule `feedback_technical_decisions_are_architect_call`. Ready to execute.
- **Date:** 2026-07-06.
- **Author:** Platform Enterprise Architect (agent) via Slice 4 re-review consult.
- **Predecessors:** [`SLICE4_PLAN.md`](SLICE4_PLAN.md) (original 2026-06-14 plan, C1–C5 shipped `f42d771`→`5ff46c2`), [`OPS_M_15_CLOSE_OUT.md`](OPS_M_15_CLOSE_OUT.md), OPS.M.17 (`bb43bb0`), OPS.M.21.A.3 (`251ea94`), [`OPS_M_16_CLOSE_OUT.md`](OPS_M_16_CLOSE_OUT.md), [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md), [`runbooks/OPS_M_7_WELCOME_EMAIL.md`](runbooks/OPS_M_7_WELCOME_EMAIL.md).
- **Supersedes:** the "start Slice 4 fresh" reading of the banner in [`SLICE4_PLAN.md`](SLICE4_PLAN.md). The banner mandated a re-review; this doc IS the re-review. Do NOT rewrite what shipped.
- **Scope:** ONE vertical slice, split into 5 GREEN sub-commits + 1 DOCS sub-commit. Ships the four residuals of the original §Residuals implicit list, plus the two owner-lockable follow-ups the post-M.21 shape surfaced. Explicitly NOT a re-do of C1–C5.

---

## §0 What we're doing + why now

### §0.1 The Slice 4 v1 shipped state (do not re-architect)

Every C1–C5 sub-commit of the original plan is on `main` and running in staging. `f42d771` (C1, `IUserEmailLookup`) → `e277a70` (C2, worker + ACS + lease) → `8113e8f` (C3, 10 Mustache templates + renderer) → `6b41f2b` (C4, owner handlers + deferred reminder + `BookingRejected`) → `e2e3e75` (C5, `/admin/notifications` + retry). Plus four polish commits (`244873b` ACS sender uses managed domain, `d81a669` link ACS service to email domain, `65b517b` sidebar nav, `0ecc0b1` template enrichment, `c27a18c` in-transaction lookup fix, `5ff46c2` scoped publisher fix). The MASTER_PLAN row for Slice 4 still reads `⏭ after Slice OPS.M.10` — that row is stale, not a work signal. The banner in `SLICE4_PLAN.md` predates the shipped state; this doc closes it.

### §0.2 What's actually left

Six things. Three come from the original Slice 4 plan implicit residuals (never scoped as sub-commits). Three come from the post-M.21 state that didn't exist when the original plan was written:

1. **`tenant.welcome` template + handler on `TenantMembershipCreated`** — the OPS.M.7 runbook `OPS_M_7_WELCOME_EMAIL.md` explicitly deferred this to "Slice 4"; still runs as an ops-manual query in staging. §Slice 4 swap in that runbook is the todo list.
2. **`guest.welcome` template + handler on `UserRegistered`** — the original SLICE4_PLAN §1 mentioned "Welcome (post-signup)". Not in C3's 10 templates; no handler exists.
3. **Runbook `docs/runbooks/notification-dispatch-failures.md`** — the original §4 Runbook line called this out; never landed.
4. **NULL-tenant RLS policy tighten** — OPS.M.10.2 F7 audit #16.b noted RLS itself permits NULL-tenant reads across tenants; today the handler filters at app layer. §5-Q3 answer keeps this shape (deferred to dedicated OPS.M.10.1 slice).
5. **`HasTenantRole` guard consistency on `ListNotificationLogQuery`** — parity with M.17 handler-level shape.
6. **Orphan template cleanup** — `booking.cancelled.owner_notice.mustache` unused; delete.

### §0.3 Why now (post-M.21, not pre-M.4)

- Post-M.4 event payload shape stable. `TenantCreated` carries `TenantId, Slug, DisplayName`; `UserRegistered` carries `UserId, Email, DisplayName`. Both frozen — handlers write directly against them.
- Post-M.9 RLS is live on `notifications.notification_log`; the tenant.welcome row must set `tenant_id = TenantCreated.TenantId` at queue time so RLS reads match.
- Post-M.12 social IdPs mean `UserRegistered` fires for guest signups via Google/Microsoft/Facebook/Apple flows too; the welcome must NOT gate on `provider == 'entra'`.
- Post-M.15 `[Authorize(Roles="Owner,Admin")]` decorators are all gone; any new controller path added here follows M.15/M.17 shape from day one.
- Post-M.16 `BookingCompleted` carries `Trigger` (`"manual" | "sweep"`). The v1 review-request deferral fires for BOTH triggers — no bug, but the payload should reflect trigger so template can subtly differentiate copy. Optional.
- Post-M.21 no `IsOwner`/`IsAdmin` fields anywhere. Confirmed clean in `BookingNotificationHandlers` + `OwnerNotificationHandlers`; no drift to fix.

---

## §1 Sub-commit sequence (6 sub-commits, one session)

Sub-commit convention: `Slice 4.V2.N: <summary>`. All target `develop`. All GREEN — no RED-then-GREEN needed since arch tests for the notification pipeline already exist (`NotificationTemplatesRenderTests`, `OpsM17` handler-guard facts).

```
4.V2.1 GREEN — tenant.welcome template + TenantNotificationHandlers on TenantMembershipCreated.
4.V2.2 GREEN — guest.welcome template + GuestNotificationHandlers on UserRegistered.
4.V2.3 GREEN — Handler-level guard consistency on ListNotificationLogQuery + orphan template cleanup.
4.V2.4 GREEN — Enrich BookingCompleted payload with Trigger; refine review.request copy branch.
4.V2.5 DOCS  — Runbook notification-dispatch-failures.md + delete OPS_M_7_WELCOME_EMAIL.md + MASTER_PLAN row flip.
4.V2.6 GREEN — Consolidated arch-test pack + close-out doc.
```

**Session budget: 1 session.** Six sub-commits, four code-carrying, one docs-only, one arch+close-out. Nothing touches infra (ACS is already provisioned + wired).

### 4.V2.1 — `tenant.welcome` template + `TenantNotificationHandlers`

- `src/Modules/VrBook.Modules.Notifications/Templates/tenant.welcome.mustache` (NEW) — mirrors OPS.M.7 runbook body verbatim.
- `src/Modules/VrBook.Modules.Notifications/Templates/Samples/tenant.welcome.json` (NEW) — `{DisplayName, OwnerFirstName, Slug, DashboardUrl}`.
- `src/Modules/VrBook.Modules.Notifications/Application/Handlers/TenantNotificationHandlers.cs` (NEW) — `INotificationHandler<TenantMembershipCreated>` per §6-A1 + §5-Q1-A locked. Filters `Role == "tenant_admin"` AND checks this is the first tenant_admin membership for that tenant. Queues `TenantWelcome` row with `TenantId = tenant.Id`.
- `src/Modules/VrBook.Modules.Notifications/Domain/NotificationLog.cs` — `TenantWelcome = 40` enum value.
- `MustacheTemplateRenderer.TemplateNameFor(TenantWelcome) => "tenant.welcome"`.
- Optionally: `ITenantFoundingAdminLookup` on Contracts (§6-A2). May be unnecessary if the `TenantMembershipCreated` event already carries `UserId`.
- 3 unit tests.

### 4.V2.2 — `guest.welcome` template + `GuestNotificationHandlers`

- `src/Modules/VrBook.Modules.Notifications/Templates/guest.welcome.mustache` (NEW) — minimal per §5-Q5-A locked ("You're signed in. Continue where you left off: {ReturnTo}.").
- `src/Modules/VrBook.Modules.Notifications/Templates/Samples/guest.welcome.json` (NEW).
- `src/Modules/VrBook.Modules.Notifications/Application/Handlers/GuestNotificationHandlers.cs` (NEW) — `INotificationHandler<UserRegistered>`. Skip if user has a tenant_admin membership at handle time (self-suppress the double-welcome for the tenant-admin-becomes-guest edge). Row: `TenantId = null`, `Kind = GuestWelcome`.
- `Domain/NotificationLog.cs` — `GuestWelcome = 41`.
- `MustacheTemplateRenderer.TemplateNameFor(GuestWelcome) => "guest.welcome"`.
- Ignore `UserOidRebound` per §5-Q2-A locked.
- 4 unit tests.

### 4.V2.3 — `ListNotificationLogQuery` handler-level guard + orphan cleanup

- `src/Modules/VrBook.Modules.Notifications/Application/Queries/ListNotificationLogQuery.cs` — reshape to mirror `RetryNotificationHandler`'s two-branch pattern (row-tenant-set → `HasTenantRole` equality; row-tenant-null → PlatformAdmin only). Non-PA callers see zero NULL-tenant rows.
- Delete `Templates/booking.cancelled.owner_notice.mustache` + `Samples/booking.cancelled.owner_notice.json` (orphan per §5-Q4-A locked). Confirm no `TemplateNameFor` reference before delete.
- `tests/VrBook.Architecture.Tests/Slice4V2_NotificationHandlerGuardShapeTests.cs` (NEW, Unit, 2 facts).

### 4.V2.4 — Enrich `review.request` payload with trigger context

- `BookingNotificationHandlers.Handle(BookingCompleted)` — payload extras gain `["Trigger"] = n.Trigger`.
- `Samples/review.request.json` — add `Trigger` field.
- Optional `{{#Manual}}` block in the template.
- No test additions beyond the existing `NotificationTemplatesRenderTests` re-render.

### 4.V2.5 — Runbook + docs sweep (DOCS-only)

- `docs/runbooks/notification-dispatch-failures.md` (NEW).
- `docs/runbooks/OPS_M_7_WELCOME_EMAIL.md` — delete.
- `docs/MASTER_PLAN.md` — flip Slice 4 row to ✅.
- `docs/SLICE4_PLAN.md` — append "Superseded by v2" banner at top.
- No CI (doc-only path-filter).

### 4.V2.6 — Arch-test pack + close-out

- `tests/VrBook.Architecture.Tests/Slice4V2_NotificationsShapeTests.cs` (NEW, Unit, 6 consolidated facts).
- `docs/SLICE_4_V2_CLOSE_OUT.md` (NEW) — standard 8-section close-out.

---

## §2 What survives from the original plan; what contradicts current shape

### §2.1 Survives (do not re-architect)

- `IUserEmailLookup` port-and-adapter — shipped C1, live.
- Separate `OwnerNotificationHandlers` — shipped C4, live. Extend with `TenantNotificationHandlers` for tenant-lifecycle events (a THIRD handler class, per §6-A1).
- `NotBeforeUtc` column on `NotificationLog` — shipped C2, live.
- Container App Job cron `*/2 * * * *` — shipped C2, live.
- `Sending` lease + `dispatch_started_at` 5-min timeout — shipped C2, live.
- Embedded resource + Stubble — shipped C3, live.

### §2.2 Contradicts / must be reshaped

- **Original "10 templates" constraint** — was written when Slice 4 was to ship BEFORE OPS.M.7. Post-M.7 the runbook `OPS_M_7_WELCOME_EMAIL.md` explicitly re-attributes `tenant.welcome` to Slice 4. Number is now 11-12 (10 original + `tenant.welcome` + optional `guest.welcome`).
- **Original C5 "acceptance 2 requires the log page + retry endpoint"** — shipped, but M.17 `bb43bb0` moved the tenant-admin gate to the handler; 4.V2.3 aligns `ListNotificationLogQuery` for parity.
- **Original §3 C4 "`owner.sync_conflict` handler"** — placeholder is still `logger.LogInformation` returning `Task.CompletedTask`. Per §6-A6 locked, **defer to Phase 2 / a Sync-touching slice**. NOT this slice.
- **Original §4 "Runbook `notification-dispatch-failures.md`"** — never landed. Reshaped in 4.V2.5.

---

## §3 Migration surface

| Sub-commit | Files added | Files modified | Files deleted |
|---|---|---|---|
| 4.V2.1 | 4 files (template, sample, handler, tests) | 3 files (NotificationLog enum, renderer, module DI) | — |
| 4.V2.2 | 4 files | 2 files (enum, renderer) | — |
| 4.V2.3 | 1 file (arch test) | 1 file (query) | 2 files (orphan template + sample) |
| 4.V2.4 | — | 3 files (handler, template, sample) | — |
| 4.V2.5 | 1 file (runbook) | 2 files (MASTER_PLAN, SLICE4_PLAN) | 1 file (superseded runbook) |
| 4.V2.6 | 2 files (arch tests, close-out) | 1 file (MASTER_PLAN revision log) | — |

No infra Bicep changes. No new KV secrets. No new packages.

---

## §4 Risks + mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Guest signup via social IdP fires `UserRegistered` twice from race | Low | Low | Locked: fire on `UserRegistered` only, ignore `UserOidRebound` (§5-Q2-A). Accept rare duplicate — deliverability is the KPI. |
| 2 | ACS quota — the `AzureManagedDomain` free tier caps at 1000 emails/24h | Low | High if hit | Runbook 4.V2.5 documents quota + dashboard link. Portal quota bump is a 5-min ops action; not gated by this slice. |
| 3 | Template rendering error at dispatch → DeadLetter → operator retry loop | Low | Medium | `NotificationTemplatesRenderTests` (existing) exercises every embedded template against sample fixture; new templates add samples so CI catches errors pre-deploy. |
| 4 | Deleting `booking.cancelled.owner_notice.mustache` breaks a `TemplateNameFor` case | Very low | Low | Grep-verify before delete; arch test 4.V2.3 fact #2 pins resource absence. |
| 5 | ACS 429 storm starves booking-critical dispatch | Very low | High | Existing 2s/4s/8s backoff + DeadLetter budget handles it; runbook 4.V2.5 tells ops to pause worker cron via `az containerapp job stop`. Phase-2 fix is per-Kind priority lanes. |
| 6 | Runbook delete of `OPS_M_7_WELCOME_EMAIL.md` breaks a hard link | Very low | Low | Grep before delete; runbook already reads "Delete this runbook" in its own §Slice 4 swap section. |

---

## §5 Product / policy questions (originally owner-facing; all locked to architect recommendation)

Per repo memory `feedback_technical_decisions_are_architect_call` — owner directive 2026-07-06 that technical questions in an architect-consult plan's §5 are the architect's call, not the owner's. All §5 questions below flagged with the recommended answer + rationale; locked in §7.

- **Q1 Tenant welcome timing** — Handle `TenantMembershipCreated` where `Role == tenant_admin` AND this is the tenant's first tenant_admin membership. Race-free by construction. Recommendation: **A**.
- **Q2 Guest welcome duplicate handling** — Fire on `UserRegistered` only; ignore `UserOidRebound` (a metadata refresh, not a signup). Recommendation: **A**.
- **Q3 NULL-tenant RLS policy tighten** — Keep the current shape; the app-layer filter in `ListNotificationLogQuery` + `RetryNotificationHandler` is the enforcement point. Defer B/C to a dedicated OPS.M.10.1 RLS-tighten slice (audit #16.b lives there). Recommendation: **A**.
- **Q4 Orphan template cleanup** — Delete `booking.cancelled.owner_notice.mustache` + sample. Dead code accrues risk. Recommendation: **A**.
- **Q5 Guest welcome content** — Minimal: "You're signed in. Continue where you left off: {ReturnTo}." Doesn't compete with the booking funnel. Recommendation: **A**.

---

## §7 Locked answers (2026-07-06)

All architect recommendations from §5 locked. Owner-directive-driven per memory rule — technical / low-stakes-product-shape questions are architect's call.

- **Q1 (Tenant welcome timing) → A.** Handle `TenantMembershipCreated`, first tenant_admin membership. Race-free.
- **Q2 (Guest welcome duplicate) → A.** Fire only on `UserRegistered`.
- **Q3 (RLS tighten) → A.** Keep app-layer filter; defer B to OPS.M.10.1.
- **Q4 (Orphan template) → A.** Delete.
- **Q5 (Guest welcome content) → A.** Minimal.

Plus §6-locked technical answers (architect's call throughout):

- **§6-A1** `TenantNotificationHandlers` as a third handler class (not merged into Owner).
- **§6-A2** `ITenantFoundingAdminLookup` on Contracts, impl in Identity, if `TenantMembershipCreated` event payload doesn't carry `UserId` directly.
- **§6-A3** Enum values `TenantWelcome = 40`, `GuestWelcome = 41`. Reserved 42-49 for future lifecycle events.
- **§6-A4** `ListNotificationLogQuery` reshape to mirror `RetryNotificationHandler` two-branch pattern.
- **§6-A5** `review.request` `Trigger` in payload, not new template.
- **§6-A6** Deferred `owner.sync_conflict` — do NOT scope in v2.
- **§6-A7** No infra Bicep changes.
- **§6-A8** Session budget = 1.

---

## §8 Close-out checklist

- [ ] All 6 sub-commits shipped in order; each CI-green under `dotnet test --filter "Category!=Integration"` + `dotnet publish -c Release`.
- [ ] `NotificationTemplatesRenderTests` (pre-existing) passes with new templates + fewer orphans.
- [ ] Behavior verified in staging: create a new tenant → founding admin receives `tenant.welcome` inside 2 min. Sign up a fresh guest → inbox receives `guest.welcome` inside 2 min. Complete a booking → 24h later receives `review.request` (verify by manipulating `NotBeforeUtc`).
- [ ] MASTER_PLAN row flipped ✅; revision log entry appended.
- [ ] `docs/SLICE_4_V2_CLOSE_OUT.md` documents the 6 sub-commits + staging-verification.
- [ ] `docs/SLICE4_PLAN.md` has "superseded by v2" banner at top; original body untouched.
- [ ] `docs/runbooks/OPS_M_7_WELCOME_EMAIL.md` deleted; grep confirms no dangling links.
- [ ] `docs/runbooks/notification-dispatch-failures.md` present with sections a-e.
- [ ] Arch tests: `Slice4V2_NotificationsShapeTests` (6 facts) + `Slice4V2_NotificationHandlerGuardShapeTests` (2 facts) both green on `develop`.
- [ ] No new secrets in KV.

---

Ready to execute Slice 4.V2.1.
