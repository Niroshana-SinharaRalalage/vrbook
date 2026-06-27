# OPS.M.4 — `TenantAuthorizationBehavior` + Cross-Tenant Write Enforcement (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.4 ("MediatR behavior gates writes by tenant_id == claim").
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §3 + line 61 prose ("auto-rejects requests where the resolved aggregate's tenant_id ≠ caller's claim"); `docs/SEQUENCING_RE_EVALUATION_2026_06_27.md` commit `507d357` (scope expansion absorbing 3 items from Slice 4).
**ADR**: `docs/adr/0014-app-roles-global-db-per-tenant.md` (DB-wins membership precedence — the data the behavior reads via `ICurrentUser.TenantId`).
**Predecessors**: Slice OPS.M.1 closed 2026-06-26 (Tenant + memberships). Slice OPS.M.2 closed 2026-06-26 (`ICurrentUser.TenantId` populated). Slice OPS.M.3 closed 2026-06-27 (every tenant-scoped aggregate carries `Guid TenantId`; `NotificationLog`/`AuditLogEntry`/`PaymentWebhookEvent` stay `Guid?`).
**Sequence**: After Slice OPS.M.3; before Slice OPS.M.5 (Stripe Connect — relies on behavior to gate intent creation), Slice OPS.M.8 (Super Admin — consumes the bypass primitive), Slice OPS.M.9 (RLS — read-side defense-in-depth on top of this write-side gate).
**Estimate**: **3 days, not 1.5d** (one-engineer). MTOP line 194's 1.5d estimate covered only the behavior; the 2026-06-27 re-evaluation absorbed event-payload extensions, the conscious-tenant-write pattern, and the arch-test pack. See §11.

This plan is the contract. Slice OPS.M.4 is **write-side authorization + event-payload tenancy + conscious-write enforcement**. Read-side scoping (RLS, query filters) is Slice OPS.M.9 and explicitly out (§7).

---

## 1. Scope summary

Slice OPS.M.4 produces five deliverables, one per Step (§12):

| # | Deliverable | Touches | Net code change |
|---|---|---|---|
| 1 | `Guid TenantId` field added to 13 domain events | `src/VrBook.Contracts/Events/*.cs` + every `Raise(new …)` call site in the 7 emitting modules | +13 record-positional params, ~25 raise-site edits |
| 2 | `ITenantScoped` marker + `TenantAuthorizationBehavior<TReq,TRes>` + DI registration | `src/VrBook.Contracts/Interfaces/`, `src/Modules/VrBook.Modules.Identity/Application/Behaviors/`, `IdentityModule.cs:44` | +2 files, +1 DI line |
| 3 | Per-handler `OwnerUserId` equality check deletions per §D6 table | 4 handlers in Booking + Catalog | net **−45 lines** of authZ boilerplate |
| 4 | `NotificationLog.Queue` signature tightened (no defaulted `Guid? tenantId`) + 3 call sites pass tenant consciously | `src/Modules/VrBook.Modules.Notifications/Domain/NotificationLog.cs:97`, 3 handler files | +3 lookups, breaking signature change |
| 5 | Architecture test: every `ITenantScoped` command must declare a `Guid TenantId` source the behavior can read | `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs` | new |

**Net behavioral change**: every tenant-scoped command must satisfy the behavior or fail at the pipeline (not the handler). Slice 4's owner-bound notification rows inherit the conscious-write pattern from Deliverable 4 — no schema work, M.3 §1.6 already shipped the nullable column.

### 1.1 What Slice OPS.M.3 left for Slice OPS.M.4 to clean up

Three deviations from `OPS_M_3_PLAN.md` §14 land here:
1. **`PropertyOwnerSnapshot.TenantId` stays `Guid?`** (deviation 3). Slice OPS.M.4 tightens to `Guid` — see §D11.
2. **`?? default-tenant` widening** in `PlaceBookingHandler.cs:82`, `AvailabilityBlockHandlers.cs:62`, `ConflictDetectionHandlers`, `ChannelFeedHandlers`. Slice OPS.M.4 deletes these — `PropertyOwnerSnapshot.TenantId` is now non-null so the fallback is dead code.
3. **Self-booking guard** at `PlaceBookingHandler.cs:52` stays in the handler (§D12).

---

## 2. Sequencing — events first, then behavior, then deletions

