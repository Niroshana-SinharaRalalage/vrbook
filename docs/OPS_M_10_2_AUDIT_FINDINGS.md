# OPS.M.10.2 — Comprehensive Multi-Tenancy Audit Findings

**Status**: Draft for user review (DO NOT commit; produced by system-architect).
**Date**: 2026-06-28.
**Scope**: Every controller + every MediatR handler across OPS.M.0 → OPS.M.10 Wave 2.
**Trigger**: One real cross-tenant leak (`SearchUsersHandler`) caught during M.10 Wave 2 audit. User asked for confirmation no others were missed.

---

## 0. Method

- Walked every controller (14) and every handler (40+) in the codebase.
- Read the M.9 RLS policy DDL (`RlsMigrationBuilderExtensions.EnableRlsTenantIsolation`) which uses `NULLIF(current_setting('app.tenant_id', true), '')::uuid` — confirming empty GUC denies all reads.
- Confirmed `RlsBypassScope.Enter()` is called in only THREE places: `HandleStripeWebhookCommand:59`, `PlatformTenantStatsLookup:37`, and `RlsBypassDbContextFactory:34`.
- Distinguished four gate classes per the user's brief:
  - **M.4 behavior** = `ITenantScoped` + caller-tenant equality check (writes only).
  - **M.9 RLS** = DB-level `app.tenant_id` GUC + policy (reads + writes).
  - **App-layer filter** = explicit `WHERE x.TenantId == currentUser.TenantId` in handler.
  - **Role-only** = `[Authorize(Roles=...)]` + nothing else.

---

## 1. Findings table

Severity scale:
- **Critical** — cross-tenant data read or write achievable by any Owner/Admin with a valid token.
- **High** — cross-tenant data read or write reachable IF the second-layer gate (RLS) regresses, OR a documented feature is broken end-to-end.
- **Medium** — partial protection; defense-in-depth missing; information disclosure bounded.
- **Low** — brittle pattern; clean-up recommendation.
- **Info** — observation; no code change needed.

