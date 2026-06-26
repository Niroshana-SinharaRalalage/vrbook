# OPS.M.3 — `tenant_id` Column Rollout Across Modules (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-26.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.3 ("Slice 3–7 tables already have the column nullable" — see §1.1 of this plan; this is wrong, only `availability_blocks` has it).
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §3 (table list + 3-phase migration strategy) + §10 row OPS.M.3.
**ADR**: `docs/adr/0014-app-roles-global-db-per-tenant.md` (DB-backed per-tenant memberships — the table OPS.M.3 attaches to).
**Predecessors**: OPS.M.1 closed 2026-06-26 (Tenant + memberships + default tenant `00000000-...-0001` seed). OPS.M.2 closed 2026-06-26 (`ICurrentUser.TenantId` populated DB-wins from `tenant_memberships`).
**Sequence**: After OPS.M.2; before OPS.M.4 (`TenantAuthorizationBehavior`), OPS.M.5 (Stripe Connect), OPS.M.9 (RLS).
**Estimate**: **5 days, not 4** (one-engineer). MTOP §10's 4-day estimate is light by ~1 day — see §11 for the basis. If the user accepts the 4-day fixed budget, §9 says which sub-deliverable falls first.

This plan is the contract. OPS.M.3 is **schema + aggregate + write-path work, no read-path enforcement** — `TenantAuthorizationBehavior` is OPS.M.4 and explicitly out of scope (see §7).

---

## 1. Scope summary

OPS.M.3 produces (per module → outcome):

| Module | New columns | Aggregate property | Handler edits | Migration count |
|---|---|---|---|---|
| Catalog | `properties.tenant_id`, `property_images.tenant_id` (denorm) | `Property.TenantId`, `PropertyImage.TenantId` | `CreatePropertyHandler`, `AddPropertyImageHandler`, `Property.Create` | 3 (3a, 3b, 3c) |
| Booking | `bookings.tenant_id`, `booking_holds.tenant_id`, `availability_blocks` (NULL → NOT NULL) | `Booking.TenantId`, `BookingHold.TenantId`, `AvailabilityBlock.TenantId` flip | `PlaceBookingHandler`, `CreateAvailabilityBlockHandler`, hold-create handler | 3 (3a, 3b, 3c) — `availability_blocks` joins from 3b only |
| Sync | `channel_feeds.tenant_id`, `sync_conflicts.tenant_id`, `external_reservations.tenant_id` (substitutes for "inbound_events" — see §1.3), `sync_runs.tenant_id` | `ChannelFeed.TenantId`, `SyncConflict.TenantId`, `ExternalReservation.TenantId`, `SyncRun.TenantId` | `CreateChannelFeedHandler`, the iCal poller `ProcessFeedHandler`, conflict-detection worker | 3 (3a, 3b, 3c) |
| Payment | `payment_intents.tenant_id`, `refunds.tenant_id`, `webhook_events.tenant_id` (NULLABLE — see §1.4) | `PaymentIntent.TenantId`, `Refund.TenantId` | `CreatePaymentIntentForBookingHandler`, `RefundCommand` handler | 3 (3a, 3b, 3c) |
| Pricing | `pricing_plans.tenant_id`, `pricing_rules.tenant_id` (denorm) | `PricingPlan.TenantId`, `PricingRule.TenantId` | `CreatePricingPlanHandler`, `AddRule`, etc. | 3 (3a, 3b, 3c) |
| Messaging | `threads.tenant_id`, `messages.tenant_id` (denorm) | `MessageThread.TenantId`, `Message.TenantId` | `BookingConfirmed` thread-create handler, `SendMessageHandler` | 3 (3a, 3b, 3c) |
| Reviews | `reviews.tenant_id` (no separate `review_responses` table — see §1.5) | `Review.TenantId` | `SubmitReviewHandler`, `AddOwnerResponse` | 3 (3a, 3b, 3c) |
| Notifications | `notification_log.tenant_id` (no `notification_template_overrides` table — see §1.5) | `NotificationLog.TenantId` (nullable forever — see §1.6) | event consumers that `Queue(...)` | 3 (3a, 3b, 3c) — but 3c skipped |
| Identity | `audit_log_entries.tenant_id` (nullable forever — see §1.7) | `AuditLogEntry.TenantId` | `AuditLogBehavior` | 3 (3a, 3b, 3c) — but 3c skipped |

