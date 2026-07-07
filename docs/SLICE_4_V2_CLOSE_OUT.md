# Slice 4 v2 — Close-Out

**Status:** shipped end-to-end.
**Slice plan:** [`SLICE_4_PLAN_V2.md`](SLICE_4_PLAN_V2.md).
**Original plan:** [`SLICE4_PLAN.md`](SLICE4_PLAN.md) (v1; C1-C5 shipped `f42d771` → `5ff46c2`, superseded 2026-07-06).
**Predecessors:** [`OPS_M_15_CLOSE_OUT.md`](OPS_M_15_CLOSE_OUT.md), OPS.M.17 (`bb43bb0`), OPS.M.21.A.3 (`251ea94`), [`OPS_M_16_CLOSE_OUT.md`](OPS_M_16_CLOSE_OUT.md), [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md).

---

## 1. What shipped

### The gap this closed

Slice 4 v1 (commits `f42d771`→`5ff46c2`) shipped C1-C5: the ACS worker, 10 Mustache templates, owner-side notification handlers, and the `/admin/notifications` list + retry surface. Six residuals remained:

1. `tenant.welcome` template + handler — deferred from OPS.M.7 (runbook `OPS_M_7_WELCOME_EMAIL.md` §Slice 4 swap explicitly listed this as the todo).
2. `guest.welcome` template + handler — mentioned in original §1 but not in C3's template set.
3. `notification-dispatch-failures.md` runbook — original §4 line item, never landed.
4. `ListNotificationLogQuery` handler-level guard — pre-4.V2.3 filtered by tenant but didn't require `tenant_admin` role. Post-M.15.3 the controller carries plain `[Authorize]`; without an explicit handler check any authenticated same-tenant caller could enumerate the log.
5. Orphan `booking.cancelled.owner_notice` template + sample — shipped in C3 but never wired to a handler or `TemplateNameFor` case.
6. `review.request` payload enrichment with M.16 `BookingCompleted.Trigger` — the M.16 turnover-aware completion added `Trigger ∈ {"sweep","manual"}` but the review-request template rendered identical copy for both.

### Backend

**Domain + template (4.V2.1 + 4.V2.2 + 4.V2.4):**
- `NotificationKind` enum grew by two values: `TenantWelcome = 40`, `GuestWelcome = 41`. Range 40-49 reserved for lifecycle-of-user templates.
- `MustacheTemplateRenderer.TemplateNameFor` extended with cases for both new kinds.
- `Templates/tenant.welcome.mustache` (NEW) — mirrors the OPS.M.7 runbook body verbatim.
- `Templates/guest.welcome.mustache` (NEW) — minimal per §7-Q5-A ("You're signed in. Continue where you left off / browse properties"). Uses Mustache `{{#HasReturnTo}}`/`{{^HasReturnTo}}` section fork.
- `Templates/review.request.mustache` — Mustache `{{#Manual}}`/`{{^Manual}}` fork added; manual completion gets "Thanks for checking out ... the host just closed out your stay" copy variant.
- `Templates/Samples/*.json` (NEW + updated) — three fixtures added / edited.

**Handlers (4.V2.1 + 4.V2.2 + 4.V2.4):**
- `TenantNotificationHandlers` (NEW) — `INotificationHandler<TenantMembershipCreated>`. Fires only when the role is `tenant_admin` AND the tenant's active tenant_admin membership count is exactly 1 (§7-Q1-A: only the founding admin gets welcomed). Race-free by construction.
- `GuestNotificationHandlers` (NEW) — `INotificationHandler<UserRegistered>`. Ignores `UserOidRebound` per §7-Q2-A. Queues with `tenant_id = null` (guests are tenant-less per MTOP §1).
- `BookingNotificationHandlers.Handle(BookingCompleted)` — payload extras now include `Trigger` + `Manual` for the review-request row.

**Query guard (4.V2.3):**
- `ListNotificationLogQuery` handler reshaped to mirror `RetryNotificationHandler`'s M.17 pattern: PlatformAdmin bypass unchanged; non-PA callers require `HasTenantRole(callerTenant, "tenant_admin")`; role miss throws `ForbiddenException`. `§7-Q3-A` locked: keep the app-layer filter as the enforcement point (RLS tighten deferred to OPS.M.10.1).