| # | Severity | Endpoint / Handler | File:Lines | Description | Risk | Recommended fix | Test pin |
|---|---|---|---|---|---|---|---|
| **1** | **Critical** | `GET /api/v1/admin/users?q=` → `SearchUsersHandler` | `src/Modules/VrBook.Modules.Identity/Application/Users/Queries/SearchUsersHandler.cs:9-20` + `Infrastructure/Persistence/UserRepository.cs:17-46` | Searches `identity.users` (M.9 §3.2 carve-out) with NO tenant filter. Repo `SearchAsync`/`CountAsync` have no scope parameter. | Any Owner/Admin enumerates EVERY user (name + email) on the platform. Confirmed in `CarveOutAppLayerTests.SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user` (currently `[Fact(Skip=...)]`). | Add `Guid tenantId` parameter to `IUserRepository.SearchAsync/CountAsync`; filter by `db.TenantMemberships.Any(m => m.UserId == u.Id && m.TenantId == tenantId && m.DeletedAt == null)`. Handler throws `ForbiddenException` if `currentUser.TenantId is null`. PlatformAdmin bypass via explicit branch. | Unskip `SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user` in `CarveOutAppLayerTests`. |
| **2** | **High** | `POST /api/v1/payments/refunds` → `RefundForBookingHandler` | `src/Modules/VrBook.Modules.Payment/Application/Commands/RefundForBookingCommand.cs:23-145` | Command is **NOT** `ITenantScoped`. No `TenantId` field. Controller doesn't pass `CallerTenantId()`. Tenant isolation depends entirely on RLS filtering `payment.payment_intents` lookup. Returns false silently if RLS filters. | If RLS regresses (migration accident, bypass scope leaked across awaits) an Owner in Tenant A could refund Tenant B's booking by guessing the bookingId. Stripe call still routes through the lookup, but the audit row would be misattributed. | (a) Add `Guid TenantId` to `RefundForBookingCommand`; implement `ITenantScoped`. (b) Controller passes `CallerTenantId()`. (c) Handler asserts `pi.TenantId == cmd.TenantId` after the load → throws 404 if not. | Add to `CrossTenantEndpointMatrix` for `/api/v1/payments/refunds` (Owner-A→Tenant-B bookingId = 404, not 200). |
| **3** | **High** | `POST /api/v1/payments/webhooks/stripe` — handled correctly; **but flag for awareness** | `src/Modules/VrBook.Modules.Payment/Application/Commands/HandleStripeWebhookCommand.cs:59` | Opens `RlsBypassScope.Enter()` for the WHOLE handler. After resolving tenant from `stripe_account_id`, all subsequent writes happen with bypass still active. | If a future code-change inserts an unrelated DbContext call inside the handler scope, it would also run as bypass. Brittle. | Narrow the bypass scope to ONLY the `tenantStripe.GetByStripeAccountAsync` lookup (matching M.9 plan §1.4 row 3 "for the lookup call only"). After tenant resolution, exit the scope; subsequent writes run under the normal tenant GUC. | Add bypass-call-site arch test: `HandleStripeWebhookHandler` opens bypass for at most N consecutive statements; assert via `RlsBypassCallSiteAllowlistTests`. |
| **4** | **High** | `GET /api/v1/properties/{id}/quotes` → `ComputeQuoteHandler` | `src/Modules/VrBook.Modules.Pricing/Application/Quotes/Commands/ComputeQuoteHandler.cs` | `[AllowAnonymous]` endpoint. Reads `pricing.pricing_plans` + `pricing.pricing_rules` via request-scoped DbContext. M.9 RLS policy denies all reads when `app.tenant_id` is empty (anonymous caller). No `RlsBypassScope` opened. | **The public quote endpoint returns 404 for every property under M.9.** The marketplace cannot show prices. Feature is broken end-to-end. | Resolve property→tenant via a small bypass-scoped lookup, then open a `BackgroundTenantScope.Enter(property.TenantId)` around the plan/rules read. Mirrors the Stripe-webhook resolve-then-scope pattern. (Avoid full bypass — keep the read tenant-scoped to the property's tenant.) | Add HTTP-level integration test: anonymous `POST /api/v1/properties/{seeded}/quotes` returns 200 with a non-empty quote. |
| **5** | **High** | `GET /api/v1/properties/{id}/reviews` → `GetReviewsForPropertyHandler` | `src/Modules/VrBook.Modules.Reviews/Application/Queries/GetReviewsForPropertyQuery.cs:9-28` | `[AllowAnonymous]` endpoint. Reads RLS-protected `reviews.reviews`. No bypass. Same root cause as #4. | Public reviews list returns empty for every property under M.9. Feature broken. | Same shape as #4: lookup property→tenant via bypass, then enter `BackgroundTenantScope` for the reviews read. | HTTP integration test: anonymous reviews list returns the seeded reviews. |
| **6** | **High** | `POST /api/v1/bookings/{id}/review` → `SubmitReviewHandler` | `src/Modules/VrBook.Modules.Reviews/Application/Commands/SubmitReviewHandler.cs:15-86` | Guest endpoint. Guest has no `TenantId`. INSERT into `reviews.reviews` with `tenant_id = property.TenantId` violates RLS WITH CHECK because `app.tenant_id` is empty. No bypass. Also touches `catalog.properties` for `tenant_id` lookup (same problem). Fallback hardcoded tenant `…0001` (line 57-61) is a foot-gun. | **Guests cannot submit reviews under M.9.** Documented feature is broken. | Resolve `property.TenantId` via bypass-scoped lookup; enter `BackgroundTenantScope` for the INSERT + the Catalog UPDATE. Remove the hardcoded fallback tenant — throw if `properties.tenant_id` is null. | HTTP integration test: guest persona submits a review → 201. Add to cross-tenant matrix: review's `tenant_id` matches property's tenant. |
| **7** | **High** | `GET /api/v1/feeds/{outboundToken}.ics` → `GetOutboundFeedHandler` | `src/Modules/VrBook.Modules.Sync/Application/Feeds/Queries/GetOutboundFeedQuery.cs:18-55` | `[AllowAnonymous]` endpoint. The outbound token IS the credential. Reads `sync.channel_feeds` to resolve the token → tenant. M.9 RLS on `channel_feeds` denies. **Valid tokens 404 just like invalid ones.** `CarveOutAppLayerTests.Outbound_iCal_feed_with_unknown_token_returns_404_not_data` passes for the wrong reason. | Every iCal subscriber (Airbnb, VRBO, Google Calendar) gets an empty feed. Cross-channel sync is broken. Test pack false-passes the carve-out invariant. | Open `RlsBypassScope.Enter()` for the `IChannelFeedRepository.GetByOutboundTokenAsync` lookup (token IS the credential — exactly the Stripe-webhook pattern). After tenant resolution, enter `BackgroundTenantScope` for the `ConfirmedBookingLookup` read. | New `OutboundFeedHttpTests` HTTP integration test that seeds a known token and asserts `200 OK` with VEVENT content. Update `CarveOutAppLayerTests` so the unknown-token assertion runs alongside a valid-token-200 assertion. |
| **8** | **High** | `GET /api/v1/properties` (Search) → `SearchPropertiesHandler` | `src/Modules/VrBook.Modules.Catalog/Application/Properties/Queries/SearchPropertiesHandler.cs` | `[AllowAnonymous]` public marketplace search. Reads `catalog.properties` via request-scoped DbContext. M.9 RLS denies for empty `app.tenant_id`. No bypass. | **Public marketplace returns zero results.** The entire homepage is broken. | The cleanest fix is a public-read carve-out on `catalog.properties` — add an `IsActive = true AND DeletedAt IS NULL` branch to the policy USING clause. This is the only RLS table whose semantics include "platform-public discovery". Alternative: open `RlsBypassScope` for the anonymous search path only. | HTTP integration test: anonymous `GET /api/v1/properties?q=...` returns seeded properties. |
| **9** | **High** | `GET /api/v1/properties/{slug}` → `GetPropertyBySlugHandler` | `src/Modules/VrBook.Modules.Catalog/Application/Properties/Queries/GetPropertyBySlugHandler.cs` | Same root as #8 — `[AllowAnonymous]` read of `catalog.properties`. RLS denies anonymous. | Property detail pages 404 for all anonymous users. | Same fix as #8 (policy carve-out OR per-handler bypass). | HTTP integration test: anonymous `GET /api/v1/properties/{seeded-slug}` returns 200. |
| **10** | **High** | `GET /api/v1/properties/{id}/availability` → `GetPropertyAvailabilityHandler` | `src/Modules/VrBook.Modules.Booking/Application/Queries/GetPropertyAvailabilityHandler.cs` | `[AllowAnonymous]`. Reads `booking.bookings` + `booking.availability_blocks`. RLS denies anonymous. | Public availability calendar shows everything available even when booked. End-to-end broken. | Resolve property→tenant via bypass; enter `BackgroundTenantScope(property.TenantId)` for the bookings/blocks read. | HTTP integration test. |
| **11** | **High** | Entire guest booking flow: `POST /api/v1/bookings` (`PlaceBookingHandler`), `GET /api/v1/bookings/{id}` (`GetBookingHandler`), `GET /api/v1/bookings` (`MyBookingsHandler`), `POST /api/v1/bookings/{id}/cancel` (`CancelBookingHandler`) | `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs`; `Application/Queries/GetBookingHandler.cs`, `MyBookingsHandler.cs`; `Application/Commands/TransitionHandlers.cs` (CancelBookingHandler) | Guests have no `TenantId` claim → `app.tenant_id` GUC empty → RLS on `booking.bookings`, `booking.booking_holds`, `booking.availability_blocks`, `catalog.properties` denies all reads + the booking INSERT (which is stamped with the property owner's tenant). | **The entire guest checkout flow is broken under M.9.** Place returns 404; list returns empty; cancel can't find row. This is the most critical functional regression introduced by Wave 2. | The architecturally clean fix: introduce a per-request `IGuestTenantResolver` that derives the tenant from the action's resource (property id for Place; booking id for Get/Cancel; the user's first-booking lookup for MyBookings), then opens `BackgroundTenantScope(resolved)` around the data reads/writes. **This is a new abstraction** — flag for architect re-consult before fixing. | Full HTTP integration test pack: guest places, lists, cancels, and confirms via SubmitReview. Pin in a new `GuestBookingFlowHttpTests`. |
| **12** | **High** | `GET /api/v1/admin/properties/{id}/calendar` → `GetPropertyCalendarHandler` | `src/Modules/VrBook.Modules.Booking/Application/Queries/GetPropertyCalendarQuery.cs:25-85` | Owner/Admin endpoint with **no app-layer authorization check at all**. Compare sibling `ListAvailabilityBlocksHandler:31` which DOES check `property.OwnerUserId == currentUser.UserId || IsAdmin`. Relies solely on RLS to filter the bookings/blocks reads. The `IExternalChannelConflictChecker` call may read Sync data outside the same RLS guard. | An Owner of Tenant A can POST a property GUID belonging to Tenant B; RLS filters bookings/blocks to empty (OK) but the handler returns a `PropertyCalendarDto` with `propertyId = request.PropertyId` and the conflicts from the external checker — possible info leak depending on external-checker semantics. | Add the same check `ListAvailabilityBlocksHandler` has: load property via `IPropertyRepository`, `403` if `OwnerUserId != currentUser.UserId && !IsAdmin`. | Add to `CrossTenantEndpointMatrix` for `/api/v1/properties/{id}/calendar`. |
| **13** | **High** | `GET /api/v1/admin/reports/{occupancy\|revenue\|adr\|source}` → all four report handlers via `ReportsAuthorization.ResolvePropertyScopeAsync` | `src/Modules/VrBook.Modules.Reports/Application/Common/ReportsAuthorization.cs:37-58` | Owner-with-explicit-propertyId path checks `snapshot.OwnerUserId == ownerId` but **NOT `snapshot.TenantId == currentUser.TenantId`**. Admin-with-explicit-propertyId path skips the check entirely (line 37). The downstream report query then runs against `booking.bookings`/`sync.external_reservations` and relies on RLS to filter. | An Owner in Tenant A could pass a propertyId they also own in another tenant (multi-tenant owners exist conceptually); an Admin in Tenant A passing Tenant B's propertyId would get RLS-zeroed results (safe but misleading "200 with zero rows"). Property-ownership lookup via `IPropertyOwnerLookup` must also be tenant-scoped. | (a) Add `snapshot.TenantId != currentUser.TenantId → 403` on the owner path. (b) Add the same check on the admin path. (c) Verify `IPropertyOwnerLookup.ListPropertyIdsOwnedByAsync` returns only ids in caller's tenant. | Add to `CrossTenantEndpointMatrix` for all four report routes. |
| **14** | **Medium** | `POST /api/v1/threads/{id}/messages` → `SendMessageHandler` | `src/Modules/VrBook.Modules.Messaging/Application/Threads/Commands/MessageCommands.cs:15-41` | Loads thread and derives `displayName` via `me == thread.GuestUserId ? Guest : Owner` but never calls `thread.IsParticipant(me)`. Sibling `MarkReadHandler:57` performs the check. RLS protects cross-tenant; same-tenant non-participants (a second guest sharing the tenant, or a tenant admin not on the thread) can spoof messages. | Same-tenant message spoofing. Owner-to-Owner-in-same-tenant. Lower than cross-tenant but still real. | Add `if (!thread.IsParticipant(me)) throw new ForbiddenException(...)` at line 26 (matching `MarkReadHandler`). | Add `SendMessage_non_participant_returns_403` integration test. |
| **15** | **Medium** | `POST /api/v1/admin/notifications/{id}/retry` → `RetryNotificationHandler` | `src/Modules/VrBook.Modules.Notifications/Application/Commands/RetryNotificationCommand.cs:25-48` | Command IS `ITenantScoped` (good — M.4 gate fires on caller). But handler queries `db.Logs.FirstOrDefaultAsync(x => x.Id == request.Id)` with no row-level `tenant_id == request.TenantId` check. RLS protects tenant-stamped rows; **but the `notifications.notification_log` policy allows `tenant_id IS NULL`** — so NULL-tenant rows (guest booking emails) are visible to EVERY tenant Admin who knows the id. | Any tenant Admin can retry any guest's booking-confirmation/cancellation email by id. Forces re-send to the guest's inbox. Mild abuse vector + audit-row pollution. | Add `&& (x.TenantId == request.TenantId || x.TenantId == null && IsRetryableForCallerTenant(...))` — and make a product decision about whether null-tenant retries should be PlatformAdmin-only. | Add `RetryNotification_OwnerA_cannot_retry_OwnerB_log` cross-tenant test + `RetryNotification_OwnerA_cannot_retry_null_tenant_guest_email_unless_PlatformAdmin`. |
| **16** | **Medium** | `GET /api/v1/admin/notifications` → `ListNotificationLogHandler` | `src/Modules/VrBook.Modules.Notifications/Application/Queries/ListNotificationLogQuery.cs:23-42` | No explicit tenant filter — relies on RLS. RLS policy permits `tenant_id IS NULL` rows. Consequence: every tenant Admin sees every guest-booking notification (including emails, recipient addresses) ever sent. | PII (guest emails) cross-tenant exposure via NULL-tenant carve-out. Likely UNINTENDED — the M.9 plan §3.4 D6/D12 comment only justifies the NULL clause for the orphan-event case, not for guest-mail visibility. | (a) Tighten the RLS policy on `notification_log` so NULL-tenant rows are PlatformAdmin-only (`OR (tenant_id IS NULL AND current_setting('app.is_platform_admin', true) = 'true')`). (b) Decide product intent for null-tenant guest mail visibility. | Add `OwnerA_notification_log_does_not_include_OwnerB_or_null_tenant_rows` test. |
| **17** | **Medium** | `GET /api/v1/payments/intents/by-booking/{bookingId}` → `GetPaymentIntentForBookingHandler` | `src/Modules/VrBook.Modules.Payment/Application/Queries/GetPaymentIntentForBookingQuery.cs:16-37` | Returns `ClientSecret`. No explicit caller-tenant check — pure RLS reliance. Returns `null` for cross-tenant requests (which yields 404 at the controller). Safe under M.9, but no defense-in-depth. | If RLS regresses, any authenticated user could harvest ClientSecrets by guessing bookingIds. | Add post-load assertion: `pi.TenantId != currentUser.TenantId && pi.Booking.GuestUserId != currentUser.UserId → null`. | Add cross-tenant matrix row for `/api/v1/payments/intents/by-booking/{id}`. |
| **18** | **Medium** | `PUT /api/v1/properties/{id}` raw-SQL writes on child tables → `UpdatePropertyHandler` | `src/Modules/VrBook.Modules.Catalog/Application/Properties/Commands/UpdatePropertyHandler.cs:84-123` | Raw `UPDATE`/`DELETE`/`INSERT` against `catalog.house_rules`, `catalog.property_amenities`, `catalog.properties` via `db.Database.ExecuteSqlInterpolated`. RLS protects `catalog.properties` (verified in M.9 migration); but **`house_rules` and `property_amenities` are NOT in the M.9 §3.1 inventory** — they're carved out (no `tenant_id`) per §3.2 row 13. The M.4 behavior gate only checks caller-tenant equality, not target-property tenant. | Hypothetical: an Owner of Tenant A passes a property GUID belonging to Tenant B AND a request body with their own `TenantId`. The M.4 gate passes (`caller == cmd.TenantId`). The line-44 existence check (request-scoped DbContext) returns null via RLS → throws 404. So this is protected, but ONLY because the existence check happens first. Defense-in-depth: lacking. | Add an explicit `property.TenantId == cmd.TenantId` check after the load, before any raw SQL writes. Independently verify whether `house_rules` and `property_amenities` need their own RLS (they inherit tenant via parent FK; cross-tenant SELECT/UPDATE/DELETE on the child should be impossible because the parent's RLS blocks the join). | Add `UpdateProperty_OwnerA_cannot_mutate_OwnerB_property_children` test (already covered by existence-check 404 but pin the behavior). |
| **19** | **Low** | `PUT /api/v1/admin/channel-feeds/{id}`, `DELETE /api/v1/admin/channel-feeds/{id}` → `UpdateChannelFeedCommand`, `DeleteChannelFeedCommand` | `src/Modules/VrBook.Modules.Sync/Application/ChannelFeeds/Commands/ChannelFeedHandlers.cs:38-62` | Commands are **NOT** `ITenantScoped`. Sole protection is RLS on `sync.channel_feeds`. The post-M.6 `ResolveConflictCommand` fix added `ITenantScoped` exactly for this defense-in-depth purpose; the same fix wasn't backported here. | If RLS regresses, an Admin in Tenant A could mutate/delete Tenant B's channel feed by id. | Make both commands `ITenantScoped`; controller passes `CallerTenantId()` (it already does for `CreateChannelFeedCommand`); handler asserts `feed.TenantId == cmd.TenantId`. | Add to matrix: `UpdateChannelFeed_OwnerA_cannot_modify_OwnerB_feed`. |
| **20** | **Low** | DevAuth: `POST /api/v1/dev-auth/persona-email` → `SetPersonaEmailHandler` | `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs` | Writes `identity.users` (carve-out). Controller checks `DevAuth:AllowAnonymous` flag; handler has no second guard. If the flag is ever true in prod, any caller can rewrite any user's email by `B2CObjectId`. | Prod-misconfiguration impact: full account takeover via email change. | Add `IHostEnvironment.IsProduction() throw` AND `cfg["DevAuth:AllowAnonymous"] != "true" throw` inside the handler. | Add `SetPersonaEmail_rejected_when_environment_is_Production` arch test. |
| **21** | **Low** | DevAuth: `POST /api/v1/dev-auth/backdate-checked-out-at` (in `IdentityController`) | `src/VrBook.Api/Controllers/IdentityController.cs:177-214` | Raw `UPDATE booking.bookings` via Npgsql — bypasses EF entirely, no GUC. Only the DevAuth flag check protects it. | Same prod-misconfiguration class as #20. | Add `IHostEnvironment.IsProduction() return 404` inside the action. | Same arch test as #20 covering both. |
| **22** | **Low** | `ReleaseHoldCommand` → `ReleaseHoldHandler` | `src/Modules/VrBook.Modules.Booking/Application/Holds/Commands/HoldCommands.cs` | Redis-backed hold store. No verification that caller owns the hold. Releases by `holdId` only. | A guest who guesses another guest's hold GUID could release their hold mid-checkout. Practically impossible (uuid v4 entropy), so Low. | Stamp holds with `UserId`; assert match on release. | Optional; could defer. |
| **23** | **Low** | `GetPricingPlanHandler`, Reviews moderation handlers | `src/Modules/VrBook.Modules.Pricing/Application/Plans/Queries/GetPricingPlanHandler.cs`; `src/Modules/VrBook.Modules.Reviews/Application/Moderation/Commands/ModerationHandlers.cs` | Load aggregates with `FirstOrDefaultAsync(x => x.Id == cmd.X)` without explicit tenant equality. Sole defense is RLS. Brittle. | Same "RLS regression = leak" pattern as #2/#17. Defense-in-depth missing. | Add explicit `&& x.TenantId == currentUser.TenantId` for queries; `&& x.TenantId == cmd.TenantId` for commands. | Brittleness, not a leak today — defer if time pressed. |
| **24** | **Info** | `ListMyPropertiesQuery`, `ListAdminBookingsQuery` comments say "Admins see all" | `src/Modules/VrBook.Modules.Catalog/Application/Properties/Queries/ListMyPropertiesQuery.cs:38-41`; `src/Modules/VrBook.Modules.Booking/Application/Queries/ListAdminBookingsQuery.cs:36` | RLS silently scopes "Admin" to the caller's tenant. Comments imply cross-tenant visibility that doesn't exist. | Doc-vs-reality drift. No security risk. | Rewrite comments to "tenant Admin sees all in this tenant" or wire a real PlatformAdmin bypass path if needed. | n/a |
| **25** | **Verify** | Background-worker SaveChanges paths (`ConflictDetectionHandlers`, `OnBookingConfirmedHandler` in Messaging, notification handlers) | `src/Modules/VrBook.Modules.Sync/Application/Conflicts/Handlers/ConflictDetectionHandlers.cs`; `src/Modules/VrBook.Modules.Messaging/Application/Threads/Handlers/OnBookingConfirmedHandler.cs`; Notifications handlers | These run from the outbox-relay worker. They stamp `tenant_id` on the inserted row from the event payload, but the request-scoped `ICurrentUser.TenantId` is null. The interceptor falls back to `BackgroundTenantScope.CurrentTenantId`. **Verify**: does the outbox-relay worker open a `BackgroundTenantScope(event.TenantId)` before invoking the handler? If not, RLS WITH CHECK rejects the INSERT and events silently fail to persist their downstream rows. | If broken: domain events from the outbox produce no notifications, no conflicts, no thread creates. Critical functional regression. | Read `src/Workers/VrBook.Workers.OutboxRelay` (not audited here — out of brief scope). Either the worker opens `BackgroundTenantScope` per event, OR the `IBackgroundCommand` marker on these handlers triggers the M.6 behavior which opens it. The M.6 plan §3.1 D1 mentioned this. **Verify by reading the worker bootstrap.** | M.9 §1.1 row 12 promised RLS integration tests; verify the M.9 fact-pack actually exercises the outbox-relay path end-to-end. |

