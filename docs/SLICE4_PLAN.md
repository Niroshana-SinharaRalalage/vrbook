# Slice 4 — Notifications That Actually Send (Plan)

**Status**: Proposed 2026-06-14. **Re-review required before implementation begins** — see banner below.
**Author**: Plan agent (architect) consult, 2026-06-14.
**REPLAN section**: `docs/REPLAN.md` §3 Slice 4.
**Scope**: bring email delivery online so the booking funnel sends real mail via Azure Communication Services. Replaces the A9 v1 stub (`user-{guid:N}@stub.vrbook` recipient, no dispatch).

This plan supersedes informal Slice 4 notes — it is the contract.

> ## ⚠️ Re-review required before Slice 4 starts (banner added 2026-06-27)
>
> Slice 4 is now slot 12 (after Slice OPS.M.10) per the architect re-evaluation in `docs/SEQUENCING_RE_EVALUATION_2026_06_27.md`. By the time this slot is reached, the following will have shipped and changed Slice 4's scope:
>
> - **Slice OPS.M.4** ships `TenantAuthorizationBehavior` + extends `BookingPlaced/Confirmed/Cancelled/Rejected/ConflictDetected` events to carry `Guid TenantId` + ships a "every write path sets `tenant_id` consciously" Roslyn-style arch test. Slice 4 inherits that pattern; it does NOT author it.
> - **Slice OPS.M.9** ships `IRlsBypassDbContextFactory<TContext>` + the bypass-RLS connection factory. Slice 4 simply registers the worker's `NotificationsDbContext` via this factory. Slice 4 does NOT author the contract.
> - **Slice OPS.M.7** ships the tenant onboarding wizard. The `tenant.welcome` template + `TenantNotificationHandlers` consuming `TenantActivated` are M.7's responsibility once the ACS pipeline exists (i.e., once this slice ships). M.7 ships first with operator-manual welcomes; the welcome template lands in M.7 retroactively after Slice 4.
>
> Net effect: Slice 4 ships at its **original** 3-day scope (10 templates, not 11; no contract authoring; no event payload bumps; no arch-test authoring). The §2 decisions and §3 commit split below stand. The `notification_log.tenant_id` correctness rule (null for guest-bound, set from event payload for owner-bound) inherits the M.4 pattern.
>
> **Action when this slot is reached**: re-consult the architect to confirm none of M.4–M.10's actual shipped shape contradicts §2; refresh any code references that drifted since 2026-06-14.

---

## 1. What is already shipped (do not re-architect)

- `NotificationLog` aggregate (`Kind`, `Status` ∈ Queued/Sent/Failed/DeadLetter, `RecipientUserId`, `RecipientEmail`, `Subject`, `PayloadJson`, `RetryCount`, `LastError`). `Queue` / `MarkSent` / `RecordFailure` exist; `RecordFailure` already promotes to `DeadLetter` after retry 3.
- `NotificationsDbContext` + migration `20260610153952_A9_InitNotificationsSchema`.
- `BookingNotificationHandlers` — `INotificationHandler<T>` for `BookingPlaced`, `BookingConfirmed`, `BookingCancelled`, `BookingCompleted`. Writes Queued rows. **The recipient email is a stub** (`user-{guid:N}@stub.vrbook`).
- `ListNotificationLogQuery` skeleton.
- ACS resource provisioned via Bicep in `0dbeed6` (Slice 0.5).
- Worker project `src/Workers/VrBook.Workers.Notifications/` is a skeleton with an empty `Task.Delay(1m)` loop.
- ADR-0011 picks ACS over SendGrid and points at LankaConnect's `c:\Work\LankaConnect\src\LankaConnect.Infrastructure\Email\Services\AzureEmailService.cs` as the port source.

## 2. Decisions (architect-locked)

### 2.1 Cross-module recipient email lookup — **Port-and-adapter `IUserEmailLookup`**

`VrBook.Contracts.Interfaces.IUserEmailLookup` returning `UserEmailSnapshot(UserId, Email, DisplayName, Locale)`. Implemented in Identity against `IdentityDbContext.Users`. Mirrors the existing `IPropertyOwnerLookup` pattern.

Why: denormalizing onto events freezes a snapshot that goes stale if email changes mid-funnel. Cross-DbContext queries violate REPLAN §10.1. The port already exists for property ownership; reuse the pattern.

### 2.2 Owner-side notifications — **Separate `OwnerNotificationHandlers`**

A second `INotificationHandler<T>` class for owner-side templates. Mirrors `BookingNotificationHandlers` one-to-one.