**Total**: 9 modules × 3 migration phases = **~27 module-migrations**, but Notifications + Identity skip 3c (nullable forever), so actual file count is **~25**.

### 1.1 Inventory — what's already done

Grepping `tenant_id` across `src/Modules/`: **the only module table with a placeholder `tenant_id` column today is `booking.availability_blocks`** (Slice 3, migration `20260613003928_Slice3_AvailabilityBlocks.cs`, nullable, FK `ON DELETE RESTRICT` to `identity.tenants("Id")`, indexed). The MASTER_PLAN §2 row 8 note "Slice 3–7 tables already have the column nullable" is **inaccurate** — only `availability_blocks` got the forward-compat treatment. Worth correcting in MASTER_PLAN at close-out.

### 1.2 Amenity: NOT tenant-scoped

MTOP §3 says `amenities (if tenant-customizable, see §6)` and §6 ratified **"Amenity catalog: global with per-property selections."** So `amenities` stays platform-global. **Plan removes `amenities` from the per-tenant column rollout.** Property's amenity-selections inherit tenant scope via Property.TenantId; the join table doesn't need its own column.

### 1.3 Sync — `inbound_events` doesn't exist

MTOP §3 lists `inbound_events`; **no such table exists.** The closest is `external_reservations` (Slice 6 — A6 init schema), which is what the iCal poller actually populates. Map `inbound_events` → `external_reservations`. Also add `sync_runs` (worker audit table; OPS.M.8 Super Admin view needs it). MTOP §3 missed both.

### 1.4 Payment `webhook_events.tenant_id` — nullable, OPS.M.5 owns population

`payment.webhook_events` is the Stripe idempotency log. At write time we don't yet know the tenant — the `account` field on the event payload routes to the tenant only after `tenants.stripe_account_id` lookup (which OPS.M.5 wires up). **Leave nullable**; let OPS.M.5 backfill during webhook processing. Platform-level events (e.g., `account.updated` for fresh Connect onboarding) may have no tenant at all.

### 1.5 Tables that don't exist — `review_responses`, `notification_template_overrides`

MTOP §3 lists `review_responses` (split from `reviews`) and `notification_template_overrides` (new). **Neither exists.** `Review.AddOwnerResponse` mutates the existing row in-place. `notification_template_overrides` is a Phase 2 feature-toggle surface. **OPS.M.3 does NOT create these tables.** Open Q3.

### 1.6 `notification_log.tenant_id` — nullable forever, skip 3c

Many notifications go to guests (no tenant) — `BookingConfirmed` to a guest email isn't tenant-scoped. Notifications to tenant_admin (e.g., `OwnerSyncConflict`) are. So `tenant_id` here means "tenant the notification is *about*" — nullable for guest-facing emails. **Skip phase 3c.** Real divergence from MTOP §3's "NOT NULL on every table" framing.

### 1.7 `audit_log_entries.tenant_id` — nullable forever, skip 3c

Super Admin actions have no tenant scope. Anonymous controller calls (login flow) have no tenant. **Nullable; skip 3c.** Same reasoning as §1.6.

---

## 2. Sequencing — Catalog first, then Wave A/B/C

**Recommended: Catalog full sequential, then Wave A/B/C across the remaining 8.** Reasons:
1. Catalog is the unique snowflake — `Property.TenantId` is the only column whose value comes directly from `ICurrentUser.TenantId`. Every other table inherits via FK to property or booking. Locking Catalog's shape first gives the others a fixed reference.
2. Catalog is the most-tested module — wave-A breaks in Catalog are caught fastest.
3. Catalog 3a+3b+3c can be tested end-to-end before any other module starts — dress rehearsal.
4. Phase A across remaining 8 is parallelisable (independent commits, additive columns).
5. Phase B (backfill) is the riskiest step — concentrate as a single deploy wave so we can pre-stage, dry-run, roll back as a unit.