**Counts**: 25 findings — **1 Critical, 12 High, 4 Medium, 5 Low, 1 Info, 2 Verify**.

**Slice OPS.M.10.2 close-out (2026-06-29)**: status by finding —

| # | Status | Closed by |
|---|---|---|
| 1 (Critical SearchUsersHandler) | **CLOSED** | OPS.M.10.2 C1 `f9c64bd` |
| 2 (RefundForBookingCommand tenant scoping) | **CLOSED** | OPS.M.10.2 F4 `dc96a2e` |
| 3 (HandleStripeWebhook bypass narrowing) | **CLOSED** | OPS.M.10.2 F4 `dc96a2e` |
| 4 (ComputeQuoteHandler) | **CLOSED** | OPS.M.9.1 F6c `1997e77` |
| 5 (GetReviewsForPropertyHandler) | **CLOSED** | OPS.M.9.1 F6c `1997e77` |
| 6 (SubmitReviewHandler + fallback foot-gun) | **CLOSED** | OPS.M.9.1 F6c `1997e77` |
| 7 (GetOutboundFeedHandler) | **CLOSED** | OPS.M.9.1 F6e _this commit_ |
| 8 (SearchPropertiesHandler) | **CLOSED** | OPS.M.9.1 F6b `ac34534` (public-read carve-out) |
| 9 (GetPropertyBySlugHandler) | **CLOSED** | OPS.M.9.1 F6b `ac34534` (public-read carve-out) |
| 10 (GetPropertyAvailabilityHandler) | **CLOSED** | OPS.M.9.1 F6d `42ef3b2` |
| 11 (Guest booking flow: Place/Get/MyBookings/Cancel) | **CLOSED** | OPS.M.9.1 F6d `42ef3b2` |
| 12 (GetPropertyCalendar auth gate) | **CLOSED** | OPS.M.10.2 C2 `9542c74` |
| 13 (Reports authorization tenant check) | **CLOSED** | OPS.M.10.2 C3 `4f7ff20` |
| 14–24 (Mediums + Lows) | **Open** | OPS.M.10.2 F7-F9 (next) |
| 25 (Outbox-relay verify) | **Open** | OPS.M.10.2 F10 (next) |