Why: keeps each handler single-responsibility (guest vs owner) and lets you grep either side in isolation. Avoids inventing a `Recipients[]` event shape change for a 10-template scope.

### 2.3 `owner.action_required_24h_reminder` scheduling — **`NotBeforeUtc` column on `NotificationLog`**

Column set at queue time inside `BookingNotificationHandlers.Handle(BookingPlaced)`. Worker query gains `AND (not_before_utc IS NULL OR not_before_utc <= NOW())`.

Why: generalizes to every future deferred template, keeps Booking's domain free of notification concerns, no second cron, no `BookingExpiryWorker` churn.

### 2.4 Worker model — **Container App Job, cron `*/2 * * * *`**

Matches `BookingExpiryWorker` and `SyncWorker`. KEDA Service Bus scaling stays deferred (A11). One pattern, one debugging story.

### 2.5 Idempotency on worker retry — **`Sending` lease with `dispatch_started_at` + 5-min timeout**

New enum value `Sending`; new `dispatch_started_at` column. Worker marks `Sending` before ACS call, then `Sent`/`Failed` after. Rows stuck in `Sending` >5 min get reset to `Queued` on next tick (worker crash recovery).

Why: §13 KPI is deliverability, not "occasional dupes." ACS has no documented receive-side dedupe via correlation id; the lease costs one enum + one timestamp; multi-replica safe.

### 2.6 Template loader — **Embedded resource in the module dll, rendered via Stubble**

`<EmbeddedResource Include="Templates\**\*.mustache" />` in the csproj. `Stubble.Core` per ADR-0011.

Why: ADR-0011 specifies embedded for Phase 1; filesystem causes Docker layout debug sessions; Blob is Phase 2.

---

## 3. Commit split (5 commits)

### C1 — `IUserEmailLookup` + Identity adapter + handler swap

Drop the `@stub.vrbook` sentinel. `BookingNotificationHandlers` injects `IUserEmailLookup`. No new behavior, no schema change.

### C2 — Schema + worker skeleton replacement + `IEmailSender` ACS impl

- Migration adds `not_before_utc` (nullable `timestamp with time zone`), `dispatch_started_at` (nullable `timestamp with time zone`), and a `Sending` value to the `NotificationStatus` enum.
- `AzureEmailSender` ports from LankaConnect (`Azure.Communication.Email`, managed-identity-friendly auth).
- Worker rewrite: drains Queued rows under the lease pattern with the 3-retry 2s/4s/8s backoff. **No templates yet** — sends `Subject` and raw `PayloadJson` body. This commit proves the wire to ACS + the lease + the retry math.

### C3 — 10 Mustache templates + Stubble renderer + CI render test

Templates embedded under `Notifications/Templates/`:

- `booking.received.mustache`
- `booking.confirmed.mustache`
- `booking.rejected.mustache`
- `booking.cancelled.guest.mustache`
- `booking.cancelled.owner_notice.mustache`
- `owner.tentative_received.mustache`
- `owner.action_required_24h_reminder.mustache`
- `owner.auto_confirmed.mustache`
- `owner.cancellation_alert.mustache`
- `owner.sync_conflict.mustache`

Shared `_layout.mustache`, `_header.mustache`, `_footer.mustache`. Sample-variables fixtures under `Templates/Samples/*.json`. CI test (`NotificationTemplatesRenderTests`) discovers every embedded template, pairs with its sample file, asserts render returns non-empty body.

### C4 — Owner notifications + deferred reminder + `BookingRejected`

- `OwnerNotificationHandlers` for `BookingPlaced` (enqueues `owner.tentative_received` immediately AND `owner.action_required_24h_reminder` with `NotBeforeUtc = TentativeUntil - 1h`), `BookingCancelled` → `owner.cancellation_alert`, `BookingConfirmed` (when triggered by SLA auto-confirm) → `owner.auto_confirmed`. Plus the existing `SyncConflictDetected` → `owner.sync_conflict`.
- `BookingNotificationHandlers` gains a `BookingRejected` handler → `booking.rejected`.
- Acceptance 1 + 3 are met after this commit.

### C5 — `/admin/notifications` UI + Retry endpoint

- `POST /api/admin/notifications/{id}/retry` resets `Status=Queued, RetryCount=0, LastError=null` so the next cron tick re-sends.
- `web/src/app/admin/notifications/page.tsx` replaces the placeholder: status filter (Queued/Sent/Failed/DeadLetter/Sending), payload preview drawer per row, Retry button on Failed/DeadLetter rows.
- Closes acceptance 2.