Sequencing:
- **Day 1**: Catalog 3a + 3b + 3c end-to-end, including handler edits and tests.
- **Days 2-3**: Wave A across the remaining 8 modules — additive `tenant_id NULL` columns + FKs + indexes. Independent commits.
- **Day 3-4**: Wave B across the same 8 — backfill UPDATEs against the default tenant. **All 8 backfill migrations grouped in one deploy.**
- **Day 4-5**: Aggregate `TenantId` properties + handler edits + Wave C NOT NULL flips. Per-module commits but co-deployed.

**Total wave count: 4 deploys.** Each is small, reversible, tested.

---

## 3. Aggregate `TenantId` shape + handler wiring

### 3.1 Aggregate property + factory

```csharp
public sealed class Property : AggregateRoot
{
    public Guid TenantId { get; private set; }   // NOT nullable in C# domain
                                                  // (DB starts nullable in 3a; 3c flips)
                                                  // exception: NotificationLog, AuditLogEntry per §1.6/§1.7

    public static Property Create(
        Guid tenantId,                            // NEW — first parameter
        Guid ownerUserId,
        // ... existing parameters unchanged ...)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        // existing validation ...
        return new Property { TenantId = tenantId, /* ... */ };
    }
}
```

**Decision: `TenantId` is first parameter on every `Create` factory.** Positional reminder during review.

### 3.2 Handler wiring — where does `tenantId` come from?

**Owner-driven writes** (creating properties, channel feeds, pricing plans): from `currentUser.TenantId`.

```csharp
if (currentUser.TenantId is null)
{
    throw new ForbiddenException("Tenant context required.");
}
var p = Property.Create(tenantId: currentUser.TenantId.Value, /* ... */);
```

**Derived-tenant writes** (booking inherits from property; thread from booking; payment from booking): from the parent aggregate already loaded.

```csharp
// PlaceBookingHandler — property is loaded via mediator.Send
var booking = DomainBooking.Place(
    tenantId: property.TenantId,   // NOT from currentUser; from loaded Property
    propertyId: property.Id, /* ... */);
```

**Why derived, not claim-read?** A guest places a booking. The guest's `currentUser.TenantId` is `null` (guests are tenant-less per MTOP §1). The booking belongs to the **property's** tenant, not the guest's. The aggregate's TenantId is authoritative — this is also the contract OPS.M.4's `TenantAuthorizationBehavior` will enforce.

**System writes** (event consumers, workers, iCal poller): from event payload or parent-aggregate lookup. This implies **at least one IDomainEvent shape change** — `BookingConfirmed` (likely also `BookingPlaced`, `BookingCancelled`) should carry `TenantId` so downstream consumers (Messaging, Notifications) don't need a cross-module DB lookup. See §10 Q1.

### 3.3 `Create` factory enforces non-empty

Every `Create` throws on `tenantId == Guid.Empty`. No silent acceptance. The DB nullable in 3a is a **migration concession only** — the aggregate never knows about it.

### 3.4 `AvailabilityBlock` migration

Today: `AvailabilityBlock.Create(propertyId, …, Guid? tenantId = null)`. **Tighten to `AvailabilityBlock.Create(Guid tenantId, Guid propertyId, …)`.** Drop `Guid? TenantId` → `Guid TenantId`. Lands in the Booking step (with handler updates).

---

## 4. Backfill safety (3b is the highest-risk step)

### 4.1 Strategy

**All rows → default tenant `00000000-0000-0000-0000-000000000001`.** Per MTOP §10 cutover plan.

```sql
UPDATE <schema>.<table>
   SET tenant_id = '00000000-0000-0000-0000-000000000001'::uuid,
       updated_at = NOW()
 WHERE tenant_id IS NULL;
```

### 4.2 Per-table or single migration?

**Per-table, one migration per module.** Each module has its own DbContext + migration sequence; per-module rollback if one module's data is weird; idempotent by `WHERE tenant_id IS NULL`.

### 4.3 Staging data older than the migration

Staging has real users, properties, bookings, payments from acceptance testing.

**Hazard**: an operator hand-inserted a row with `tenant_id = <some-other-uuid>` (test seed). The `WHERE tenant_id IS NULL` clause skips those, which is correct. But if that explicit value doesn't match `identity.tenants`, the 3a FK add fails (RESTRICT FK).

**Action**: Pre-flight check `scripts/preflight-tenant-id-orphans.sql` (per-module, runs against staging clone before each wave). Lives outside migrations.