Critical + all 12 High **closed**. Remaining 4 Medium + 5 Low + 1 Verify ship under F7-F10.

---

## 2. The big-picture root cause behind 8 of the 12 High findings

**Findings #4, #5, #6, #7, #8, #9, #10, #11** all share one root cause: **M.9 was shipped without a "guest/anonymous read" mechanism for tenant-scoped public surfaces**.

M.9's design treats RLS as a closed-world boolean: every `app.tenant_id` GUC is either a real tenant id or empty (and empty denies). The OPS.M.9 plan §1.4 row 6 explicitly stated public quote/property/review reads "do NOT need bypass; the per-statement GUC binding handles it naturally" — **this was wrong**. The per-statement GUC for anonymous callers is empty; the policy `tenant_id = NULLIF('','')::uuid` resolves to `tenant_id = NULL` which is false. Anonymous reads are denied.

The shape M.9 actually needs is:
- A `BackgroundTenantScope`-style mechanism for anonymous endpoints, where the tenant is derived from the URL's resource (property slug, property id, outbound token, booking id for the guest who owns it), and stamped before the read happens.
- An optional per-policy carve-out for `catalog.properties` (the only table whose semantics include "platform-public discovery").

This is an **architectural omission**, not a bug in any one handler. Recommend re-consulting the architect for the design of the resolver — see Part 3 below.

