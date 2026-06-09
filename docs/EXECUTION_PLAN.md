# VrBook Master Execution Plan

> **Source of truth for execution order, definition-of-done, and cross-cutting standards.**
> If you (Claude) ever feel uncertain about scope or sequencing, re-read this file
> before doing anything else. ONE numbering system: the proposal's **A-numbers**.
> Amendments are `A{N}.{M}`. Frontend = F1. Admin polish = O1. Operational
> hardening = `OPS.{N}`. Polish backlog = `POLISH.{N}`. **Never invent a parallel
> system (no "Phase 1", no "Sprint 3", no "Wave A"). One TODO, one tag scheme.**

Last revised: 2026-06-08

---

## 1. North Star

VrBook is a **direct-booking platform** competing with AirBnB on host fees and guest service fees. **Phase 1 ships in 8 weeks** as a modular monolith on Azure Container Apps. The proposal is `BookingApp_Proposal.md`; all architectural decisions anchor to it.

The **single most load-bearing engineering constraint** is *zero double-bookings between AirBnB and the direct site* (proposal §1). That makes **A6 (Sync) not optional** — it's the safety floor.

---

## 2. Agent registry (proposal §20.2) — current status

| Tag | Scope | Status (2026-06-08) |
|---|---|---|
| **A0** | Foundation: monorepo, contracts, CI, Bicep, ADRs | ✅ done |
| **A1** | Identity & Auth: Entra External ID, `/me`, audit, role+ownership pipeline | ✅ done (`A1.1` IT fix pending) |
| **A2** | Catalog & Search | ✅ v1 done; `A2.1` overlap guard ✅; `A2.2` amenity expansion pending |
| **A3** | Pricing Engine | ✅ v1 done; `A3.1` full 5-rule engine pending |
| **A4** | Booking & State Machine | ✅ v1 done; `A4.1` hold + SLA queue pending |
| **A5** | Payments & Stripe | ✅ done & verified |
| **A6** | Sync (AirBnB iCal) | ❌ NOT STARTED — **load-bearing per §1** |
| **A7** | Messaging & SignalR | ❌ NOT STARTED |
| **A8** | Reviews & Loyalty | ⚠️ Reviews v1 shipped out of order; `A8.1` Loyalty + Reviews moderation pending |
| **A9** | Notifications & Templates (ACS email per ADR 0011) | ❌ NOT STARTED |
| **F1** | Frontend Web App (Next.js + Playwright E2E) | ⚠️ shipped continuously alongside backend; E2E suite (`F1.1`) missing |
| **O1** | Admin Dashboard polish: reports, conflict modal, toggles, alerts | ❌ NOT STARTED |

**Cross-cutting amendments to A0 (because foundation work continues to surface):**

- **`A0.1`** Observability + exception handling hardening — ✅ COMPLETE 2026-06-08 (all 8 subtasks, 7 commits, 21 unit tests passing)
- **`A0.2`** Test debt paydown + TDD discipline + CI gate — IN PROGRESS

---

## 3. The "CQRS reservation confirmation system" you asked about

It's not its own agent. It's the proposal §7 outbox + §11.3 projections backbone, and it spans existing A-numbers:

- **Write side (Command)** — A4 emits domain events in-transaction on every state change (`BookingPlaced`, `BookingConfirmed`, `BookingCancelled`, `BookingCheckedOut`, `BookingCompleted`, `BookingRejected`, `BookingDisputed`, `RefundIssued`)
- **Bus** — Service Bus topics carry events out-of-process (A0 wired this; consumers haven't been attached)
- **Read side (Query) consumers / projections** — each lives in a different agent:
  - **A9** Notifications → emails on every lifecycle event
  - **A7** Messaging → auto-create Thread on `BookingConfirmed`
  - **A8.1** Loyalty → tier promotion on `BookingCompleted`
  - **A2** Catalog → recompute `rating_avg` on `ReviewApproved` (already wired in A8 v1)
  - **O1** Admin → projections (`admin.revenue_daily`, `booking_funnel`, `property_occupancy`)
  - **A6** Sync → owner conflict resolution cascades to `BookingRejected` + refund

**Today: A4 emits the events. Zero consumers exist.** Every event goes nowhere. **A6, A7, A8.1, A9, O1 close that loop** — each one wires up its slice of the read side.

---

## 4. Cross-cutting standards (apply to every A-number forever)

These are ADR-locked and the most common source of regressions if forgotten.

### 4.1 TDD discipline (mandatory)

Every A-number follows red→green→refactor:
1. Write the failing unit test for the next slice of domain behaviour
2. Write the minimum code to pass
3. Refactor with the safety net of the test

**Integration tests are written FIRST for any new API endpoint** — assert the expected 200/4xx/5xx shape before writing the handler.

### 4.2 Observability (ADR 0010, proposal §14)

**Every** handler must:
- Use `ILogger<T>` (constructor-injected)
- Emit structured properties via `LogContext`: `userId`, `traceId`, `bookingId`, `propertyId`, `correlationId` where relevant
- Log entry/exit at `Information`; failures at `Warning` with `ex.GetType().Name`
- NEVER log raw email/phone/address/full name — `IDestructuringPolicy` filters at sink

**Azure Log Analytics workspace** is the diagnostic surface:
- All container logs → workspace via Container Apps platform integration
- Saved KQL queries in `docs/observability/queries.kql` (created by A0.1)
- Workbook with sections per §14 (created by A0.1)
- 8 alert rules per ADR 0010 deployed as Bicep `scheduledQueryRules`

**Lessons from this session that became the new standard:**
- Container Apps `az logs show --tail 300` rolls out after a few minutes — **always use Log Analytics KQL** for incidents
- Every domain exception needs the rule code or resource name in the log message
- Webhook 500-vs-422 framework noise (Hellang ProblemDetails remap) is a recurring diagnostic trap — A0.1 fixes it

### 4.3 Exception handling

**Standard hierarchy** (in `VrBook.Domain.Common`):
- `BusinessRuleViolationException(rule, message)` → 422
- `NotFoundException(resource, id)` → 404
- `ForbiddenException(message)` → 403
- `ConflictException(message)` → 409
- `ValidationException` (FluentValidation) → 400
- Anything else → 500 with `traceId` in Problem Details

Mapping lives in `src/VrBook.Api/Middleware/ProblemDetailsConfig.cs`. Never bypass it. **Never catch generic `Exception` without rethrowing as a domain exception.**

### 4.4 Schema changes (ADR 0007)

- One `DbContext` per module, one migration per change
- **Expand-then-contract** for any NOT NULL tightening or rename: two releases minimum
- Migrator Container App Job runs before every deploy. API NEVER auto-migrates

### 4.5 Contract changes (ADR 0002)

- Any change to `VrBook.Contracts` or `/contracts/openapi.yaml`:
  1. Label PR `contracts-change`
  2. Entry in `/contracts/CHANGELOG.md`
  3. Atomic: same PR updates OpenAPI + TS client + handlers + tests
- Spectral lint clean; oasdiff no breaking changes

---

## 5. Definition-of-done (every A-number)

An A-number is NOT complete until ALL of:

1. **Failing tests written first** (TDD)
2. **Unit tests** ≥70% coverage on the new domain code (≥80% on Booking + Pricing)
3. **Integration tests** via Testcontainers (Postgres + Redis) covering happy + ≥3 failure modes per endpoint
4. **Smoke test** added to `cd-staging-api.yml` for the new public endpoint(s)
5. **Structured logging** in every new handler (`userId`, `traceId`, `correlationId` minimum)
6. **Exception handling** uses the standard hierarchy mapped via ProblemDetailsConfig
7. **Observability**: new worker → new alert rule + runbook stub; new failure mode → saved KQL query
8. **Contract atomicity**: OpenAPI + TS client + handlers + tests in one PR with CHANGELOG entry
9. **Deploy verified live** on staging
10. **Update this file** to mark the A-number ✅

---

## 6. Execution sequence (THE order I will work in)

This is the order. I do not skip ahead. If a new request from the user doesn't fit the current A-number, I ASK whether to:
- Park it as `POLISH.N`
- Bump the current A-number
- Re-prioritize (which means editing this file first)

| Order | Tag | Title | Why this slot |
|---|---|---|---|
| 1 | **A0.1** | Observability + exception handling hardening | Every downstream debug session depends on this |
| 2 | **A0.2** | Test debt paydown (TDD retroactive) + CI gate | Confident delivery requires this before more features |
| 3 | **A2.2** | Amenity catalog expansion (~70 items) + admin CRUD | User-requested; small scope to practice 0A/0B discipline |
| 4 | **A0.3** | Event bus wiring (in-process MediatR + outbox table; no Service Bus yet) | A6/A7/A9 all need this; without it `BookingConfirmed` is silently dropped. Service Bus relay deferred to A11. |
| 5 | **A6** | Sync (AirBnB iCal) — zero double-bookings | The single most load-bearing safety constraint |
| 6 | **A7** | Messaging + SignalR | First REAL event consumer (`BookingConfirmed` → auto-thread); proves the bus works |
| 7 | **A8.1** | Loyalty + Reviews moderation polish | Plug into A3 (real discount), unlock retention |
| 8 | **A9** | Notifications + Templates (ACS email) | Final consumer; wires up emails for every event A4 emits |
| 9 | **A3.1** | Full 5-rule pricing engine | After A9 so dynamic-pricing emails are also covered |
| 10 | **A4.1** | Hold flow (Redis NX) + Owner SLA queue | Pre-launch polish; requires all consumers live |
| 11 | **O1** | Admin dashboard polish (reports, conflict modal, toggles, alerts banner) | Requires data from A6/A7/A8.1/A9 to populate |
| 12 | **A10** | Functional completeness sweep | Final pass — every endpoint has UI, every event has a consumer, every feature row in proposal is ticked |
| 13 | **A11** | Observability + exception handling completeness audit + outbox→ServiceBus relay | Re-walk every handler shipped since A0.1; verify alerts/runbooks/KQL still match; wire the relay deferred from A0.3 |
| 14 | **OPS** | Operational hardening (see §8) | Pre-launch readiness |

Subtasks of each item are detailed in §7.

---

## 7. A-number breakdowns

Each item below ends in the §5 DoD. Subtask numbering is `A0.1.1`, `A0.1.2`, etc. — no parallel scheme.

### A0.3 — Event Bus Wiring (in-process MediatR + outbox table)

Closes the gap surfaced 2026-06-09: `IDomainEventPublisher` was declared in
Contracts but never implemented; `DequeueEvents()` was never called by any
caller — so every domain event raised so far has been silently discarded.

- **A0.3.1** Failing unit tests first: publisher publishes single event, publisher publishes batch, EF interceptor drains events from changed aggregates, outbox row written in same txn
- **A0.3.2** `MediatRDomainEventPublisher` implementing `IDomainEventPublisher` — delegates to `IPublisher.Publish` so any registered `INotificationHandler<TEvent>` fires
- **A0.3.3** `DomainEventOutboxInterceptor` (EF `SaveChangesInterceptor`) — on SavingChanges scans `ChangeTracker` for `IAggregateRoot` entries, calls `DequeueEvents()`, persists each to the outbox table, holds them; on SavedChanges calls `IDomainEventPublisher.PublishAsync` to fire in-process handlers
- **A0.3.4** `outbox_messages` table per module schema (Catalog/Booking/Payment/Reviews/Identity) — columns: id, event_id, event_type, payload (jsonb), occurred_at, dispatched_at (nullable), retry_count, last_error. Migration per module.
- **A0.3.5** DI registration in `IModuleRegistration.AddModuleServices` — register the interceptor on each module's DbContext + register publisher singleton
- **A0.3.6** Integration test: raise `BookingPlaced`, assert outbox row exists with payload, assert MediatR notification handler observed it
- **A0.3.7** Deploy + verify staging — make a real booking, query `booking.outbox_messages` for the `BookingPlaced` row
- **A0.3.8** Smoke updated — assert outbox row count increments after a booking POST

Service Bus relay (outbox → topic) is **NOT** in A0.3 scope; that lands in A11 once consumers exist and we know which topics they need.

### A0.1 — Observability + Exception Handling Hardening

- **A0.1.1** Audit Bicep — confirm Log Analytics workspace + retention (30 days minimum); confirm Container Apps consoleLogs + systemLogs routed
- **A0.1.2** Audit every existing handler for `ILogger<T>` + structured properties (`userId`, `traceId`, `bookingId`, `propertyId`, `correlationId`)
- **A0.1.3** Implement / verify Serilog destructuring policy for PII redaction (email, phone, displayName, address, cardLast4)
- **A0.1.4** Fix Hellang ProblemDetails framework 500-vs-422 log noise (Serilog enricher suppresses the misleading framework status log on remap)
- **A0.1.5** Save KQL queries in `docs/observability/queries.kql`: API 5xx by endpoint, slow handlers >1s, webhook delivery, booking funnel, payment failures
- **A0.1.6** Workbook in Bicep with sections: Errors, Booking Funnel, Payments, Sync Health (placeholder for A6), Messaging (placeholder for A7), Infrastructure
- **A0.1.7** Deploy 8 alert rules per ADR 0010: 5xx spike, P95 >1s, postgres CPU, redis evictions, webhook fail burst, SLA worker silent, sync feed stale (A6 placeholder), template render failure (A9 placeholder)
- **A0.1.8** Runbook completeness audit — flesh out stubs for live systems (payment-webhook-failure, api-5xx-spike, postgres-cpu-high, redis-evictions, stripe-dispute-opened)

### A0.2 — Test Debt Paydown + CI Gate

- **A0.2.1** Fix the 8 failing `IdentityFlowTests` (root cause: Entra OIDC discovery doc fetch in CI — WireMock fixture)
- **A0.2.2** Backfill unit tests for shipped domain code (TDD retroactive):
  - A2 Property aggregate + search filters
  - A3 PricingPlan + ComputeQuote (weekend, fees)
  - A4 Booking aggregate (every state transition + guard, plus §18.2 concurrent booking test)
  - A5 PaymentIntent + Refund + webhook signature + idempotency
  - A8 Review aggregate + submission rules
- **A0.2.3** Backfill integration tests for shipped contexts — first 3 of 7 §18.2 mandated flows:
  - #1 End-to-end booking (search → book → webhook → confirm)
  - #5 Concurrent booking → only one wins
  - #6 Stripe webhook idempotency
- **A0.2.4** CI gate hardening: change `cd-staging-api.yml` integration-tests step from `continue-on-error: true` to **blocking**
- **A0.2.5** Architecture tests in NetArchTest project (currently empty): Clean Architecture boundary rules, modular-monolith no-cross-schema-reference rule

### A1.1 — Identity IT Fix (folded into A0.2.1)

Same work as A0.2.1; tracked there to avoid duplication.

### A2.2 — Amenity Catalog Expansion (user-requested)

- **A2.2.1** Failing unit tests for amenity CRUD + enable/disable
- **A2.2.2** Add `is_active` column to `catalog.amenities` (expand: nullable default true, per ADR 0007)
- **A2.2.3** Seed migration: expand catalog from 25 → ~70 amenities across 12 industry-standard categories
- **A2.2.4** Admin endpoints: `POST /admin/amenities`, `PUT /admin/amenities/{id}`, `POST /admin/amenities/{id}/enable`, `POST /admin/amenities/{id}/disable`
- **A2.2.5** Integration tests for admin CRUD (auth: admin role required)
- **A2.2.6** Public `GET /amenities` excludes `is_active=false`
- **A2.2.7** Admin UI: `/admin/amenities` page (F1 contribution)
- **A2.2.8** Owner UI: edit-property amenity checkboxes (F1 contribution)
- **A2.2.9** Smoke + observability + deploy

### A6 — Sync (AirBnB iCal)

- **A6.1** Failing integration test §18.2 #3: iCal conflict → SyncConflictDetected → owner resolves → booking transitions
- **A6.2** `IExternalChannel` interface in Contracts + `AirBnBICalChannel` implementation (Ical.Net 4.x)
- **A6.3** `sync` schema: `channel_feeds`, `external_reservations`, `sync_conflicts`; migration
- **A6.4** Sync Worker Container App Job: cron `*/5 * * * *`, per-feed cadence honouring ETag/304
- **A6.5** Inbound: UPSERT external_reservations by `(feed_id, ical_uid)`, cancellation by absence
- **A6.6** Conflict detection: overlap query → `sync_conflicts` row + `SyncConflictDetected` publish
- **A6.7** Outbound feed `/feeds/{outboundToken}.ics` with Redis 10-min cache + SHA-256 ETag (include Tentative as `STATUS:TENTATIVE`)
- **A6.8** Owner resolution: `POST /admin/sync-conflicts/{id}/resolve` with action enum (`keep_direct`, `cancel_direct`, `manual_override`)
- **A6.9** Implement `IExternalChannelConflictChecker` (A4 stub → real)
- **A6.10** WireMock.Net fixture with canned ICS payloads (happy, malformed, BOM, cancelled, recurring)
- **A6.11** Frontend O1: `/admin/sync` page with conflict resolution modal (F1 contribution)
- **A6.12** Observability: `sync.poll.duration_ms`, `sync.events.imported`, `sync.conflicts.detected`, alert at 3 consecutive feed failures
- **A6.13** Runbooks: `sync-feed-stale`, `sync-conflict-detected`

### A7 — Messaging & SignalR

- **A7.1** Failing integration tests for thread + message endpoints
- **A7.2** `Thread`, `Message`, `Attachment` aggregates
- **A7.3** `messaging` schema; migration
- **A7.4** Auto-thread on `BookingConfirmed` — FIRST real event consumer
- **A7.5** REST: messages, read receipts, attachments (Blob 10MB cap + content-type allowlist + SAS download)
- **A7.6** `/realtime/negotiate` → SignalR access token (Serverless mode)
- **A7.7** Offline detection + Service Bus publish to A9 for email fallback
- **A7.8** Frontend F1: messaging UI on `/account/messages`
- **A7.9** Observability: `messaging.connections.active`, `messaging.offline.fallback.published`
- **A7.10** Manual two-browser WebSocket test plan documented

### A8.1 — Loyalty + Reviews Moderation Polish

- **A8.1.1** Failing integration test §18.2 #7: Silver-tier user → next quote shows 5% discount
- **A8.1.2** `LoyaltyAccount` aggregate
- **A8.1.3** `loyalty` schema + `tier_definitions` seed (Bronze 1/0%, Silver 3/5%, Gold 6/10%); migration
- **A8.1.4** Event consumer: `BookingCompleted` → increment `completed_stay_count` + recompute tier
- **A8.1.5** Implement `ILoyaltyDiscountResolver` (A3 stub → real)
- **A8.1.6** Booking-time snapshot (`loyalty_tier_at_booking`, `loyalty_discount_pct`)
- **A8.1.7** Toggles: global `loyalty_enabled` + per-property override
- **A8.1.8** A8 Reviews polish: owner response endpoint, moderation queue + admin endpoints
- **A8.1.9** Frontend F1: tier badge on `/account`, loyalty toggle in admin

### A9 — Notifications & Templates

- **A9.1** Failing integration test: `BookingPlaced` event → email queued in `notification_log`
- **A9.2** Notification Worker (Service Bus consumer, KEDA scale-to-zero)
- **A9.3** ACS email integration (per ADR 0011, NOT SendGrid)
- **A9.4** 18 Mustache templates with shared layout, MSO conditional comments, plain-text fallback
- **A9.5** Per-template variable schema validation at render time
- **A9.6** `notification_log` + retry policy (3× exponential backoff → dead-letter)
- **A9.7** CI step: render every template with sample variables, fail on Mustache errors
- **A9.8** Frontend F1: unsubscribe page + preference center
- **A9.9** Observability: `notifications.sent`, `notifications.failed`, `notifications.template_render_errors`
- **A9.10** Runbook: `notification-template-render-failure`

### A3.1 — Full 5-rule pricing engine

- **A3.1.1** Failing unit tests for each rule kind: DateRangeOverride, LastMinute, LengthOfStay, DayOfWeek
- **A3.1.2** Failing unit tests for priority resolution per §11.2 (override wins, last-minute trumps base, etc.)
- **A3.1.3** Implement the rule engine
- **A3.1.4** Owner CRUD endpoints for pricing rules
- **A3.1.5** Integration test §18.2 #4: cancellation policy refunds (3 policies × 4 timing buckets)
- **A3.1.6** Frontend F1: rules editor on owner property page

### A4.1 — Hold flow + Owner SLA queue

- **A4.1.1** Failing integration test §18.2 #2: SLA auto-confirm
- **A4.1.2** Redis NX hold during checkout (15-min default, owner-configurable)
- **A4.1.3** `/admin/bookings/queue` Tentative-awaiting-action projection
- **A4.1.4** SLA worker (cron sweep `tentative_until <= now`) → auto-confirm if no sync conflicts
- **A4.1.5** Frontend F1: owner queue page with confirm/reject actions

### O1 — Admin Dashboard polish

The cross-cutting admin sweep AFTER A6/A7/A8.1/A9/A3.1/A4.1 ship their own
per-feature admin screens. Each individual feature ships its admin view
inside its own A-number; O1 ties them together and fills the gaps that
don't belong to any single feature vertical.

- **O1.1** Admin Dashboard (`/admin`) — landing page with KPI tiles fed from O1.2 reports
- **O1.2** Reports (`/admin/reports`) — occupancy, revenue, ADR, source — projection tables via event consumers
- **O1.3** Properties admin (`/admin/properties`) — owner CRUD list + create/edit drawer + image upload (currently stub: web/src/app/admin/properties/page.tsx)
- **O1.4** Bookings admin (`/admin/bookings`) — queue with filter + manual entry + detail tabs (Overview/Payments/Messages/Timeline) (currently stub: web/src/app/admin/bookings/page.tsx)
- **O1.5** Calendar (`/admin/calendar`) — multi-property month view + drag-to-block
- **O1.6** Guests (`/admin/guests`) — guest list + booking history per guest
- **O1.7** Feature-toggle CRUD UI (`/admin/settings`)
- **O1.8** `/admin/alerts` endpoint + alerts banner

### A10 — Functional Completeness Sweep

Audit pass over the entire system AFTER O1. Walks every proposal feature
row, every OpenAPI endpoint, every domain event, and confirms a working
end-to-end surface exists. Generates `POLISH.N` items for gaps that
should slip; closes A10 only when nothing material remains.

- **A10.1** Walk `contracts/openapi.yaml` — every endpoint has a guest, owner, or admin UI surface that exercises it
- **A10.2** Walk every domain event raised across modules — confirm at least one consumer wired through Service Bus (or A11-pending if relay hasn't shipped)
- **A10.3** Walk every proposal §X feature row — tick implemented / tested / smoked / no gap
- **A10.4** Manual smoke matrix against deployed staging — visit every screen, run every flow, record red/green
- **A10.5** File POLISH.N for any cosmetic / non-critical gaps; close A10 when no material gaps remain

### A11 — Observability + Exception Handling Completeness Audit + Outbox Relay

Re-walks A0.1 + A0.3 conclusions against everything shipped between then
and now. Closes the Service Bus piece that A0.3 deferred.

- **A11.1** Audit every handler shipped since A0.1 — has `ILogger<T>` with `userId`, `traceId`, business key
- **A11.2** Confirm every handler throws from `DomainException` hierarchy; verify ProblemDetailsConfig still maps correctly
- **A11.3** New failure modes since A0.1 (sync conflict, message delivery failure, template render failure, hold expiry) — each has an alert rule + saved KQL query + runbook
- **A11.4** Re-verify `PiiRedactingEnricher` covers fields added since A0.1 (message bodies, review bodies, sync feed URLs)
- **A11.5** Re-verify 10 saved KQL queries still match deployed schema
- **A11.6** Wire **outbox → Service Bus relay** deferred from A0.3 — background worker reads `outbox_messages` where `dispatched_at IS NULL`, publishes to topic per event type, marks dispatched, retries with backoff on failure
- **A11.7** Cross-module event consumers (if any landed via direct MediatR INotificationHandler in A6-A9) — migrate to Service Bus subscription where appropriate (in-process is fine when consumer is in same process; cross-process needs the relay)

### F1.1 — Playwright E2E suite (cross-cutting frontend deliverable)

Scheduled inside `OPS` since the suite spans every prior agent's frontend work.

---

## 8. OPS — Operational Hardening (pre-launch)

These are not agent-context items per §20.2; they're operational gates the proposal calls out (§14, §18, §22). Tracked here so they don't get lost.

- **OPS.1** Pact contract tests (FE ↔ API) — at least the 7 §18.2 mandated flows
- **OPS.2** Playwright E2E suite (~30 scenarios) — F1.1 deliverable
- **OPS.3** k6 load test: Booking flow at 50 RPS sustained 5 min, P95 < 1s
- **OPS.4** OWASP ZAP baseline scan in CI
- **OPS.5** Trivy + SBOM signing in image build (currently deferred to "prod-CD" that doesn't exist)
- **OPS.6** Stripe key rotation (sandbox keys leaked in chat — overdue)
- **OPS.7** Entra External ID prod app registrations + cutover
- **OPS.8** Custom domain DKIM verification for ACS email

---

## 9. POLISH — Backlog (interleave when convenient)

These don't block the agent sequence. Pull one in when context allows.

- **POLISH.1** `BookingDto` add `CheckedInAt` + `CheckedOutAt` → Timeline UI bug fix
- **POLISH.2** `/health/ready` Degraded — currently failing because Redis disabled
- **POLISH.3** Image upload endpoint (deferred from A2 v1)
- **POLISH.4** Frontend amenity filter chips
- **POLISH.5** Property images gallery on listing detail

---

## 10. Observability cheat sheet

### Where to look first

| Symptom | First place to look |
|---|---|
| 5xx in browser console | Log Analytics: `AppRequests \| where Url contains "..." and Success == false` |
| Webhook 4xx/5xx | Stripe Dashboard → destination → Event deliveries; then Log Analytics for our endpoint trace |
| Slow handler | Log Analytics: `AppRequests \| where DurationMs > 1000 \| project TimeGenerated, Url, DurationMs, OperationId` |
| Booking stuck Tentative | DB: `SELECT * FROM booking.bookings WHERE status='Tentative' AND tentative_until < now()` + Log Analytics for SLA worker activity |
| Sync conflict missing | DB: `SELECT * FROM sync.sync_conflicts` + Log Analytics: `traces \| where Message contains "SyncConflictDetected"` |

### Don't use `az containerapp logs show` for incidents

The CLI tail is bounded — logs roll out within minutes. Always use **Log Analytics KQL** for retrospective investigation. KQL queries live in `docs/observability/queries.kql`.

### Required structured properties on every log line

`userId`, `traceId`, `correlationId`, `bookingId`, `propertyId`, `requestName`. Set via MediatR pipeline `LogContext.PushProperty`.

---

## 11. When you (Claude) come back to this

If you're picking up a new session and feel lost:
1. **Read this file** end-to-end
2. **Read** `docs/observability/queries.kql` (if it exists)
3. **Run** `git log --oneline -20` — see recent commits
4. **Check** TodoWrite contents against §6 here — they should mirror the execution sequence
5. **Identify the active A-number** from TodoWrite `in_progress` items
6. **Stay anchored to that A-number.** Do not jump ahead. If the user asks for something outside it, ASK whether to park as POLISH, bump the agent, or re-prioritize (which means editing this file first)

**Persistence over memory.**
