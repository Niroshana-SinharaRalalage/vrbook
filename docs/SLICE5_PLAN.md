# Slice 5 — Stay completes → review + loyalty (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-22.
**REPLAN section**: `docs/REPLAN.md` §3 Slice 5.
**Sequence**: `docs/MASTER_PLAN.md` §2 row 2 (after Slice 4, before Slice 6).

This plan supersedes informal Slice 5 notes. It is the contract.

---

## 1. What is already shipped (do not re-architect)

**Reviews module** (Slice 0/1 era):
- `Review` aggregate at `src/Modules/VrBook.Modules.Reviews/Domain/Review.cs`. Fields: `BookingId, PropertyId, GuestUserId, GuestDisplayName, Rating, Body, Status, PublishedAt, ResponseBody, ResponseAt`. Submits are auto-`Approved`; `AddOwnerResponse` is one-shot.
- `SubmitReviewCommand` + handler. **Currently throws `BusinessRuleViolationException` on duplicate submit** — Slice 5 softens this (see §2.4).
- `ListReviewsForAdminQuery`, moderation `Hide`/`Restore`/`Reject` commands + handlers.
- `ReviewsController` at `[Route("api/v1/admin/reviews")]` ships moderation endpoints + per-property public review list.

**Loyalty module** (A8.1 era):
- `LoyaltyAccount` aggregate. `RecordCompletedStay()` increments `CompletedStayCount`, resolves new tier via `TierDefinition.Resolve(...)`. Thresholds: Bronze 1+/0%, Silver 3+/5%, Gold 6+/10%.
- `TierDefinition.NextTier(...)` returns next tier + stays remaining (for UI progress).
- `OnBookingCompletedHandler` — `INotificationHandler<BookingCompleted>` opens a new account on first completion. **Currently logs the tier upgrade but does not raise a domain event** — Slice 5 changes this.
- `GetMyLoyaltyQuery`.

**Booking module**:
- `Booking.CheckOut()` currently raises `BookingCompleted` directly. Slice 5 moves that to a new `Booking.Complete()` method.
- **No `BookingCompletionWorker` exists** — `CheckedOut` bookings sit forever.
- Existing `BookingExpiryWorker` (Slice 0.4, cron `*/10`) handles Tentative-window expiry. Container App Job, image `vrbook-booking-worker`.

**Notifications module** (Slice 4):
- `NotificationKind.BookingCompleted = 6` exists, currently mapped to the `booking.confirmed` template (Slice 4 placeholder).
- 10 enriched templates + dispatch backbone (ACS, `NotBeforeUtc` deferred sends, retries) verified on staging.
- `BookingNotificationHandlers.Handle(BookingCompleted)` enqueues a "Thanks for staying" row.

**Frontend**:
- `/admin/reviews/page.tsx` is a placeholder.
- No `/account/bookings/[id]/review` page.
- No `/account/loyalty` page.
- `AdminSidebar` already lists Reviews.

## 2. Decisions (architect-locked)

### 2.1 Worker scope — **new `BookingCompletionWorker` Container App Job, cron `0 6 * * *`**

The existing `BookingExpiryWorker` is `*/10` over Tentative; a daily completion sweep over CheckedOut has a wholly different cadence, predicate, and failure radius. Co-tenanting would force the expiry sweep to make 140 unnecessary daily DB reads against the `CheckedOut` set just to no-op, and the failure mode of the new sweep would page on a 10-min schedule rather than once a day. Two jobs is the existing pattern (expiry + notifications + sync are already three).

**Implementation**: same booking-worker image, mode selected via `--mode=expiry|completion` arg. One Docker image, two Container Apps Jobs, two cron schedules.

### 2.2 Tier promotion event — **new `LoyaltyTierPromoted` contract event**