The M.10 test pack didn't catch any of #4-#11 because:
- The matrix only exercises authenticated cross-tenant scenarios. Anonymous endpoints (`[AllowAnonymous]`) are out of matrix by design.
- No HTTP-level integration test hits `ComputeQuote`, `GetReviewsForProperty`, `GetOutboundFeed`, `SearchProperties`, `GetPropertyBySlug`, `GetPropertyAvailability`, the guest booking flow, or `SubmitReview` via the actual HTTP pipeline. The existing `ComputeQuoteHandlerTests` and `ComputeQuoteWithRulesTests` are unit tests with mock repositories — they never see the RLS interceptor.

**Recommendation**: Slice OPS.M.10 Wave 3 (or a new OPS.M.9.1 hotfix slice) must close this hole BEFORE Slice 4. Without it, the platform's public surface is non-functional in any environment that has M.9 deployed (which is the deployed environment per the M.9 close-out commit `b47197e`).

---

## 3. Fix-sequence recommendation

### 3.1 Critical / High dependency graph

```
#11 (Guest booking flow) ─── needs ──> Decision: IGuestTenantResolver abstraction (architect re-consult)
                              │
                              ├──> #4 (ComputeQuote)
                              ├──> #5 (GetReviewsForProperty)
                              ├──> #6 (SubmitReview)
                              ├──> #7 (Outbound iCal)
                              ├──> #8 (SearchProperties) ─── separate path: RLS policy carve-out vs scope
                              ├──> #9 (GetPropertyBySlug)
                              └──> #10 (GetPropertyAvailability)

#1 (SearchUsersHandler) — independent, ship first.
#2 (RefundForBookingHandler) — independent, ship in Stripe-handler group.
#3 (HandleStripeWebhook bypass scope narrowing) — independent, ship in Stripe-handler group.
#12 (PropertyCalendar auth gate) — independent.
#13 (Reports authorization) — independent.
```

