# VrBook Phase 1 Replan — 2026-06-10

**Status**: Proposed. Awaiting user review.
**Reason for replan**: Backend-first sequencing produced a system with rich domain code and zero end-to-end usability. Owner cannot today create a property, see a tentative booking, or confirm one through the browser. Continuing backend depth on this foundation compounds risk because every backend claim is unverified by a real user path.

This document was produced after:
1. Reading `BookingApp_Proposal.md` §§7–14, §18, §22.
2. Reading `docs/adr/0011-azure-communication-services-email.md`.
3. Auditing every controller + every Next.js page for stubs.
4. Consulting system-architect with the full picture (race windows, sequencing trade-offs, hidden bugs).

The previous plan (`docs/EXECUTION_PLAN.md` A0.1 → A11 → OPS) is **superseded** by this document. The A-number nomenclature stays as a tag inside slices but is no longer the sequencing primitive.

---

## 1. What the audit uncovered

### 1.1 Critical backend bugs (must fix before any usable slice ships)

| File | Issue | Severity |
|---|---|---|
| `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs:53-54` | Documented race: "Race window vs SaveChangesAsync is acceptable for v1 - A5 will harden via a transactional pre-charge guard." Two guests can book the same dates and only one's `SaveChanges` rolls back at the unique constraint, but by then both payments may have been authorized. | **HIGH** — direct double-book risk |
| `src/VrBook.Contracts/Interfaces/IHoldStore.cs` | Interface defined; no implementation anywhere. The 15-minute checkout hold flow (§9.3) has no code path. | **HIGH** — concurrent checkouts can both reach `POST /bookings` for same property+dates |
| `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/BookingRepository.cs` | `FindOverlapsAsync` uses `AsNoTracking` — no row lock. The §7.3 `SELECT ... FOR UPDATE` defense is not present. | **HIGH** — same root as the race above |
| `src/Modules/VrBook.Modules.Pricing/Application/Quotes/Commands/ComputeQuoteHandler.cs:81` | "no loyalty discount, no dynamic adjustments. A3.1 work" — the §11.2 5-rule engine is base + weekend + fees only today. | MEDIUM — feature gap, not a safety bug |
| No `*Sla*.cs` files | The §7 SLA worker that auto-confirms after 24h is not implemented. Tentative bookings stay tentative forever. | **HIGH** — funnel doesn't complete unattended |
| No `*Acs*.cs` files, no Bicep for ACS resource | ADR-0011 ACS Email integration is paper-only. Notifications log rows accumulate in `Queued` state with nothing draining them. | MEDIUM — emails are funnel-critical but Phase 1 can demo with notification log |
| No `NotificationWorker*.cs` files | The Notification Worker that consumes the outbox doesn't exist. | MEDIUM — same root as ACS |

### 1.2 UI gap inventory

**Web — real and working:**
- `/` (home), `/properties` (search), `/properties/[slug]` (detail + Book button)
- `/bookings/[id]` (post-checkout)
- `/account/profile`, `/account/bookings`, `/account/messages`
- `/admin/amenities` (shipped today), `/admin/sync` (shipped today)

**Web — stub pages (10 of the 12 admin screens):**
- `/admin` (Dashboard)
- `/admin/properties` — owner can't create or edit a property via UI today
- `/admin/calendar`
- `/admin/bookings` — owner can't see tentative bookings or click Confirm via UI today
- `/admin/pricing`
- `/admin/guests`
- `/admin/messages`
- `/admin/reviews`
- `/admin/reports`
- `/admin/settings`

### 1.3 Implication

Backend without UI is a museum. Until an Owner can drive the funnel end-to-end through the browser, none of the §7–§13 safety properties (race-free booking, conflict resolution, payment auth-hold, notification delivery) are exercised by a realistic path. **The system is unverified.**

---

## 2. Recommendation: U-turn to vertical slices, immediately

After both my analysis and the architect consult, the recommendation is unambiguous.

**Do not** continue backend-first. Specifically: do not finish A3.1 (5-rule pricing engine), A4.1 (SLA worker), A9 (full ACS dispatch), OPS list, or A11 (Service Bus relay) as standalone phases.

**Do** ship vertical slices. Each slice ends with a journey a real user can perform in the deployed browser. Backend deliverables fold into the slice that needs them, scoped to what that slice requires (3 rules not 5, 6 templates not 18, etc.).