`OnBookingCompletedHandler` already does the tier comparison and currently logs it; promoting that log line to `Raise()` on the aggregate is a ~4-line change. The alternative — Notifications subscribing to `BookingCompleted` and re-querying Loyalty — forces a cross-module DbContext access on every BookingCompleted, violating the same module-boundary rule the Slice 4 `IUserEmailLookup` port was created to honour.

**Shape**: `LoyaltyTierPromoted(Guid UserId, LoyaltyTier OldTier, LoyaltyTier NewTier, int CompletedStayCount, decimal NewDiscountPct)` in `VrBook.Contracts.Events`.

### 2.3 `review.request` deferred T+1 — **reuse `NotBeforeUtc` from Slice 4**

The column exists, the worker query already filters on it, and the deferred-reminder pattern was specifically generalised in Slice 4 §2.3 for exactly this. A separate "daily cron over Completed bookings" would duplicate the dispatch worker's drain logic, gain nothing the lease pattern doesn't already give, and split the moment-of-truth send path across two cron schedules.

`BookingNotificationHandlers.Handle(BookingCompleted)` enqueues two rows: the existing `BookingCompleted` row (immediate), and a new `ReviewRequest` row with `NotBeforeUtc = UtcNow + 24h`.

### 2.4 Review submission idempotency — **handler returns existing review (200), unique index defends races**

The unique index on `(BookingId)` in `ReviewConfiguration` is the backstop. `SubmitReviewHandler` currently throws on the dupe path; soften that branch to return the existing `ReviewDto`. Satisfies "click email twice → land on a happy page"; the FE `/account/bookings/[id]/review` stays stateless; the unique index still defends against concurrent races.

### 2.5 Email deep link — **bare URL, auth-gated page**

DevAuth in staging and Entra in prod both gate `/account/bookings/...`. A signed HMAC token would add a key-rotation surface (no `OPS.6` yet) for a flow whose worst-case abuse is "a stranger sees a 'sign in to review' page". Bare URL. Phase 2 can revisit if public review-by-email lands.

### 2.6 `Booking.Complete()` semantics

Add `Booking.Complete()`: guards `Status == CheckedOut`, transitions to `Completed`, raises `BookingCompleted`. **Move the `Raise(new BookingCompleted(...))` call out of `CheckOut()`** so the daily sweep is the only trigger. Today nothing in production calls `CheckOut()` automatically — behavioural change is invisible to existing flows.

---

## 3. Commit split (5 commits)

### C1 — Domain: `Booking.Complete()` + `LoyaltyTierPromoted` contract event

- `Booking.Complete()` added; `Booking.CheckOut()` no longer raises `BookingCompleted`.
- `LoyaltyTierPromoted` event in `VrBook.Contracts.Events`.
- `LoyaltyAccount.RecordCompletedStay()` returns promotion info; `OnBookingCompletedHandler` raises `LoyaltyTierPromoted` when tier shifts.
- Unit tests cover both transitions. No schema. No UI.

### C2 — Worker: `CompletionSweepCommand` + Bicep + CI step

- `CompletionSweepCommand` + handler in `VrBook.Modules.Booking.Application.Commands`. Predicate: `Status == CheckedOut AND CheckedOutAt <= NOW() - 1 day`. Per booking: `booking.Complete()` → `SaveChangesAsync()` → MediatR dispatches `BookingCompleted` to Loyalty + Notifications handlers in-process. Idempotent (CheckedOut → Completed only).
- `Workers.Booking/Program.cs` adds `--mode=expiry|completion` arg (default `expiry`). One image, two jobs.
- `infra/main.bicep`: new `bookingCompletionJob` module mirroring `bookingExpiryJob` (lines 398-418), `cronExpression: '0 6 * * *'`, same `bookingWorkerImage`, `args: ['--mode=completion']`.
- `.github/workflows/cd-staging-api.yml`: new `deploy-booking-completion-job` step mirroring `deploy-booking-expiry-job` (line 375), and `deploy-api`'s `needs:` (line 395) updated.
- Acceptance 1 met after this commit.