### 3.2 Suggested commits (ordered)

| # | Commit | Findings closed | Est. effort | Tests to add/update |
|---|---|---|---|---|
| C1 | `Fix Slice OPS.M.10.2 Critical-01: SearchUsersHandler tenant scoping` | #1 | 3 h | Unskip `CarveOutAppLayerTests.SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user`; add positive-case unskip. |
| C2 | `Fix Slice OPS.M.10.2 High-12: GetPropertyCalendar auth gate` | #12 | 1 h | Add matrix row + integration test. |
| C3 | `Fix Slice OPS.M.10.2 High-13: Reports authorization tenant check` | #13 | 2 h | Matrix rows for occupancy/revenue/adr/source. |
| C4 | `Fix Slice OPS.M.10.2 High-02 + 03: Stripe refund tenant scoping + webhook bypass narrowing` | #2, #3 | 4 h | Matrix row for refunds; arch test for bypass-call-site narrowness. |
| C5 | **ARCHITECT RE-CONSULT — DO NOT CODE YET.** Plan slice OPS.M.9.1 (or OPS.M.10 Wave 3) for the guest/anonymous tenant-resolver. | n/a — planning gate | 4 h (architect doc) | n/a |
| C6 | `Fix Slice OPS.M.9.1: IGuestTenantResolver + scope-around-public-reads` | #4, #5, #6, #7, #8 (if scope-route chosen), #9, #10, #11 | 12-16 h | New `GuestBookingFlowHttpTests`, `OutboundFeedHttpTests`, `PublicQuoteHttpTests`, `PublicReviewsHttpTests`, `PublicPropertySearchHttpTests`. Plus update `CarveOutAppLayerTests` outbound-feed assertions. |
| C7 | `Fix Slice OPS.M.10.2 Medium findings 14-17 + 18 + 19` | #14, #15, #16, #17, #18, #19 | 4 h | Matrix rows + tenant-scoped command arch test. |
| C8 | `Fix Slice OPS.M.10.2 Low findings 20-21` (DevAuth prod-guards) | #20, #21 | 1 h | `DevAuthRejectedInProduction` arch tests. |
| C9 | `Fix Slice OPS.M.10.2 Low findings 22-24` (defense-in-depth + doc cleanup) | #22, #23, #24 | 2 h | n/a (brittleness) |
| C10 | `Verify Slice OPS.M.10.2 finding 25: outbox-relay worker BackgroundTenantScope` | #25 | 2 h (mostly reading) | If broken, separate hotfix commit + integration test. |