**Cross-module port (4.V2.1):**
- `ITenantSetupContextLookup` (NEW, on Contracts) — returns tenant Slug + DisplayName + active tenant_admin membership count. Impl `TenantSetupContextLookup` in Identity module. Kept in Contracts so Notifications doesn't take an Identity module dependency.

**Cleanup (4.V2.3):**
- `Templates/booking.cancelled.owner_notice.mustache` + sample — DELETED per §7-Q4-A.

### Docs (4.V2.5)

- `docs/runbooks/notification-dispatch-failures.md` (NEW) — 8-section runbook covering DeadLetter/Sending triage, template rendering error branch, worker cron failure branch, ACS outage branch, admin retry paths, escalation ladder.
- `docs/runbooks/OPS_M_7_WELCOME_EMAIL.md` — DELETED (retired; 4.V2.1's handler supersedes).
- `docs/MASTER_PLAN.md` — Slice 4 row flipped from ⏭ to ✅.
- `docs/SLICE4_PLAN.md` — top-of-file "Superseded" banner; original §s preserved verbatim.

### Arch tests

Landed across three files (4.V2.1 + 4.V2.2 + 4.V2.3):
- `Slice4V2_TenantNotificationHandlersShapeTests` (6 facts).
- `Slice4V2_GuestNotificationHandlersShapeTests` (5 facts).
- `Slice4V2_ListNotificationLogGuardShapeTests` (4 facts).

Plus the pre-existing `NotificationTemplatesRenderTests` enum-iteration theory automatically covers `TenantWelcome` + `GuestWelcome` rendering against their sample fixtures.

**15 new arch facts. No 4.V2.6 consolidated pack needed** — the per-sub-commit files cover the invariants without redundancy.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `829fdea` | Plan | Architect brief committed as `SLICE_4_PLAN_V2.md` + §7 locked answers. Docs-only. |
| `fc2ad08` | 4.V2.1 | tenant.welcome template + `TenantNotificationHandlers` + `ITenantSetupContextLookup` + 6 arch facts. |
| `1dfa80c` | 4.V2.2 | guest.welcome template + `GuestNotificationHandlers` + 5 arch facts. |
| `91f8f72` | 4.V2.3 | ListNotificationLogQuery M.17-parity guard + orphan template delete + 4 arch facts. |
| `eb9cc77` | 4.V2.4 | BookingCompleted payload gains Trigger/Manual; review.request template fork. |
| `dcbcf31` | 4.V2.5 | notification-dispatch-failures runbook + OPS_M_7_WELCOME_EMAIL retirement + MASTER_PLAN flip + SLICE4_PLAN banner. Docs-only. |
| _(this commit)_ | 4.V2.6 | Close-out doc. Docs-only. |

**Total: 4 code sub-commits + 3 docs sub-commits + 1 pre-slice architect brief.**

Matches the plan §1 sequence with one variance: the plan's 4.V2.6 called for a consolidated `Slice4V2_NotificationsShapeTests.cs` (6 facts). Skipped because the per-sub-commit arch files already cover the invariants without gaps; adding another test file would duplicate coverage.

---

## 3. Where the plan diverged

### Followed the plan mostly

- All 5 code sub-commits shipped exactly the surface the plan said.
- Owner-locked §7 answers held throughout — no re-litigation.
- No RED-then-GREEN needed (existing arch tests + `NotificationTemplatesRenderTests` covered the shape from the first commit onward).

### One-session budget held

The plan estimated one session. Delivered in one session (2026-07-06 evening).

### 4.V2.6 consolidated arch test pack — skipped

Per §Arch tests above, the per-sub-commit test files (15 facts total) already cover the load-bearing invariants (correct event subscription, correct role guards, correct tenant_id shape, correct enum → template mapping). A consolidated pack would duplicate rather than complement. Documented as a deliberate skip.

### Founding-admin double-welcome accepted

Per §7-Q2 rationale: users who sign up + immediately set up a workspace via the OPS.M.7 wizard receive both a `guest.welcome` (from `GuestNotificationHandlers` on `UserRegistered`) AND a `tenant.welcome` (from `TenantNotificationHandlers` on `TenantMembershipCreated` for their founding tenant_admin membership). The two events model different lifecycle stages; the rare double-send is lower cost than a race-prone suppression check across the wizard flow. Called out as a deliberate design choice, not a bug.

---

## 4. What was deferred / follow-ups

- **RLS policy tighten (§7-Q3-A)** — `notifications.notification_log` RLS still permits NULL-tenant reads across tenants for anyone matching `is_platform_admin_bypass = false`; app-layer filter in `ListNotificationLogQuery` + `RetryNotificationHandler` is the enforcement point. A dedicated OPS.M.10.1 slice tightens the policy across every nullable-tenant table (audit #16.b lives there).
- **`owner.sync_conflict` handler** — placeholder still returns `Task.CompletedTask` in `OwnerNotificationHandlers.Handle(BookingConflictDetected)`. Needs an `IBookingOwnerLookup(bookingId) → propertyOwnerId` cross-module read that doesn't exist. Defer to next Sync-touching slice (Phase 2 or a dedicated OPS.SYNC.1 if operator sees prod sync conflicts).
- **Custom ACS domain + DKIM** — OPS.8 scope per MASTER_PLAN §4 rule 6.
- **Per-Kind priority lanes** — ACS 429 storm mitigation. Phase-2 optimization; today's mitigation is worker cron throttle via runbook §6.

---

## 5. Rollback runbook

Zero database changes. Zero infra changes. Rollback is code-only.

- **Tier-1 (per sub-commit revert)** — `git revert <commit-sha>` on any of 4.V2.1..4.V2.5. Each is independent. Order dependencies:
  - Reverting 4.V2.1 leaves the `TenantWelcome = 40` enum value dangling; the next `NotificationTemplatesRenderTests` run fails (no case in `TemplateNameFor`). Include the renderer + enum change in the revert.
  - Reverting 4.V2.3 restores the orphan template but not the older guard shape; either restore both or accept the enumeration hole.
- **Tier-2 (revert-all)** — `git revert dcbcf31^..829fdea` restores the pre-4.V2 shape. Test fixtures + arch tests revert atomically.
- **The Slice 4 v1 (C1-C5) shape is not touched by this rollback** — the ACS worker + owner handlers + retry surface all remain.

No secret / KV / Bicep changes to revert.

---

## 6. Staging walk verification

**Preconditions:** slice deployed to staging; ACS resource healthy; at least one operator account with `tenant_admin` membership; one staging guest email addressable.

- Create a new tenant via `/admin/onboarding` wizard as a fresh user. Expect the founding admin's inbox to receive `tenant.welcome` within 2 min (worker cron `*/2 * * * *`). Verify `notification_log` row has `Kind = TenantWelcome` + `TenantId` set.
- Sign up a fresh guest via `GuestSignUpSignIn` (any social IdP OR Entra local). Expect `guest.welcome` within 2 min. Verify row has `Kind = GuestWelcome` + `TenantId = null`.
- Complete a booking via the daily sweep + separately via the "Complete now" button. Verify TWO `review.request` rows are queued (one with `Trigger = sweep`, one with `Trigger = manual`). Manipulate `NotBeforeUtc` in the DB to skip the 24h wait and observe the copy variant.
- As a non-PlatformAdmin user without `tenant_admin` membership, hit `GET /api/v1/admin/notifications` → expect 403 with rule "Notification log requires tenant_admin role in the tenant." (post-4.V2.3 guard).
- Retry a Failed row via `/admin/notifications` UI as tenant_admin → expect 200.

If any step fails, cross-reference `docs/runbooks/notification-dispatch-failures.md` §3 for diagnostic queries.

---

## 7. Prod cutover checklist

Zero database migrations. Zero KV secrets. Zero Bicep changes. Pure code + docs.

**Pre-deploy:**
- Verify ACS `AzureManagedDomain` free-tier quota (1000 emails/24h) is comfortable for the expected new-user + new-tenant rate. If prod has multiple new signups/hour, plan for a Standard-tier bump.
- No arch tests should skip in the prod CI run; per-Kind render coverage protects the pipeline shape at compile time.

**Post-deploy smoke:**
- Trigger a synthetic tenant via `POST /api/v1/admin/tenants` (PlatformAdmin only) — expect `tenant.welcome` in the seeded admin's inbox within 2 min.
- Query `notifications.notification_log` for new rows in the last 10 min. Every Kind should show 0 or Sent; if any is Failed, dispatch runbook.
- Container App Job execution history should show consistent 2-min ticks.

---

## 8. Session debt

Nothing outstanding. Plan followed with one variance (§3): the 4.V2.6 consolidated arch test pack was skipped as duplicate coverage. Documented as deliberate.

Follow-up work seeded in §4.
