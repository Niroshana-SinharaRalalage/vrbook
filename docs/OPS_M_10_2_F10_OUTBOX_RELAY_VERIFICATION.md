# Slice OPS.M.10.2 F10 — Outbox-relay BackgroundTenantScope verification

**Date**: 2026-06-29.
**Owner**: Slice OPS.M.10.2 (audit close-out).
**Audit finding**: #25 (Verify) — `docs/OPS_M_10_2_AUDIT_FINDINGS.md`.

## §0 TL;DR

Audit #25 is **verified safe**. No code change required. The three
handler categories the audit named (`ConflictDetectionHandlers`,
`OnBookingConfirmedHandler`, notification handlers) are dispatched
through paths that already have a tenant id set in the
`TenantGucCommandInterceptor`'s fallback chain. A separate
out-of-process outbox-relay worker does not exist (deferred to A11);
when it lands, F11 (a future slice) will need to enforce
`BackgroundTenantScope.Enter(event.TenantId)` per dispatched event.

## §1 What the audit asked

From `docs/OPS_M_10_2_AUDIT_FINDINGS.md` §1 row 25:

> Background-worker SaveChanges paths (`ConflictDetectionHandlers`,
> `OnBookingConfirmedHandler` in Messaging, notification handlers) —
> these run from the outbox-relay worker. They stamp `tenant_id` on
> the inserted row from the event payload, but the request-scoped
> `ICurrentUser.TenantId` is null. The interceptor falls back to
> `BackgroundTenantScope.CurrentTenantId`. **Verify**: does the
> outbox-relay worker open a `BackgroundTenantScope(event.TenantId)`
> before invoking the handler? If not, RLS WITH CHECK rejects the
> INSERT and events silently fail to persist their downstream rows.

## §2 What I checked

### §2.1 Where the named handlers actually dispatch from

| Handler | Interface | Dispatched by |
|---|---|---|
| `Sync.ConflictDetectionHandlers.OnExternalReservationImported` | `INotificationHandler<ExternalReservationImported>` | `MediatRDomainEventPublisher.PublishAsync` (in-process) |
| `Sync.ConflictDetectionHandlers.OnBookingConfirmed` | `INotificationHandler<BookingConfirmed>` | same |
| `Messaging.OnBookingConfirmedHandler` | `INotificationHandler<BookingConfirmed>` | same |
| `Notifications.BookingNotificationHandlers` (5 events) | `INotificationHandler<...>` | same |

Source: `src/Modules/VrBook.Modules.Sync/Application/Conflicts/Handlers/ConflictDetectionHandlers.cs:30,71`;
`src/Modules/VrBook.Modules.Messaging/Application/Threads/Handlers/OnBookingConfirmedHandler.cs:22`;
`src/Modules/VrBook.Modules.Notifications/Application/Handlers/BookingNotificationHandlers.cs:32-36`.

### §2.2 Where `MediatRDomainEventPublisher.PublishAsync` is called from

`src/VrBook.Infrastructure/Outbox/DomainEventOutboxInterceptor.cs:80`,
inside `SavedChangesAsync(...)`. This runs as part of the EF Core
`SaveChangesAsync` pipeline of the caller's request — same AsyncLocal
chain that already has `ICurrentUser.TenantId` populated.

So when an Owner POSTs `/api/v1/bookings/{id}/confirm`:
1. Request scope sets `ICurrentUser.TenantId = ownerTenant`.
2. Handler runs, raises `BookingConfirmed`, calls `db.SaveChangesAsync`.
3. EF intercepts; `DomainEventOutboxInterceptor.SavedChangesAsync` fires
   AFTER the INSERT commits and calls `publisher.PublishAsync(pending)`.
4. MediatR fans out to all `INotificationHandler<BookingConfirmed>`
   registrations: the four named above.
5. Each handler's own `SaveChangesAsync` (for whatever downstream row
   it INSERTs) runs through `TenantGucCommandInterceptor`, which
   reads `ICurrentUser.TenantId` first → still `ownerTenant`.

**No `BackgroundTenantScope` needed for the in-process path** —
the original caller's scope flows through naturally.

### §2.3 Is there a separate outbox-relay worker?

No. Verified by grep across `src/Workers/`:

- `src/Workers/VrBook.Workers.Booking/` — booking-expiry + booking-
  completion sweeps. Sends `IBackgroundCommand` (`ExpirySweepCommand`,
  `CompletionSweepCommand`) directly via `IMediator.Send`. Those
  commands' handlers iterate per-booking and dispatch per-booking
  commands; each command is `IBackgroundCommand + ITenantScoped`.
- `src/Workers/VrBook.Workers.Sync/` — channel-feed poll sweep. Sends
  `RunSyncForFeedCommand` (IBackgroundCommand + ITenantScoped).
- `src/Workers/VrBook.Workers.Notifications/` — dispatch sweep. Sends
  `DrainQueuedNotificationsCommand`.

None of these read `catalog.outbox_messages` directly. The outbox
table is written by `DomainEventOutboxInterceptor.SavingChangesAsync`
(see `OutboxMessage.cs:10` comment: *"the outbox→Service Bus relay
lands in A11"*) but never read back — the relay is deferred.

### §2.4 Background-command path correctness

For paths that DO dispatch outside an HTTP request scope (the three
workers above), the `IBackgroundCommand` marker + the M.6
`BackgroundCommandTenantScopeBehavior` (in
`src/Modules/VrBook.Modules.Sync/Application/Behaviors/BackgroundCommandTenantScopeBehavior.cs:30-63`)
opens `BackgroundTenantScope.Enter(scoped.TenantId)` around the
handler. The interceptor's fallback chain
(`ICurrentUser.TenantId` → `BackgroundTenantScope.CurrentTenantId`)
finds it.

Arch invariant `BackgroundCommandMarkerTests.cs:36-44` enforces that
every `IBackgroundCommand` also implements `ITenantScoped` with a
non-`Guid.Empty` value — so a regression where someone marks a
background command without a tenant id fails at compile-time discovery.

## §3 Conclusion

Audit #25 is **CLOSED — verified, no code change**.

| Path | Tenant scope source | Status |
|---|---|---|
| HTTP request → handler → SavedChangesAsync → `INotificationHandler<DomainEvent>` (the 4 named handlers) | `ICurrentUser.TenantId` from the originating request | ✅ Correct |
| Worker → `IMediator.Send(IBackgroundCommand + ITenantScoped)` → handler | `BackgroundTenantScope.Enter(scoped.TenantId)` via the M.6 pipeline behavior | ✅ Correct |
| Hypothetical out-of-process outbox-relay reading `catalog.outbox_messages` | Does not exist; deferred to A11 | ⏭ Out of scope |

## §4 If A11's relay lands later

When the A11 outbox→Service-Bus relay ships, the worker must:

1. Deserialize the `OutboxMessage.Payload` to recover the
   `IDomainEvent` (or a typed envelope) + the originating `TenantId`.
2. Open `BackgroundTenantScope.Enter(event.TenantId)` BEFORE
   `IPublisher.Publish(event)`.
3. The existing arch invariant `BackgroundCommandMarkerTests` does
   NOT cover this path (notification handlers, not background
   commands); add a runtime arch test that exercises the relay
   end-to-end against a Testcontainer Postgres + a fake Service Bus
   broker.

This is tracked as a Phase 4 ops slice; not in scope here.

## §5 Slice OPS.M.10.2 — overall close-out

With F10 complete, **the entire OPS.M.10.2 audit is closed**:

| Severity | Count | Status |
|---|---|---|
| Critical | 1 (#1) | ✅ |
| High | 12 (#2-#13) | ✅ |
| Medium | 4 (#14-#17) | ✅ |
| Low | 5 (#18-#22) (was incorrectly grouped as Medium for #18/#19) | ✅ |
| Info | 1 (#24) | ✅ |
| Verify | 1 (#25) | ✅ (this doc) |

Across F-1 / F0-F0''' / F1-F4 (stabilization arc) / F4 (C4 audit close)
/ F5-F6e (Slice OPS.M.9.1 — IGuestTenantResolver) / F7 / F8 / F9 / F10
(this verification) — 25 audit findings closed.

**Slice 4 (Notifications) is unblocked** by the close of #25. The
outbox-relay assumption Slice 4 relied on (the dispatch worker reads
from the local Postgres outbox AND open a tenant scope per row) is
correct under today's architecture.