**Commit-cadence**:
- Ship C1, C2, C3, C4 in close succession — they're independent, each is small, and each closes a clear finding.
- C5 is a **planning gate**. The user explicitly asked us to consult the architect for multi-module plans (per memory entry `consult_architect_for_planning`). The IGuestTenantResolver is exactly that kind of plan.
- C6 is one fat commit because all the public-anonymous-read fixes share the resolver abstraction. Splitting would require the abstraction landing first as a no-op commit — wasteful.
- C7-C9 are tidy-up commits, low-risk, can ship after Wave 3 plan lands.
- C10 is a verification, may be a no-op.

**Tests invalidated by the fixes**: `CarveOutAppLayerTests.Outbound_iCal_feed_with_unknown_token_returns_404_not_data` still passes after #7 — but the test must be paired with a "valid token returns 200" assertion or it lies about what it's covering. The matrix rows that currently document "endpoint returns 404 for anonymous" expectations may need updating for `properties`, `quotes`, `reviews`, `feeds`, `availability` — they currently false-pass.

**Estimated total effort**: **31-35 engineering hours** (4 days at a sustainable pace). Architect re-consult adds ~half a day before C6 starts.

---

## 4. Verdict

**25 findings: 1 Critical, 12 High, 4 Medium, 5 Low, 1 Info, 2 Verify.**

The Critical (#1) is the previously-known `SearchUsersHandler` leak — confirmed; no other Critical found.

**Eight of the twelve Highs (#4-#11) trace to one architectural omission**: M.9 RLS shipped without a guest/anonymous tenant-resolver mechanism. The public marketplace, the public iCal feed, public quotes, public reviews, and the entire guest booking flow are all broken end-to-end under M.9 today. This was not caught by Wave 2 because the M.10 matrix and the M.9 integration facts both focus on cross-tenant prevention, not "does anonymous still work."

**Recommendation**: Fix all 1 Critical + 12 High **before Slice 4**. Architect re-consult required before C6 (the IGuestTenantResolver design). Suggested cadence: 4 small commits (C1-C4) → architect consult → 1 fat commit (C6) → 3 tidy-up commits (C7-C9) → 1 verification (C10). **Total: 31-35 hours.**

The 4 Mediums and 5 Lows are defense-in-depth and clean-up; they can ship alongside Wave 3 or as a final OPS.M.10.2 close-out commit. They do NOT block Slice 4.

The 2 Verify items (background-worker SaveChanges paths) MUST be confirmed before declaring this audit complete — they may turn into new High findings depending on what the outbox-relay worker does.