### C3 — Templates + NotificationKind wiring + deferred review.request

- Three new Mustache files under `src/Modules/VrBook.Modules.Notifications/Templates/`:
  - `booking.completed.mustache` (replaces Slice 4 placeholder pointing at `booking.confirmed`).
  - `review.request.mustache`.
  - `loyalty.tier_promotion.mustache`.
- Three new sample fixtures under `Templates/Samples/`.
- `NotificationKind.ReviewRequest = 7`, `NotificationKind.LoyaltyTierPromotion = 21` added.
- `MustacheTemplateRenderer.TemplateNameFor` map updated.
- `BookingNotificationHandlers.Handle(BookingCompleted)` adds the second `Queue()` call with `NotBeforeUtc = UtcNow + 24h` for `ReviewRequest`.
- New `LoyaltyNotificationHandlers : INotificationHandler<LoyaltyTierPromoted>` queues `LoyaltyTierPromotion` rows.
- `NotificationPayload` extras gain `DeepLink` (computed from `IConfiguration["App:WebBaseUrl"]` + `/account/bookings/{BookingId}/review`).
- CI render test (`NotificationTemplatesRenderTests`) auto-discovers new templates against samples.

### C4 — Guest UI: `/account/bookings/[id]/review` + `/account/loyalty`

- New page `/account/bookings/[id]/review/page.tsx` — star picker (1-5), body textarea, submit. Hits existing `POST /api/v1/bookings/{id}/review`. Soften submit-when-exists per §2.4.
- New page `/account/loyalty/page.tsx` — tier badge (Bronze/Silver/Gold), `CompletedStayCount`, "X stays until Silver/Gold" progress, current discount %. Hits existing `GET /api/v1/me/loyalty`.
- `SubmitReviewHandler` softens "already reviewed" branch to return existing DTO with 200 status.
- `web/src/lib/api/reviews.ts`, `web/src/lib/api/loyalty.ts` added (or extended).
- Acceptance 2 + 5 met after this commit.

### C5 — Admin UI: replace `/admin/reviews` placeholder

- Real list via `ListReviewsForAdminQuery`. Status filter chips (All / Approved / Hidden / Rejected). Each row: stars, guest, property, body excerpt, status pill.
- Response box per row, disabled when `ResponseBody is not null` (one-shot enforcement is in the aggregate; UI surfaces it).
- Hide / Restore / Reject buttons calling existing `/api/v1/admin/reviews/{id}/...` endpoints.
- Acceptance 3 + 4 met after this commit.

---

## 4. Concrete file additions

### Contracts
- `src/VrBook.Contracts/Events/LoyaltyEvents.cs` — `LoyaltyTierPromoted` record.

### Booking module
- `Domain/Booking.cs` — edit: add `Complete()`, move `BookingCompleted` raise.
- `Application/Commands/CompletionSweepCommand.cs` — new (+ handler).

### Loyalty module
- `Domain/LoyaltyAccount.cs` — edit: `RecordCompletedStay` returns promotion info.
- `Application/Accounts/Handlers/OnBookingCompletedHandler.cs` — edit: raise event on tier change.

### Notifications module
- `Domain/NotificationLog.cs` — edit: add `ReviewRequest`, `LoyaltyTierPromotion` enum values.
- `Application/Handlers/BookingNotificationHandlers.cs` — edit: second queue call in `Handle(BookingCompleted)`.
- `Application/Handlers/LoyaltyNotificationHandlers.cs` — new.
- `Application/Handlers/NotificationPayload.cs` — edit: support `DeepLink` extra.
- `Application/Dispatch/MustacheTemplateRenderer.cs` — edit: `TemplateNameFor` map.
- `Templates/booking.completed.mustache`, `Templates/review.request.mustache`, `Templates/loyalty.tier_promotion.mustache` — new.
- `Templates/Samples/booking.completed.json`, `review.request.json`, `loyalty.tier_promotion.json` — new.