**Slice 0 (infrastructure bar) lands first**. It closes the documented race bugs so that the first user-facing slice ships on an honest foundation. ~2 days of backend work, no UI.

---

## 3. The 8-item plan (Slice 0 + 7 slices)

### Slice 0 — "Make the booking lifecycle honest" — infrastructure bar (~2 days, no UI)

**Why first**: every slice below assumes (a) tentative holds actually hold, (b) the booking state machine is race-free, (c) the Tentative → Confirmed/Cancelled transition completes unattended. None of these are currently true.

**Includes**:
1. `RedisHoldStore : IHoldStore` implementation + `booking_holds` mirror table + `POST /bookings/holds` + `DELETE /bookings/holds/{id}` endpoints. 15-minute TTL per §9.3.
2. Wrap `PlaceBookingHandler` in a serializable transaction; replace `FindOverlapsAsync` with a raw SQL overlap query using `FOR UPDATE` row lock. PostgreSQL pattern from §7.3.
3. Switch PaymentIntent `capture_method` default from `automatic` to `manual` (re-read of §9.4 below). Add `CapturePaymentIntentForBookingCommand` invocation inside `ConfirmBookingHandler`. Add `CancelPaymentIntent` inside reject/expire paths.
4. `BookingExpiryWorker` (a Container App Job, cron `*/10 * * * *`) — scans `Status=Tentative AND tentative_until <= NOW()`, per booking calls a sync re-poll of related feeds, then either confirms (no conflict found) or cancels with reason `sla_expired`. Tentative window default: **6 hours**, config-overridable.
5. ACS Email Bicep resource + connection string in Key Vault. Just provision; actual `IEmailSender` impl ships in Slice 4.
6. Calendar aggregator query: `GET /properties/{id}/calendar?from=&to=` returning a single DTO that merges direct bookings + external reservations + blocks. Single query handler; no new schema.

**Acceptance**:
- Two parallel curl POSTs to `POST /bookings/holds` for same property+dates → exactly one succeeds, the other returns 409.
- Two parallel POSTs to `POST /bookings` with same hold → one succeeds, one returns 409.
- A Tentative booking older than 6h auto-resolves on the next worker cron tick.
- Cancelling a Tentative booking voids the PaymentIntent (Stripe dashboard shows `canceled`, not `succeeded → refunded`).

**Risk**: switching capture method may affect existing payment tests — they'll need updates. Not user-visible.

---

### Slice 1 — Owner onboards a property (~3 days)

**Persona**: Owner (Dev Owner via DevAuth).
**Why first user-facing**: no property = no booking = no demo. Currently a fresh tenant has zero options for creating a property through the UI; this is the single biggest blocker.