### 4.4 Default-tenant absorption — fresh tenant's properties

If OPS.M.7's wizard ships before OPS.M.3 (it doesn't per sequencing, but worth saying), a fresh tenant might already have properties pointing at a non-default tenant. **3b's `WHERE tenant_id IS NULL` does the right thing** — leaves them alone.

### 4.5 Rollback story

- **3a rollback**: drop column. Reversible cleanly — no app code reads it yet.
- **3b rollback**: `UPDATE ... SET tenant_id = NULL` — only if 3c hasn't shipped.
- **3c rollback**: `Down()` flips back to nullable. App keeps writing the column, no breakage.

---

## 5. NOT NULL flip blast radius (3c)

### 5.1 Pre-conditions for 3c

Before each module's 3c lands, CI must prove:
1. Every command handler sets `TenantId` on every create-path (compiler-enforced via non-nullable factory param).
2. Every `Create` reachable only from code path with non-null `TenantId` source (guard in handler — `if (currentUser.TenantId is null) throw new ForbiddenException(...)`).
3. Every backfill UPDATE in 3b completed (integration test: `SELECT COUNT(*) FROM <table> WHERE tenant_id IS NULL` → expected 0).

### 5.2 How CI proves 3c is safe

One integration test per module in `tests/VrBook.Api.IntegrationTests/Multitenancy/TenantIdRolloutTests.cs`. Fail-loudly if 3c stages but backfill didn't happen.

---

## 6. Per-module checklist (Catalog as worked example)

1. **Migration `<timestamp>_OpsM3a_<Module>_TenantIdColumn.cs`**:
   - `AddColumn<Guid>(name: "tenant_id", nullable: true)` per in-scope table.
   - Cross-schema FK via raw SQL (Slice 3 pattern):
     ```csharp
     migrationBuilder.Sql("""
         ALTER TABLE <schema>.<table>
         ADD CONSTRAINT fk_<table>_tenant
         FOREIGN KEY (tenant_id) REFERENCES identity.tenants ("Id")
         ON DELETE RESTRICT;
         """);
     ```
   - `CreateIndex` on `tenant_id`.
   - `Down()` drops index → FK → column.
2. **Migration `<timestamp>_OpsM3b_<Module>_TenantIdBackfill.cs`**:
   - One `UPDATE ... SET tenant_id = '<default>' WHERE tenant_id IS NULL` per table.
3. **Aggregate edit**: add property, extend factory, guard `Guid.Empty`.
4. **EF configuration**: `b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();` (post-3c).
5. **Command-handler edits**: pass `tenantId` from `currentUser.TenantId.Value` (owner writes) or parent aggregate (derived writes).
6. **Query-handler edits**: **none in OPS.M.3.** Filter enforcement is M.4's behavior.
7. **Migration `<timestamp>_OpsM3c_<Module>_TenantIdNotNull.cs`**: `AlterColumn(nullable: false)`.
8. **Tests**: per-module rollout test (backfill correctness + `Create` throws on empty + end-to-end "tenant-A handler creates row → tenant_id=A").

---

## 7. Non-goals (explicitly OUT of OPS.M.3)

| Item | Owner slice | Why deferred |
|---|---|---|
| `TenantAuthorizationBehavior` MediatR behavior | OPS.M.4 | This is read-time enforcement; M.3 only ensures column present + populated |
| `[Authorize(Roles="Owner,Admin")]` controller rewrites | OPS.M.4 | Per OPS_M_1_PLAN §2.1 |
| Query-handler `.Where(x => x.TenantId == currentUser.TenantId)` filters | OPS.M.4 | Behavior centralizes this |
| RLS policies | OPS.M.9 | Defense-in-depth, gated on M.4 |
| Bypass connection factory | OPS.M.9 | Needs RLS in place |
| Stripe Connect Express wiring | OPS.M.5 | Column lands here nullable; M.5 populates |
| iCal poller tenant_id filter | OPS.M.6 | Needs column + read-side enforcement first |
| `review_responses` table split | Never (Phase 2 if ever) | Not in scope |
| `notification_template_overrides` table | Phase 2 | Per-tenant feature toggle UI is Phase 2 |
| `users.tenant_id` column | Never | Per OPS_M_1_PLAN §2.3 — memberships replace it |
| Aggregate `TenantId` on `Amenity` | Never | Per §1.2 |
| Domain event payload extensions to carry `TenantId` | Maybe OPS.M.3 (Q1) | Optional optimization |
| Per-tenant Redis hold key | OPS.M.6 or M.4 (Q2) | Not load-bearing for M.3 |