### Reviews module
- `Application/Commands/SubmitReviewHandler.cs` — edit: existing-review branch returns 200.

### Worker
- `src/Workers/VrBook.Workers.Booking/Program.cs` — edit: mode-switch arg, dispatch `CompletionSweepCommand` when `--mode=completion`.

### Web
- `web/src/app/account/bookings/[id]/review/page.tsx` — new.
- `web/src/app/account/loyalty/page.tsx` — new.
- `web/src/app/admin/reviews/page.tsx` — replace placeholder.
- `web/src/lib/api/reviews.ts`, `web/src/lib/api/loyalty.ts` — new.

### Tests
- `tests/VrBook.Api.IntegrationTests/Domain/BookingCompleteTests.cs` — new (`Booking.Complete()` invariants).
- `tests/VrBook.Api.IntegrationTests/Domain/LoyaltyTierPromotionTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/CompletionSweepHandlerTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/ReviewSubmissionIdempotencyTests.cs` — new.

### Infra + CI
- `infra/main.bicep` — new `bookingCompletionJob` module block.
- `.github/workflows/cd-staging-api.yml` — new `deploy-booking-completion-job` step + `deploy-api` `needs:` update.

### Runbook
- `docs/runbooks/booking-completion-sweep.md` — what to check if completion stalls.

---

## 5. Scope-cut order (drop top first when deadline bites)

Never falls: C1, C2, C3-templates `booking.completed` + `review.request`, C4 `/account/bookings/[id]/review`, `SubmitReviewHandler` soften. Those carry acceptance 1 + 2.

1. **Loyalty tier promotion email** (`loyalty.tier_promotion` template + `LoyaltyNotificationHandlers` + the `LoyaltyTierPromoted` event raise). Tier shows on `/account/loyalty` either way; the email is nice-to-have. Cutting removes 3 of C3's units and all of C1's contract-event work.
2. **`/account/loyalty` page** (C4-part-2). The endpoint exists; ship without the page; demoable via API.
3. **`/admin/reviews` replacement** (C5 entirely). Keep placeholder; owner can curl `/api/v1/admin/reviews` or use existing endpoints. Loses acceptance 3 + 4's UI proof but aggregate enforcement still holds.
4. **`review.request` deferral fidelity** — if `NotBeforeUtc` plumbing breaks, ship immediate-send (T+0 instead of T+1). Guest gets the review email at completion-sweep tick rather than 24h later. Acceptance 2 still passes (the link works); cosmetic.

If 1-4 all fall, the slice still delivers: completed bookings get a working review form via a same-day email link. That's the minimum that lets us claim "the post-stay loop closes."

---

## 6. Out of scope (Phase 1.5+ / OPS)

- **Loyalty discount applied at quote-compute time.** The `Discount` field comment in `Booking.cs:30` already calls this out ("Wired in A4.1 + A8"). Slice 5 ships the read side (`/account/loyalty` shows discount %); applying it at the next quote computation lands in OPS.M.
- **Public review-by-email signed-link flow.** Phase 2.
- **Review attachment uploads / photos.** Phase 2.
- **Tier downgrade.** Phase 2 (currently monotonic; resetting after 365 days of no stay is a real-world pattern not in Phase 1).
- **Owner reply moderation.** Owner replies are also auto-approved in Phase 1.

---

## 7. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/SLICE5_PLAN.md`.
2. I open C1 — `Booking.Complete()` + `LoyaltyTierPromoted` event.
3. Each commit ends with: I push, CI runs, I report green/red. Slice ends with: I seed a CheckedOut booking on staging, manually trigger the completion sweep job, watch the guest review email arrive at `niroshanaks@gmail.com`, click the link, submit a review, see it on the property page, see the owner respond, see the loyalty tier increment on `/account/loyalty`.

If you reject or want changes: point at the specific decision in §2 or the specific commit in §3; I revise this document and re-submit.