---

## 4. Concrete file/folder additions

### Contracts
- `src/VrBook.Contracts/Interfaces/IUserEmailLookup.cs`

### Identity module
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserEmailLookup.cs`
- `IdentityModule.cs` — DI wire-up edit

### Notifications module
- `Domain/NotificationLog.cs` — `NotBeforeUtc`, `DispatchStartedAt`, `Sending` enum value, `Lease()` / `ReleaseLease()` / `Reset()` methods
- `Application/Handlers/OwnerNotificationHandlers.cs`
- `Application/Handlers/BookingNotificationHandlers.cs` — edit to use lookup, add `BookingRejected`
- `Application/Commands/RetryNotificationCommand.cs`
- `Application/Dispatch/NotificationDispatcher.cs`
- `Application/Dispatch/MustacheTemplateRenderer.cs`
- `Application/Dispatch/DrainQueuedNotificationsCommand.cs`
- `Infrastructure/Email/AzureEmailSender.cs`
- `Infrastructure/Persistence/NotificationsDbContext.cs` — column additions
- `Infrastructure/Persistence/Migrations/{date}_Slice4_DispatchColumns.cs`
- `Templates/_layout.mustache`, `_header.mustache`, `_footer.mustache`
- `Templates/booking.received.mustache` … `owner.sync_conflict.mustache` (10 files)
- `Templates/Samples/*.json` (10 sample-variable fixtures)
- `VrBook.Modules.Notifications.csproj` — embedded-resource glob + `Stubble.Core` package
- `Directory.Packages.props` — `Stubble.Core ~1.10`, `Azure.Communication.Email ~1.x`

### Worker
- `src/Workers/VrBook.Workers.Notifications/Program.cs` — rewrite from skeleton; mirror `VrBook.Workers.Booking/Program.cs` shape; dispatch `DrainQueuedNotificationsCommand`; exit code reflects outcome.

### API + UI
- `src/VrBook.Api/Controllers/AdminNotificationsController.cs`
- `web/src/app/admin/notifications/page.tsx` (replaces stub)
- `web/src/lib/api/notifications.ts`

### Tests
- `tests/VrBook.Architecture.Tests/NotificationTemplatesRenderTests.cs` — render every embedded template against `Samples/*.json`, assert non-empty
- `tests/VrBook.Api.IntegrationTests/NotificationsDispatchTests.cs` — fake `IEmailSender` proves Queued → Sent and 3 failures → DeadLetter

### Infra
- None new. ACS resource is already provisioned (Slice 0.5 `0dbeed6`). Verify `infra/modules/containerAppJobs.bicep` declares `notifications-worker` with cron `*/2 * * * *` and ACS connection string env var binds from Key Vault. If absent, this becomes a C2 sub-task.

### Runbook
- `docs/runbooks/notification-dispatch-failures.md` — DeadLetter accrual, stuck `Sending` rows >5 min.

---

## 5. Scope-cut order (when the deadline bites)

Cut from the top:

1. **`owner.sync_conflict` template** — fires off a Sync event, not the §3 funnel. Defer to Slice 5 backfill.
2. **`owner.action_required_24h_reminder` + the `NotBeforeUtc` column.** Cosmetic nudge. Cutting also removes the only column-add, schema-stable slice.
3. **`owner.auto_confirmed` + `owner.cancellation_alert`.** Owner sees state in `/admin/bookings`; email is dashboard-redundant.
4. **Retry UI button (keep the API endpoint).** Operator can curl; the visible row + payload preview still satisfies acceptance 2.
5. **`/admin/notifications` page entirely** — last to go because it is the only browser-visible proof of acceptance 2. If this falls, the slice is effectively cancelled.

Never falls: `IEmailSender` ACS impl, worker, 4 funnel guest templates (`booking.received`, `booking.confirmed`, `booking.rejected`, `booking.cancelled.guest`), `owner.tentative_received`. That minimum satisfies acceptance 1, 3, 4 and proves the wire. Acceptance 2 needs the log page + retry endpoint to survive.

---

## 6. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/SLICE4_PLAN.md`.
2. I open C1 — `IUserEmailLookup` port-and-adapter + handler swap, including the Identity DI wire-up.
3. Each commit ends with: I push, CI runs, I report green/red. Slice ends with: I send a real test confirm in the browser and the email lands in `niroshanaks@gmail.com` inside 60s.

If you reject or want changes: point at the specific decision in §2 or the specific commit in §3; I revise this document and re-submit.