---

## 8. Out of scope (future phases)

Per MTOP §11 — all Phase 2+ items stay out (subdomain routing, per-tenant Entra, per-tenant ACS, Channel Manager API push, self-serve tenant sign-up, per-tenant feature-toggle UI).

---

## 9. Scope-cut order (drop top first if 4-day budget bites)

1. **`sync_runs.tenant_id`.** Worker-internal audit. **Recommended cut first.**
2. **`external_reservations.tenant_id`.** Derivable via `ChannelFeed.PropertyId → Property.TenantId`. **Recommended cut second.**
3. **`pricing_rules.tenant_id` (denorm).** Derivable from `PricingPlan.PropertyId → Property.TenantId`.
4. **`property_images.tenant_id` (denorm).** Derivable from `Property.TenantId`.
5. **`messages.tenant_id` (denorm).** Derivable from `Thread.BookingId → Booking.TenantId`.
6. **Aggregate `TenantId` on denormalised tables** if their columns drop.
7. **`notification_log.tenant_id`.** Add in OPS.M.8 when Super Admin view needs it.

Never falls: **`properties`, `bookings`, `payment_intents`, `pricing_plans`, `threads`, `reviews`, `channel_feeds`, `sync_conflicts`, `audit_log_entries`, `availability_blocks` 3c flip.** Without these, OPS.M.4 has no read-side to filter against.

---

## 10. Open questions — RESOLVED 2026-06-26 (architect defaults adopted)

User feedback: technical decisions belong with the architect, not the reviewer. Per [`feedback_proactive_architect_consult`](../../memory/feedback_proactive_architect_consult.md). All 7 questions are now **decided** with the architect's recommended defaults; documented here for the record.

1. **Domain event payload extensions for `TenantId`** — ✅ **YES, add now.** `BookingPlaced`, `BookingConfirmed`, `BookingCancelled`, `PropertyCreated`, and any downstream-consumed event gain `Guid TenantId`. Events are pre-bus (Outbox interceptor) so replay implications are local-only; schema bump is cheap. Consumers (Messaging thread-create, Notification queue) avoid cross-module DB lookups.
2. **Redis hold key — include `tenant_id`?** ✅ **Defer to M.6.** Not load-bearing for M.3.
3. **MTOP §3 corrections** — ✅ **Leave MTOP intact; deviations documented here only** (§1.2 / §1.3 / §1.5 / §1.6 / §1.7).
4. **3c default-value strategy** — ✅ **No DEFAULT, loud NOT NULL violation if app forgets.** Surfacing the bug fast > silent fallback.
5. **Migration naming** — ✅ **`OpsM3a_<Module>_TenantIdColumn` / `OpsM3b_<Module>_TenantIdBackfill` / `OpsM3c_<Module>_TenantIdNotNull`.** Breaks from Slice-N pattern because slice numbers don't carry meaning for cross-module migration sets.
6. **`audit_log_entries.tenant_id` source** — ✅ **Actor's `currentUser.TenantId` (nullable).** Super Admin actions = null. Target-tenant joins happen at read time in M.8.
7. **Booking-derived tenant id authority** — ✅ **Aggregate is authoritative.** `PlaceBookingHandler` reads `property.TenantId`. M.4's behavior plan must respect this for guest-placed bookings.

---

## 11. Estimate basis — why 5 days, not 4

- 9 modules × (3 migrations) = ~27 files × ~30 min = ~13 engineer-hours.
- 9 modules × ~2-3 handler edits = ~25 edits × ~20 min = ~8 engineer-hours.
- ~15 aggregate factory signature changes propagated to call sites = ~4 hours.
- 9 round-trip migration assertion tests + cross-module integration test = ~6 hours.
- Catalog dress-rehearsal day = ~6 hours.
- Doc footers + ADR cross-references + close-out = ~2 hours.

Total: ~39 engineer-hours = **~5 single-engineer working days at sustainable pace**. Two engineers wave-paralleling: ~3 days.