**Recommended: Steps 1→5 strictly in order.** Reasons:
1. **Events first** because the behavior reads `command.TenantId` against a known-good aggregate — but downstream consumers (Notifications, Messaging, Sync) need `TenantId` on the event payload BEFORE the behavior starts rejecting writes. If we ship the behavior first, a thread-create handler reading `BookingConfirmed` falls back to a cross-module property lookup that the behavior may itself gate. Order matters.
2. **Behavior next, no deletions yet.** Ship the gate with all per-handler owner checks still in place — belt-and-braces. CI proves the behavior alone catches every cross-tenant case before the handler checks come out.
3. **Deletions third** because rollback is "re-add the check" — non-load-bearing.
4. **Conscious-write tightening fourth** — `NotificationLog.Queue` signature flip cascades to 3 call sites. Isolated from steps 1–3.
5. **Arch-test last** so it sees the final shape and locks it.

**Total wave count: 5 deploys.** All small, reversible, individually testable. Deploys 1+2 must co-deploy in a single tag if event consumers and event raisers are split across replicas — see §4.

---

## 3. Behavior design

### 3.1 Marker shape (D1+D2 lock)

**Decision: `ITenantScoped` is a non-bare marker carrying `Guid TenantId { get; }`.** Reflection is a smell — the contract should be type-checked. The behavior reads the value off the command directly; no aggregate round-trip.

```csharp
public interface ITenantScoped
{
    Guid TenantId { get; }
}
```