**Journey**:
1. Owner signs in → lands on `/admin` (Dashboard shows empty state, "Add your first property" CTA).
2. Click → `/admin/properties` (empty state, "Add property" button).
3. Click → `/admin/properties/new` (multi-step wizard):
   - Step 1: Basics — title, description, type, max guests/beds/bedrooms/baths
   - Step 2: Location — street/city/state/country/postal + coords
   - Step 3: Pricing — base nightly rate + weekend rate + cleaning fee + min/max stay
   - Step 4: Amenities — checkboxes (uses /admin/amenities catalog from today's work)
   - Step 5: Photos — drag-drop, 1 required, 10 max
   - Step 6: House rules — 3 free-text lines max
   - Step 7: Review + Publish (sets `IsActive=true`)
4. After publish → redirect to `/admin/properties/[id]` (detail/edit view).
5. Owner navigates to `/properties` (public) → sees their listing in search.
6. Click → `/properties/[slug]` → sees photos, amenities, pricing.

**Backend done**: Property + PricingPlan + amenity join. CRUD APIs work.
**Backend gaps**:
- `Publish` endpoint that requires a PricingPlan to exist (return 422 otherwise).
- Photo upload-from-browser path verification (blob storage SAS). If not wired, fall back to base64 inline upload — ugly but ships.

**UI deliverables**:
- `/admin` Dashboard v1 (empty-state-aware; KPI cards stubbed for Slice 2).
- `/admin/properties` list + new + detail/edit (replaces the placeholder we saw).
- Wizard component reusable for Phase 2 multi-property flows.

**Acceptance** (verifiable in browser):
- Fresh DevAuth Owner with no properties lands on `/admin/properties` and sees empty state with a CTA, not a stub.
- Completing the 7-step wizard creates a property AND its pricing plan AND publishes it in one transaction.
- The new property appears in `/properties` within 1 page reload.
- `/properties/[slug]` shows the photos/amenities/price the owner entered.
- Invalid input (missing pricing) blocks Publish with a clear 422 message.

**Risk**: photo upload is the longest-tail item. Scope-flex: ship base64-only first; real blob+SAS in Slice 6 polish.

---

### Slice 2 — Guest books, Owner confirms — THE PRODUCT (~4 days)

**Persona**: Guest then Owner.
**Why this slice**: this is the credibility test. If Slice 2 doesn't demo cleanly, the product story collapses regardless of how much backend exists.

**Journey**:
1. **Guest** on `/properties/[slug]` picks check-in / check-out dates + guest count → "Reserve" button.
2. POST `/bookings/holds` issues 15-min hold; quote rendered with cancellation policy.
3. Checkout page (`/properties/[slug]/checkout` or modal) with Stripe Elements card field; visible 15-min countdown.
4. Card entered → Stripe.js confirms → **PaymentIntent authorized (not captured)** → POST `/bookings` creates Tentative → Guest lands on `/bookings/[id]`.
5. Page shows: "Reservation submitted — awaiting host confirmation. Your card has been authorized but not charged. You'll know within 6 hours."
6. **Owner** on `/admin` sees red pill "1 awaiting confirmation."
7. Click → `/admin/bookings` (list filtered to `status=Tentative`).
8. Click row → `/admin/bookings/[id]` with tabs (Overview / Payments / Timeline).
9. Click **Confirm** → backend captures payment, transitions to `Confirmed`, fires events → Guest's `/bookings/[id]` updates within 30s (polling).
10. Alternative: Owner clicks **Reject** → backend voids PaymentIntent → Guest sees "Host couldn't accept this reservation; no charge made."
11. Alternative: Owner stays silent. 6h later `BookingExpiryWorker` checks → if no iCal conflict, auto-confirm + capture. If conflict, auto-cancel.

**Backend done**: state machine, PaymentIntent + refund flow, conflict detection handler.
**Backend gaps**: Slice 0 items + verifying `POST /bookings/{id}/confirm` exists and captures payment + the booking-detail GET returns the data the UI tabs need.

**UI deliverables** (§12.1, §12.2 screen 3, §12.3, §12.5):
- `/admin` Dashboard v2 — KPI cards: Pending Tentative (count + list), Today's Checkins/Checkouts, MTD Revenue, Sync Health rail.
- `/admin/bookings` list — server-side filters (status, property, date range), sortable columns, sticky header.
- `/admin/bookings/[id]` detail with tabs:
  - **Overview**: line items, cancellation policy, guest info, dates, special requests
  - **Payments**: PaymentIntent status, charges, refunds, "Issue refund" action
  - **Timeline**: booking_status_history rendered chronologically
- Booking detail shows Confirm + Reject buttons when `status=Tentative`.
- Sync conflict banner on booking detail when one exists; opens the §12.5 3-action modal.

**Acceptance**:
- Guest can complete a tentative booking; their card statement shows an authorization, not a charge.
- Owner can confirm in ≤3 clicks from `/admin`.
- On confirm, Guest's `/bookings/[id]` updates to "Confirmed" within 30s.
- A test booking with `tentative_until=NOW()+60s` (feature flag for demo) auto-confirms or auto-cancels on the next worker tick.
- A sync conflict on a Tentative booking surfaces on its detail page before Confirm is clicked.

**Risk**: this slice is wide. Suggest splitting commits: Guest checkout flow → Admin Bookings list → Admin Booking detail → SLA worker integration. Each shippable independently.

---

### Slice 3 — Availability calendar + iCal round-trip (~2 days)

**Persona**: Owner.

**Journey**:
1. Owner on `/admin/calendar` → property dropdown selects one → month grid renders.
2. Bars colored per §12.2 screen 2: Direct (blue), AirBnB (red), Tentative (striped), Blocked (gray), Conflict (red border).
3. Click direct booking bar → side panel with booking detail + quick Confirm/Reject.
4. Click AirBnB bar → side panel "External reservation from feed X, dates D1-D2."
5. "Add iCal feed" link → routes to existing `/admin/sync` page where Owner pastes AirBnB inbound URL.
6. Within 6 minutes the imported events show as red bars on the calendar.
7. Calendar displays the **outbound iCal URL** prominently with a Copy button + instructions to paste into AirBnB host calendar.
8. "Block dates" button on calendar → manual block (creates `availability_blocks` row).

**Backend done**: poll worker, outbound feed, conflict detection, all sync APIs.
**Backend gaps**:
- Calendar aggregator query (already in Slice 0).
- `availability_blocks` table + `POST /properties/{id}/blocks` (small new domain) for owner-manual blocks.

**UI deliverables**:
- `/admin/calendar` month grid component (use `react-day-picker` or a custom CSS grid; bars are absolute-positioned spans).
- `/admin/sync` polish — ensure outbound URL is prominent + has copy + has instructions.

**Acceptance**:
- Owner adds an AirBnB feed URL; within 6 min a test event appears as a red bar.
- Owner's confirmed direct bookings appear when AirBnB calendar subscribes to the outbound URL (verifiable by curling the outbound URL after a confirm).
- Overlapping an iCal event with a tentative booking lights up a red bar AND surfaces in `/admin/sync` Pending Conflicts.
- "Block dates" creates a gray bar that prevents new bookings on those dates.

**Risk**: low. Mostly UI on top of working backend.

---

### Slice 4 — Notifications that actually send (~3 days)

**Persona**: Guest + Owner receive emails.

**Journey**: Same Slice 2 flow — but now:
- Guest gets `booking.received` email at tentative.
- Owner gets `owner.tentative_received` email at tentative.
- Guest gets `booking.confirmed` email when Owner confirms (and `booking.rejected` if Owner rejects).
- Guest gets `booking.cancelled.guest` email on cancellation.
- Owner gets `owner.action_required_24h_reminder` 1 hour before the 6h window closes (config'd).

**Backend done**: NotificationLog persistence, 4 booking event consumers writing Queued rows.

**Backend gaps**:
- `IEmailSender` ACS implementation using `Azure.Communication.Email` SDK (per ADR-0011, port pattern from LankaConnect's `AzureEmailService.cs`).
- `NotificationDispatchWorker` (Container App Job, cron `*/2 * * * *`) — drains Queued rows, renders Mustache, calls ACS, updates status to Sent/Failed with 3-retry exponential backoff (2s/4s/8s).
- **10 Mustache templates** (user-approved stretch from initial 6): booking.received, booking.confirmed, booking.rejected, booking.cancelled.guest, booking.cancelled.owner_notice, owner.tentative_received, owner.action_required_24h_reminder, owner.auto_confirmed, owner.cancellation_alert, owner.sync_conflict. Templates in `Notifications/Templates/*.mustache`. Shared header/footer layout. The remaining 8 from §13 (pre-arrival reminders, lockbox code, dispute/refund/loyalty milestones, admin sync_failure) land in Slice 5 backfill or Phase 2.
- Custom domain DKIM/SPF wiring (OPS task — out of scope for this slice; ACS default sender works for demo).

**UI deliverables**:
- `/admin/notifications` log viewer — list NotificationLog rows with status filter (Queued/Sent/Failed/DeadLetter), per-row Retry button, payload preview.

**Acceptance**:
- Confirming a booking causes a real email to land in the Guest's inbox within 60s.
- A failed email shows as `status=Failed` in the log with the ACS error message and a working Retry button.
- The other 4 funnel emails fire on their state transitions.
- Templates pass a CI render test (sample variables file).

**Risk**: ACS deliverability into Gmail/Outlook requires DKIM/SPF; deferred to OPS but documented. For demo, recipient is a `niroshanaks@gmail.com` and the sender domain is what ACS provisions.

---

### Slice 5 — Stay completes → review + loyalty (~2 days)

**Persona**: Guest then Owner.

**Journey**:
1. Daily `BookingCompletionWorker` (cron `0 6 * * *`) finds `Status=CheckedOut AND checkout_date <= TODAY-1` → transitions to `Completed` → raises `BookingCompleted` event.
2. Loyalty handler (already wired) increments stay count + recomputes tier. Tier-up promotion notification fires (template added here).
3. Notification Worker sends `review.request` email to Guest at T+1 day.
4. Guest clicks email link → `/account/bookings/[id]/review` → submits rating + text.
5. Review appears on `/properties/[slug]` (public, unless property has `moderation_required` toggle).
6. Owner on `/admin/reviews` → sees the new review → can publicly respond (one-shot per §11.1) OR Hide/Restore (already wired).
7. Guest on `/account/loyalty` (new page) sees their tier badge + next-tier progress.

**Backend done**: reviews + moderation + loyalty stay-count handler + review APIs.
**Backend gaps**:
- `BookingCompletionWorker` (daily cron over CheckedOut bookings).
- 2 more templates: `review.request`, `loyalty.tier_promotion`.

**UI deliverables**:
- `/account/bookings/[id]/review` form (Guest).
- `/account/loyalty` tier badge + history.
- `/admin/reviews` list with response box + moderation buttons. Replaces the placeholder.

**Acceptance**:
- A booking with `checkout_date=yesterday` is `Completed` after the morning cron run.
- Guest receives the review request email; clicking it lets them submit; review appears on public property page.
- Owner can respond once; second response is blocked.
- Owner can Hide a review; it disappears from public view but stays in admin list.
- Guest's `/account/loyalty` shows current tier + "X stays until Silver" progress.

**Risk**: low.

---

### Slice 6 — Host↔Guest chat + pricing power-user (~3 days)

**Persona**: Owner and Guest.

**Journey A (chat)**:
1. Owner on `/admin/bookings/[id]` → Messages tab shows active thread (auto-created on Confirm via A7 wiring).
2. Type message → Send → POST `/threads/{id}/messages` → Guest sees it on `/account/messages` within 30s (polling).
3. Owner on `/admin/messages` sees inbox of all threads, unread counts.

**Journey B (pricing)**:
1. Owner on `/admin/pricing` → property dropdown → rules table with drag-reorder.
2. Click "Add rule" → modal with rule kind dropdown (Seasonal / Last-minute / Length-of-stay).
3. Fills params → preview pane shows quote for a chosen date range → save.
4. Owner navigates to `/properties/[slug]` for those dates → quote reflects new rule.

**Backend done (chat)**: messaging aggregates, auto-create on Confirm, all 5 thread endpoints.

**Backend gaps (chat)**: verify polling works; SignalR realtime deferred to Slice 7.

**Backend gaps (pricing)**: 5-rule engine expansion. Phase 1 ships **3 rules** (Seasonal + Last-minute + Length-of-stay), not 5. Gap-night and Occupancy deferred. Each rule = ~half a day domain + handler + tests.

**UI deliverables**:
- `/admin/messages` inbox + active-thread panel (replaces stub).
- `/admin/pricing` editor (replaces stub): property dropdown + rules table + drag-reorder (use `@dnd-kit`) + add/edit/delete + preview pane.

**Acceptance**:
- A Guest message lands on Owner's `/admin/messages` within 30s.
- Owner can reply; Guest sees the reply on `/account/messages`.
- Owner adds a Seasonal rule (Dec 20–Jan 5: +50%) → preview reflects it → `/properties/[slug]` quote for those dates reflects it.
- Rule priority drag-reorder persists across page reloads.

**Risk**: pricing UI is the longest-tail. Scope-flex: ship Seasonal only first; Last-minute + LOS in a follow-up.

---

### Slice 7 — Reports + realtime polish (~2 days)

**Persona**: Owner.

**Journey**:
1. Owner on `/admin/reports` → tabs (Occupancy / Revenue / ADR / Source).
2. Date-range picker + property filter → charts render.
3. CSV export button.
4. Dashboard pill ("1 awaiting confirmation") updates without page refresh when a new tentative booking arrives → SignalR push.

**Backend done**: data sources all exist.
**Backend gaps**:
- 3–4 aggregate query handlers over `bookings` joined with `external_reservations` for source attribution.
- SignalR Service provisioning (Bicep) + `GET /realtime/negotiate` real impl + connection management.

**UI deliverables**:
- `/admin/reports` charts + CSV (use Recharts).
- SignalR client wiring in `/admin` Dashboard pill.

**Acceptance**:
- Owner can filter date range + export occupancy + revenue + ADR as CSV.
- A new tentative booking causes Dashboard pill to increment within 5s, no refresh.

**Risk**: SignalR Serverless setup is real ops work. Isolate to this slice so it can be dropped without affecting funnel.

---

## 4. Cross-cutting risks & race-window handling

### 4.1 Payment timing — change to manual capture

**Current state**: PaymentIntent.create uses `capture_method=automatic`. Charge happens immediately at booking. Rejection refunds via Stripe (cost: ~30¢ + 2.9% non-refundable fee on every rejection).

**Recommendation**: switch to `capture_method=manual` in Slice 0.
- `POST /bookings` creates Tentative + PaymentIntent with `capture_method=manual`. Stripe authorizes (no charge to guest yet) and holds funds for **7 days**.
- `POST /bookings/{id}/confirm` calls `PaymentIntent.capture()`. Owner or SLA worker can trigger.
- `POST /bookings/{id}/reject` calls `PaymentIntent.cancel()` — releases hold immediately; guest sees no charge ever.
- Tentative window default: **6 hours** (config-overridable). Comfortably within Stripe's 7-day auth window even on debit cards.

**Why this differs from §9.4**: §9.4 says auto-capture is the Phase-1 default "because the tentative window is short and refund-on-reject is cleaner than capture-on-confirm operationally." That math depends on rejection rate. In Phase 1 with new owners learning the product, expect rejection rate >5%, which flips the math toward manual capture. The §7 state machine also implies manual capture ("Tentative requires payment auth"). Manual is the correct read.

### 4.2 AirBnB lock-back race (1–2h iCal lag)

**Inherent race**: AirBnB's poll lag means there's a window between when we publish Tentative to our outbound feed and when AirBnB sees it. During that gap, AirBnB could accept a reservation on the same dates.

**Defenses**, layered:
1. **Tentative-in-outbound feed** (already done per §8). The proactive defense — AirBnB sees the dates blocked from the moment we publish.
2. **Pre-confirm re-poll** (new in Slice 0): when Owner clicks Confirm or SLA worker fires, force-poll all feeds for that property synchronously. If a conflicting external event appeared since the booking was placed, **block confirm** with the §12.5 conflict modal. Don't capture payment.
3. **Short auto-confirm window**: 6 hours (down from 24). Less time for AirBnB to accept against our published-as-tentative dates.

**Residual risk**: same-dates booking accepted on AirBnB inside the 1-2h gap → Owner gets a manual conflict to resolve via the 3-action modal (cancel direct / keep direct / manual override). Document this honestly in admin help copy.

### 4.3 Stripe auth lapse

- `BookingExpiryWorker` checks `PaymentIntent.Status` before acting. If `requires_payment_method` or `canceled` (auth lapsed), it transitions booking → `Cancelled` with reason `payment_auth_lapsed` and queues a notification "We couldn't hold your reservation, please rebook."
- Track `payment_authorization_expires_at` denormalized on the booking row for fast queries.
- Optional `PaymentAuthExpiringWorker` (daily) finds Tentatives whose intents expire within 24h and prompts Owner with "Decision needed in 24h." Skip for Phase 1; rely on the 6h tentative window keeping us well clear of 7-day auth ceiling.

---

## 5. What we abandon or defer

| Item | Decision | Reason |
|---|---|---|
| **Service Bus relay (A11)** | Defer to Phase 2 | In-process MediatR + outbox covers Phase 1. Service Bus is only needed when modules go cross-process. The relay is plumbing for a problem we don't have. |
| **Entra External ID cutover (OPS.7)** | Defer to OPS-only phase | DevAuth covers Phase 1 demo and pilot. Real auth is an Ops project. |
| **5-rule pricing engine (full)** | Reduce to **3 rules** | Base + Weekend already exist. Slice 6 adds Seasonal + Last-minute + Length-of-stay. Gap-night and Occupancy are revenue optimization for power users; not funnel-critical. |
| **SignalR realtime everywhere** | Slice 7 only | 30s polling ships and is acceptable. SignalR Serverless provisioning is real Ops work — isolate. |
| **18 notification templates** | Ship **10 in Slice 4** (user-approved stretch), 2 more in Slice 5 | Catalog completeness is documentation. Funnel-critical 10 deliver the moment-of-truth touches; pre-arrival reminders, lockbox code, dispute alerts, loyalty milestones land in Slice 5 or Phase 2. |
| **Auto evidence submission on disputes** | Out of scope | Confirmed Phase 2 per proposal §9.6. Manual via Stripe dashboard. |
| **Pact contract tests, k6 load, ZAP scan** | Keep in CI but don't block slices | Test bar is policy; slices ship behind feature flags if pact diff appears. |
| **Photo CDN/optimization** | Defer to Slice 6 polish | Basic upload in Slice 1 is enough. ImageSharp pipeline can wait. |
| **Multi-currency** | Out of scope | Single currency per property is sufficient. |
| **Property admin search/filter beyond basic** | Defer | Phase 1 single-owner = small property count. |
| **Audit log UI** | Defer | Audit log is written by pipeline behavior; consumed via DB query in incident response, not via UI yet. |
| **Self-service profile editing for owners (display name, payout details, brand)** | Defer | DevAuth fakes this. |
| **A0.2.1/3/4 Docker integration tests** | Keep deferred; pick up post-Slice 7 | Important but not blocking. |

---

## 6. Sequencing rationale

| # | Slice | Cumulative usability after this slice |
|---|---|---|
| 0 | Infra bar | System is honest about its own state. No user-visible change yet, but the foundation is sound. |
| 1 | Owner onboards property | Fresh tenant can exist. We can show a property to anyone. |
| 2 | Booking → tentative → confirm | **The funnel works end-to-end.** This is "Phase 1 minimum viable system." |
| 3 | Calendar + iCal round-trip | Owner can see what's happening across channels. Defensive cross-OTA story holds. |
| 4 | Notifications | Both parties trust the system without watching the screen. Asynchronous operation possible. |
| 5 | Review + loyalty | Post-stay loop closes. Product feels alive across sessions. |
| 6 | Chat + pricing rules | Daily-driver fit-and-finish. Revenue uplift for power users. |
| 7 | Reports + realtime | Operator polish. Owner becomes an analyst. |

**Slices 1–3 are non-negotiable for "Phase 1 done."**
**Slices 4–5 are required to claim §11/§13 compliance.**
**Slices 6–7 are scope-flex; cut to MVP if timing tight.**

---

## 7. Estimated effort

| Item | Days | Cumulative |
|---|---|---|
| Slice 0 | 2 | 2 |
| Slice 1 | 3 | 5 |
| Slice 2 | 4 | 9 |
| Slice 3 | 2 | 11 |
| Slice 4 | 3 | 14 |
| Slice 5 | 2 | 16 |
| Slice 6 | 3 | 19 |
| Slice 7 | 2 | 21 |

**~3 weeks to "Phase 1 demo-able" through Slice 5.** Add 1 week for Slices 6–7. Add 1 week buffer for unforeseen integration friction. **~5 weeks total** to a polished Phase 1.

---

## 8. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/REPLAN.md`.
2. I update `docs/EXECUTION_PLAN.md` to point to this document as the active plan (mark old A-number sequencing superseded).
3. I close pending A-number todos in TodoWrite; replace with Slice 0 → Slice 7 entries.
4. I start Slice 0. No vertical-slice work begins until Slice 0 is verified live on staging.
5. Each slice ends with: I walk you through the journey in the deployed browser. You confirm pass/fail. Backend deliverables inside a slice are unmarked until the journey works.

**If you reject or want changes**: point at the specific slice/sequence/scope decision; I revise this document and re-submit.

---

## 9. Decisions locked in by user (2026-06-10)

1. ✅ **Manual capture for Phase 1.** Authorize at Tentative, capture on Confirm, cancel on Reject. §9.4 cost trade-off accepted.
2. ✅ **6h tentative window.** Default value; config-overridable per property.
3. ✅ **3 pricing rules** (Seasonal + Last-minute + LOS). Gap-night + Occupancy deferred to Phase 2.
4. ✅ **10 notification templates** in Slice 4 (stretch from initial 6). Remaining 8 from §13 deferred to Slice 5 backfill or Phase 2.
5. ✅ **No external demo audience.** Standard polish bar applies.

Housekeeping:
- A9 backend code committed (`ab3a685`) for future Slice 4 to fold into.
- `docs/EXECUTION_PLAN.md` left in place for history; this document is the active plan.