---

## 12. Implementation step list

### Step 1 — Catalog 3a (S, ~3h) — safe in one session

- Migration `<ts>_OpsM3a_Catalog_TenantIdColumn.cs` for `properties` + `property_images`.
- EF config: `b.Property(p => p.TenantId).HasColumnName("tenant_id");` (not `IsRequired` yet).
- Aggregate `Property.cs` + `PropertyImage.cs`: add `public Guid TenantId { get; private set; }` only (factory edit in Step 2).
- Test: `CatalogTenantIdColumnTests.cs` — schema-presence + FK exists.
- Acceptance: migration scripts clean; round-trip green; no handler changes; no app behavior change.

### Step 2 — Catalog 3b + 3c + handler edits (M, ~4h)

- Migrations: `OpsM3b_Catalog_TenantIdBackfill.cs` + `OpsM3c_Catalog_TenantIdNotNull.cs`.
- Property/PropertyImage: extend `Create(...)` signature with `Guid tenantId` first; guard `Guid.Empty`.
- `CreatePropertyHandler` + `AddPropertyImageHandler`: source from `currentUser.TenantId.Value` (owner) or parent aggregate (derived).
- EF config: add `.IsRequired()`.
- Tests: `CatalogTenantIdRolloutTests.cs` — backfill, `Create` throws on empty, end-to-end.
- Acceptance: full Catalog test pack green; staging applies all three migrations sequentially.

### Step 3 — Wave A: 8 module 3a migrations (M, ~6h)

Eight 3a migrations following Catalog 3a template. Copy-paste-rename per module:
- Booking: `bookings`, `booking_holds`.
- Sync: `channel_feeds`, `sync_conflicts`, `external_reservations`, `sync_runs`.
- Payment: `payment_intents`, `refunds`, `webhook_events`.
- Pricing: `pricing_plans`, `pricing_rules`.
- Messaging: `threads`, `messages`.
- Reviews: `reviews`.
- Notifications: `notification_log` (nullable forever).
- Identity: `audit_log_entries` (nullable forever).

Plus 8 EF Configuration edits + 8 aggregate `TenantId` property additions (no factory edits yet).

### Step 4 — Wave B: backfill (M, ~3h)

Eight 3b migrations. Notifications + Identity skipped per §1.6/§1.7. **Single deploy wave** so rollback is atomic.

### Step 5 — Aggregate factory + handler edits across Wave B/C modules (L, ~6h)

For each module: `Create` factory takes `tenantId` first; each command handler sources appropriately; update tests. Largest single chunk.

Key files: `PlaceBookingHandler` (derive from `property.TenantId`); `Booking.Place(...)` signature; `BookingHold.Create(...)`; `AvailabilityBlock.Create(...)` (flip from nullable to required); `CreatePaymentIntentForBookingHandler` (derive from `booking.TenantId`); + similar patterns for other 6 modules.

### Step 6 — Wave C NOT NULL flips (S, ~2h)

Seven 3c migrations (Notifications + Identity skipped). **Single deploy wave** following Step 5 deploy. CI gate per §5.2.

### Step 7 — Test pack + doc footers (M, ~3h)

- `TenantIdRolloutTests.cs` — per-module NULL-count assertions.
- `CrossModuleTenantIdInheritanceTests.cs` — guest places booking on tenant-A property; assert booking.TenantId == A.
- MTOP §10 row OPS.M.3 → shipped.
- MASTER_PLAN row OPS.M.3 → ✅.

---

## 13. What gets approved by this document

If you approve:
1. Plan commits as `docs/OPS_M_3_PLAN.md`.
2. **Step 1 + Step 2** ship together (Catalog dress rehearsal); merge before Wave A.
3. **Step 3 (Wave A)** ships as 8 commits in one or two batches; merge before Wave B.
4. **Step 4 (Wave B)** ships as 8 commits in one batch; merge before Step 5.
5. **Step 5 + Step 6** ship together (handler edits + Wave C NOT NULL flips); single deploy because app code change and DB constraint flip must co-deploy.
6. **Step 7** ships as a final commit closing out the slice.

If you reject or want changes: point at the specific §1.x decision, the §2 sequencing argument, or the §12 step that needs reshaping.