Every owner-driven command record (`CreatePropertyCommand`, `UpdatePropertyCommand`, `CreateAvailabilityBlockCommand`, `ConfirmBookingCommand`, etc.) implements `ITenantScoped`. The `TenantId` value is stamped by the controller (or the command's own factory) from `currentUser.TenantId.Value`. Derived-tenant commands (`PlaceBookingCommand` issued by a guest) implement it by reading the property's tenant during command construction at the controller — same pattern as `OPS_M_3_PLAN.md` §3.2.

### 3.2 Behavior body (D3 lock)

**Decision: trust `command.TenantId` against `currentUser.TenantId` — no aggregate round-trip.** The aggregate-lookup variant doubles every command's read load, and the value the behavior would read off the aggregate is identical to what the controller stamped on the command (because Slice OPS.M.3 made the aggregate authoritative). Defense-in-depth comes from RLS in Slice OPS.M.9.

```csharp
if (request is not ITenantScoped scoped) return await next();

if (!currentUser.IsAuthenticated)
    throw new ForbiddenException("Sign-in required.");

if (IsPlatformAdmin(currentUser)) return await next();   // §D7 carve-out

if (currentUser.TenantId is null || currentUser.TenantId.Value != scoped.TenantId)
    throw new CrossTenantAccessException(
        attempted: scoped.TenantId, actual: currentUser.TenantId);

return await next();
```

### 3.3 Exception type (D4 lock)

**Decision: new `CrossTenantAccessException : ForbiddenException`.** Subclasses the existing `ForbiddenException` in `src/VrBook.Domain/Common/DomainException.cs:42` so the API layer's RFC 7807 mapping continues working unchanged — but the type name is greppable for telemetry filters and audit-log analysis. Carries `AttemptedTenantId`/`ActualTenantId` for the audit trail.

### 3.4 Pipeline order (D5 lock)

**Decision: `TenantAuthorizationBehavior` runs AFTER `ValidationBehavior` and BEFORE `AuditLogBehavior`.** Order: `LoggingBehavior` → `UnhandledExceptionBehavior` → `ValidationBehavior` → `TenantAuthorizationBehavior` → `AuditLogBehavior` → handler. Reasons:
- Validation first — a malformed request shouldn't burn audit-log rows.
- Tenant gate before audit — but `AuditLogBehavior` at `src/Modules/VrBook.Modules.Identity/Application/Behaviors/AuditLogBehavior.cs:48` already writes `.failed` audit entries on exception, so a cross-tenant attempt becomes `propertyUpdate.failed` with the `CrossTenantAccessException` message captured in the audit row's `before_json`. We get the audit-of-rejection for free.
- Registration via `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>))` immediately before the `AuditLogBehavior` line at `IdentityModule.cs:44`. MediatR runs them in registration order; cross-cutting behaviors from `VrBook.Application.Common.Behaviors` register earlier.

### 3.5 Super Admin bypass (D7 lock)

**Decision: claim-based — `currentUser.HasRole("PlatformAdmin")`.** Slice OPS.M.8 will populate the claim from a `users.is_platform_admin` flag (global, not per-tenant). For Slice OPS.M.4 we ship `IsPlatformAdmin(ICurrentUser)` returning `false` always — the carve-out exists at the right seam but is dormant. M.8 lights it up. Rejected alternatives:
- **`[AllowCrossTenant]` attribute on the command.** Attribute on a `record` requires `[type: AllowCrossTenant]` and reflection — both ugly. Adds a new authorization vocabulary.
- **Connection-string bypass via M.9.** That's RLS bypass for queries; this is write-side authorization. Different surface.

---

## 4. Event payload extensions (D8 lock)

**Decision: 13 events get `Guid TenantId`.** Full sweep of `src/VrBook.Contracts/Events/`:

| Module file | Events bumped | Tenant source at raise site |
|---|---|---|
| `BookingEvents.cs` | `BookingDraftCreated`, `BookingPlaced`, `BookingConfirmed`, `BookingRejected`, `BookingCancelled`, `BookingCompleted`, `BookingConflictDetected` | `Booking.TenantId` (set in `Booking.Place` per OPS.M.3 §3.2) |
| `CatalogEvents.cs` | `PropertyCreated`, `PropertyImageAdded` | `Property.TenantId` |
| `MessagingEvents.cs` | `MessageSent` | `Thread.TenantId` (OPS.M.3 §1.0 row) |
| `ReviewsEvents.cs` | `ReviewSubmitted`, `ReviewApproved` | `Review.TenantId` |
| `SyncEvents.cs` | `SyncConflictDetected` | `SyncConflict.TenantId` |

**Not bumped:**
- `BookingCheckedIn`/`BookingCheckedOut`/`BookingDisputed` — no downstream consumer demonstrably needs it; defer until one does.
- `PaymentAuthorized`/`PaymentCaptured`/`RefundIssued` — Slice OPS.M.5 owns Stripe Connect routing; payment events get the bump in M.5 when the consumer (account routing) lands.
- `NotificationRequested`/`NotificationDispatched`/`NotificationFailed` — internal to the Notifications module pipeline; tenant lives on `NotificationLog` row not the event.
- `TenantCreated`/`TenantActivated`/etc. — already about a tenant; subject id is the tenant.
- `UserRegistered`/`UserEmailVerified`/etc. — pre-tenant-context; user might not have a membership yet.
- `TierPromoted`/`TierDemoted` — loyalty is global per OPS.M.1.
- `MessageRead`/`MessageDeliveryDeferred`/`PropertyUpdated`/`PropertyDeactivated`/`PropertyRatingRecomputeRequested`/`ReviewRejected`/`ReviewResponded`/`PricingPlanUpdated`/`PricingRuleAdded`/`PricingRuleRemoved`/`ExternalReservationImported`/`ExternalReservationCancelled`/`SyncRunFailed` — same-module consumers only, tenant derivable from the foreign key.

**Atomic-deploy constraint.** Steps 1+2 must co-deploy. Outbox replay on a mid-deploy mix of old-raise/new-consume crashes deserialization (record positional ctor mismatch). Single tag, single ACA revision swap.

---

## 5. Per-handler owner-check audit (D6 lock)

The 5 sites from the brief:

| # | File:line | Current check | Action | Justification |
|---|---|---|---|---|
| 1 | `AvailabilityBlockHandlers.cs:31` | `property.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin` | **Delete** | Any tenant member with the appropriate role may block dates — owner-vs-staff distinction not load-bearing for availability. Behavior validates tenant equality; RBAC handled by `[Authorize]` on controller. |
| 2 | `AvailabilityBlockHandlers.cs:99` | same as #1 | **Delete** | same |
| 3 | `PlaceBookingHandler.cs:52` | `property.OwnerUserId == currentUser.UserId.Value` (self-booking) | **KEEP** | Semantically NOT a tenant check — it's a domain rule "guest != owner". MTOP line 194 explicitly preserves this. See §D12. |
| 4 | `TransitionHandlers.cs:62` (`OwnerActionHandler.TransitionAsync`) | `isOwner = property.OwnerUserId == currentUser.UserId.Value; if (!isOwner && !currentUser.IsAdmin)` | **Delete** | Confirm/Reject/CheckIn/CheckOut are owner-side actions but in the tenant model, any tenant member with `TenantRole=Owner` may perform them. The behavior's tenant equality + `currentUser.HasTenantRole(tenantId, "Owner")` covers it. |
| 5 | `UpdatePropertyHandler.cs:47` | `existing.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin` | **Delete** | Tenant property is editable by any tenant Owner/Admin. Same justification as #4. |

**Net deletion**: ~45 lines of authZ boilerplate. The `IsAdmin` short-circuit also goes — `IsAdmin` is a platform-global concept that the M.8 `IsPlatformAdmin` bypass will subsume. Until M.8 lights up the bypass, the test pack covers admin's tenant-bound case as a member with `TenantRole=Admin`.

---

## 6. Per-module checklist (Booking as worked example)

1. **Step 1 (events)**: `BookingEvents.cs:13-65` — add `Guid TenantId` to the 7 booking-event records. `Booking.cs:117/129/142/155/164/177` — every `Raise(new …)` call passes `TenantId` (the aggregate already has it).
2. **Step 2 (behavior)**: `PlaceBookingCommand`, `ConfirmBookingCommand`, `RejectBookingCommand`, `CheckInBookingCommand`, `CheckOutBookingCommand`, `CreateAvailabilityBlockCommand`, `DeleteAvailabilityBlockCommand` implement `ITenantScoped`. Controllers stamp `TenantId` at command construction.
3. **Step 3 (deletions)**: per §5 table.
4. **Step 4 (conscious notification writes)**: Booking does not call `NotificationLog.Queue` directly — the Notifications module's `BookingNotificationHandlers` does. Booking-side: no edit. Notifications-side: 3 `Queue` call sites read tenant from the event's new `TenantId` field.
5. **Step 5 (arch test)**: covered globally in `tests/VrBook.Architecture.Tests/`.
6. **Tests**: `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantWriteRejectionTests.cs` — tenant-A user attempts each tenant-A command with `TenantId = tenantB`; assert `CrossTenantAccessException` and audit row written with `.failed` suffix.

Other modules repeat with their command set (Catalog: 5 cmds; Pricing: 4; Reviews: 3; Sync: 2; Payment: M.5 owns).

---

## 7. Non-goals (explicitly OUT of Slice OPS.M.4)

| Item | Owner slice | Why deferred |
|---|---|---|
| RLS policies / read-side filter enforcement | Slice OPS.M.9 | M.4 is write-side; queries unchanged |
| `IRlsBypassDbContextFactory<TContext>` | Slice OPS.M.9 | Reads, not writes |
| Cross-tenant query handlers | Slice OPS.M.9 | Same |
| Super Admin UI / dashboard | Slice OPS.M.8 | M.4 ships the bypass primitive only; M.8 wires the claim source |
| `notification_log.tenant_id` schema column | (shipped) | Slice OPS.M.3 §1.6 already shipped it nullable; M.4 only tightens write-time discipline |
| Stripe Connect routing changes | Slice OPS.M.5 | Payment events get `TenantId` bump in M.5, not M.4 |
| `BookingCheckedIn`/`BookingCheckedOut`/`BookingDisputed` TenantId bump | Later | No downstream consumer demonstrably needs it |
| `IsAdmin` claim retirement | Slice OPS.M.8 | Behavior treats it as platform-admin proxy; M.8 introduces real `PlatformAdmin` role |
| Per-tenant Redis hold key | Slice OPS.M.6 | Per OPS_M_3_PLAN §10 Q2 deferral |
| `[AllowCrossTenant]` attribute machinery | Never | Rejected per §3.5 |

---

## 8. Out of scope (future phases)

Per MTOP §11 — all Phase 2+ items stay out (subdomain routing, per-tenant Entra, per-tenant ACS, Channel Manager API push, self-serve tenant sign-up).

---

## 9. Scope-cut order (drop top first if 3-day budget bites)

1. **Arch test (Step 5).** Integration test pack at §6 catches the same violations at PR time; the arch test is belt-and-braces. **Recommended cut first.**
2. **`NotificationLog.Queue` signature flip (Step 4).** Slice 4 inherits the conscious-write contract via the arch test instead — once Step 5 ships. If both Step 4 and Step 5 are cut, the pattern stays informal until Slice 4. Acceptable risk.
3. **`PropertyOwnerSnapshot.TenantId` tightening** (a sub-task in Step 3) — defer to Slice OPS.M.9 if needed.
4. **The 5 non-critical event bumps** (`PropertyImageAdded`, `ReviewApproved`, `SyncConflictDetected`, `BookingCompleted`, `BookingDraftCreated`) — add only the consumed-cross-module ones (the other 8). Reduces Step 1 by ~3h.

Never falls: `BookingPlaced`/`BookingConfirmed`/`BookingCancelled`/`BookingRejected`/`MessageSent`/`ReviewSubmitted`/`PropertyCreated` payload bumps; `ITenantScoped`+`TenantAuthorizationBehavior`+DI; the 4 owner-check deletions; the cross-tenant rejection integration tests.

---

## 10. Open questions for user

D1–D12 are decided above. Three judgment calls promoted here for user resolution.

### Q1. Super Admin bypass via `IsAdmin` until Slice OPS.M.8?

§3.5 ships `IsPlatformAdmin()` as a dormant seam. **Today** `currentUser.IsAdmin` (the only existing platform-wide role flag) would bypass — meaning the existing "admin" users mid-staging can edit cross-tenant. **Default**: dormant — `IsPlatformAdmin` always returns `false`, `IsAdmin` does NOT bypass. **Cost of being wrong**: a legitimate platform-admin operator can't touch cross-tenant aggregates until M.8 ships. They can still impersonate a tenant member. Acceptable.

### Q2. `PropertyOwnerSnapshot.TenantId` tightening — Yes / Defer to Slice OPS.M.9?

§D11 lock: **YES, tighten in Step 3.** Cost of being wrong: a previously-uncovered call site that relies on the nullable falls back to a default-tenant value and the compiler doesn't catch it. Mitigation: M.3's close-out grepped both consumers (`ChannelFeedHandlers`, `ConflictDetectionHandlers`); tightening flushes them.

### Q3. Arch test: Roslyn analyzer vs reflection scan?

§D9 lock: **reflection scan** in `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs`. Roslyn analyzer is 2-3× the work for IDE-time enforcement; the reflection version catches violations at test-run time which is "before merge" given CI gates. **Cost of being wrong**: a developer adds a command in a topic branch and only learns at CI time it failed the arch contract. That's normal. **Test contract** (§D10): every `IRequest<>`/`IRequest<T>` whose handler writes to a table with a non-null `TenantId` column either (a) implements `ITenantScoped` with a `Guid TenantId` getter, or (b) is on an explicit allowlist (`PlatformAdminCommands`) with a comment justifying. The list starts empty.

---

## 11. Estimate basis — why 3 days, not 1.5

- Step 1 (13 events × ~15 min for record edit + raise-site updates + cross-module consumer fix): ~5h.
- Step 2 (interface + behavior + DI + ~30 command records adopting marker + controllers stamping `TenantId`): ~6h.
- Step 3 (5 handlers × ~30 min deletion + test update; tighten `PropertyOwnerSnapshot.TenantId` cascade): ~4h.
- Step 4 (`NotificationLog.Queue` flip + 3 call-site lookups for owner-bound rows): ~3h.
- Step 5 (arch test): ~3h.
- Integration test pack (`CrossTenantWriteRejectionTests` — ~12 commands × scenario): ~4h.
- Doc footers + ADR cross-references + close-out: ~2h.

Total: ~27 engineer-hours = **~3 single-engineer working days at sustainable pace**.

---

## 12. Implementation step list

### Step 1 — Event payload `TenantId` (M, ~5h)

13 records get `Guid TenantId` (5 files in `src/VrBook.Contracts/Events/`); every `Raise(new …)` call passes the aggregate's `TenantId`. Consumer handlers reading these events read `e.TenantId` instead of cross-module property lookups. **Co-deploys with Step 2** — single tag.

### Step 2 — Behavior + marker + DI (M, ~6h)

- `src/VrBook.Contracts/Interfaces/ITenantScoped.cs` — new.
- `src/VrBook.Domain/Common/DomainException.cs` — append `CrossTenantAccessException : ForbiddenException`.
- `src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs` — new, sibling to `AuditLogBehavior.cs`.
- `src/Modules/VrBook.Modules.Identity/IdentityModule.cs:44` — register before `AuditLogBehavior` (MediatR runs in registration order).
- ~30 command records adopt `ITenantScoped` (positional `Guid TenantId` field). Controllers stamp from `currentUser.TenantId.Value` (owner writes) or aggregate (guest writes — see `PlaceBookingHandler.cs:82` as the existing pattern).
- Per-handler owner checks STAY in place this step — defense in depth during cutover.

### Step 3 — Per-handler owner-check deletions + `PropertyOwnerSnapshot` tightening (M, ~4h)

- Delete 4 checks per §5 table (keep `PlaceBookingHandler.cs:52` self-booking).
- Tighten `IPropertyOwnerLookup.cs:24` `Guid? TenantId` → `Guid TenantId`. Cascade: delete `?? new Guid("0…001")` widening sites at `PlaceBookingHandler.cs:82`, `AvailabilityBlockHandlers.cs:62`, and the 2 Sync handlers from OPS_M_3 §14 deviation 3.
- `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantWriteRejectionTests.cs` — new test pack, ~12 scenarios.

### Step 4 — Conscious notification-tenant writes (S, ~3h)

- `src/Modules/VrBook.Modules.Notifications/Domain/NotificationLog.cs:97` — drop the defaulted `Guid? tenantId = null`. Now `Guid? tenantId` (required, no default) — caller must pass it consciously, even if `null` for guest-bound mail.
- 3 call sites: `BookingNotificationHandlers.cs:123`, `OwnerNotificationHandlers.cs:153`, `LoyaltyNotificationHandlers.cs:45`. Owner-bound rows pass the event's new `TenantId`; guest-bound rows pass `null` with a 1-line comment explaining why.

### Step 5 — Arch test (S, ~3h)

`tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs` per §10 Q3. Reflects over the loaded MediatR command assembly; asserts every command-record that mutates a tenant-scoped EF entity implements `ITenantScoped` (or is on the empty `PlatformAdminCommands` allowlist).

---

## 13. What gets approved by this document

If you approve:
1. Plan commits as `docs/OPS_M_4_PLAN.md`.
2. **Step 1 + Step 2** ship co-tagged (event/consumer atomicity).
3. **Step 3** ships as one commit per module (4 module-touching commits).
4. **Step 4** ships as one commit (Notifications-only).
5. **Step 5** ships as one commit closing out the slice.

If you reject or want changes: point at the specific §3.x/§4/§5 decision, the §2 sequencing argument, or the §12 step that needs reshaping.

---

## 14. Close-out — `<date TBD>`

*To be filled in by the implementing slice.*

### Per-step commit ledger

| Step | Module(s) | Commit | Files touched |
|---|---|---|---|
| 1 | Contracts + 7 raising modules | `<sha>` | 5 event files + ~25 raise sites |
| 2 | Identity + Contracts + Domain.Common + all 7 modules | `<sha>` | ITenantScoped, CrossTenantAccessException, TenantAuthorizationBehavior, ~30 command records, controllers |
| 3a | Booking | `<sha>` | AvailabilityBlockHandlers + TransitionHandlers + PlaceBookingHandler cleanup |
| 3b | Catalog | `<sha>` | UpdatePropertyHandler |
| 3c | Sync | `<sha>` | ChannelFeedHandlers + ConflictDetectionHandlers (PropertyOwnerSnapshot cascade) |
| 4 | Notifications | `<sha>` | NotificationLog.Queue + 3 call sites |
| 5 | Architecture.Tests | `<sha>` | TenantScopedCommandTests |

### Step 5 deliverables shipped

- `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantWriteRejectionTests.cs` — N cross-tenant rejection scenarios, each asserts both `CrossTenantAccessException` and the `.failed` audit row.
- `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs` — reflection-based contract check.
- `docs/OPS_M_4_PLAN.md` §14 — this close-out section.

### Deviations from this plan

*(to be filled in)*

### Forward links

- Slice OPS.M.5 — Stripe Connect routing reads `Booking.TenantId` to pick the destination account; payment events get their `TenantId` bump in M.5's slot.
- Slice OPS.M.8 — Super Admin lights up `IsPlatformAdmin` (the dormant carve-out at §3.5).
- Slice OPS.M.9 — RLS policies provide defense-in-depth on top of the write-side gate this slice ships.
- Slice 4 — Owner-notification handlers inherit the conscious-tenant-write pattern via the Step 4 signature and Step 5 arch test.
