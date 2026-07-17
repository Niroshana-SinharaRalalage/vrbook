# VRB-210 / 215 / 216 — Product-Settings Design (settings-schema REVIEW-REQ)

**Status:** REVIEW-REQ (owner/TL review before code). **Branch context:** develop + config-defect cluster (206/207/208/209). **Depends on:** VRB-211 (audit) landing first. **Unblocks:** PAY VRB-102/104/105 via the §3 contract.

> Authored by the system-architect (consult 2026-07-16) at AGENT-2's request, per the "consult architect for planning + commit the plan as a reviewable doc" working rule. Technical decisions are adopted directly (§6 lists them for TL visibility); the PRODUCT/POLICY questions in §6 are routed to the owner.

## 0. Scope & grounding

- **VRB-210** — admin settings UI shell (framework, validation, safe defaults, audit line, role-gated sections). Est L. `EPIC-configuration-settings.md:262-288`.
- **VRB-215** — cancellation/refund policy config, 2 owner-locked models (Q3/Q24), per-property, snapshot-per-line. Est L. `:381-403`.
- **VRB-216** — platform-global cancellation tiers + platform fee + tax posture (PlatformAdmin-only). Est M. `:405-425`.
- **VRB-211** — settings audit trail; reuses `identity.audit_log` + `AuditLogBehavior`. Dependency. `:290-311`.
- **PAY consumer** — VRB-102 cancellation-policy engine (`EPIC-launch-features.md:56-77`), blocked-by VRB-104; needs the §3 contract.

Owner-locked invariants honoured throughout (CLAUDE.md:40-68): admin surface Entra-local only (ADR-0016); role authority = `IsPlatformAdmin` + `HasTenantRole(tid,"tenant_admin")`; only permitted new role literal is `PlatformAdmin`; no `IsOwner`/`IsAdmin`.

---

## 1. Bounded-context + storage decision

### Decision: NO new "Settings" bounded context. Settings are owned by the domain that owns the values.

Rationale: the locked principle "*standardize the framework/machinery, localize the values per-property*" (CLAUDE.md:36) maps cleanly to **one shared UI/validation framework (frontend, VRB-210)** + **per-domain storage in the owning module**. A monolithic Settings context would duplicate aggregates (Property, PricingPlan, Tenant) and cross module boundaries (ADR-0001). VRB-216's own technical note names the Admin module as the "natural home" (`:418`).

| Concern | Owning module | Schema | RLS? |
|---|---|---|---|
| Platform-global cancellation tiers (VRB-216) | **Admin** (extend) | `admin` | No — platform-global, matches `AdminModule.cs:24-31` deliberate no-RLS |
| Platform fee default + per-tenant override (VRB-216) | **Admin** override table; canonical per-tenant value stays `identity.tenants.PlatformFeeBps` (`Tenant.cs:34`, default 1500) | `admin` + `identity` | fee override table platform-global (no RLS) |
| Tax posture / per-state facilitator enablement (VRB-216) | **Admin** | `admin` | No |
| Per-property cancellation-model selection (VRB-215) | **Catalog** (Property aggregate — tenant-scoped, existing RLS) | `catalog` | **Yes** (existing RLS pattern) |
| Per-booking policy **snapshot** (VRB-215/102) | **Booking** (line item) | `booking` | Yes |
| Audit trail (VRB-211) | **Identity** (reuse `identity.audit_log`) | `identity` | existing |

### 1a. Admin-module tables (VRB-216) — platform-global, no RLS

`AdminDbContext` registered as **plain** (non-tenant-scoped) DbContext — same pattern as `feature_flags` (`AdminModule.cs:27-31`, `AdminDbContext.cs:15-30`). Add:

```
admin.cancellation_tiers            -- single active row + version history (append, never mutate)
  id                uuid  pk
  version           int   not null   -- monotonic; latest = active
  first_tier_days   int   not null   -- full refund >= N1 days   (default 7)
  second_tier_days  int   not null   -- partial band lower bound  (default 2)
  middle_tier_pct   int   not null   -- % refunded in the band 0..100 (default 50)
  final_cutoff_hours int  not null   -- no refund < N3 hours      (default 48)
  upgrade_price_pct  int  not null   -- RefundableUpgrade price = pct % of subtotal (Q2; default e.g. 8)
  updated_by_user_id uuid not null
  updated_at        timestamptz not null
  -- CHECK: first_tier_days > second_tier_days; middle_tier_pct in [0,100]; upgrade_price_pct in [0,100]

admin.platform_fee_overrides         -- per-tenant fee override (default lives on identity.tenants.platform_fee_bps)
  tenant_id         uuid  pk
  platform_fee_bps  int   not null    -- [0,10000]
  updated_by_user_id uuid not null
  updated_at        timestamptz not null

admin.tax_posture                     -- singleton engine-config row (Stripe Tax marketplace-facilitator)
  id                uuid  pk
  facilitator_active bool not null default true
  per_state_json    jsonb not null default '{}'   -- {"CA":true,"NY":true,...} Q25 enablement
  updated_by_user_id uuid not null
  updated_at        timestamptz not null
```

Design note: tiers are **versioned append-only** rather than mutated so that a snapshot taken at booking Place can reference the version — reinforces VRB-216 AC "does not retroactively change already-snapshotted bookings" (`:412`) even though the primary snapshot guard is the copy-onto-booking (§3).

### 1b. Catalog per-property selection (VRB-215) — tenant-scoped, RLS

Extend the `catalog` schema (Property aggregate; tenant-scoped DbContext via `AddTenantScopedDbContext`, see `PricingModule.cs:20` + `RlsServiceCollectionExtensions.cs:55-78`). Add a per-property policy config (own table so it can be per-line-ready for Phase-3 cart, per `:390`):

```
catalog.property_cancellation_config
  property_id       uuid pk (fk property)
  tenant_id         uuid not null            -- RLS GUC column (app.current_tenant)
  model             int  not null            -- 0=Tiered, 1=RefundableUpgrade (host enables; no price input — Q2 corrected)
  updated_by_user_id uuid not null
  updated_at        timestamptz not null
  -- RLS policy mirrors OpsM9_Catalog_RlsPolicies
```

The RefundableUpgrade **price is platform-set** (`admin.cancellation_tiers.upgrade_price_pct`), not per-property — the host row only records the model choice.

The launch model set is **`{Tiered, RefundableUpgrade}`** (Q24), not the legacy `Flexible/Moderate/Strict` (`CancellationPolicyCode.cs:3-8`). VRB-102 explicitly replaces the old enum root (`EPIC-launch-features.md:65`). Recommend a new `CancellationModel` enum in Contracts and deprecating `CancellationPolicyCode` reads (arch-test RED-then-GREEN to prove no old-enum consumers remain).

### 1c. Booking snapshot (VRB-215/102)

Add `catalog`→`booking` at Place: the effective, **resolved** policy is copied onto the booking (per line item — `BookingLineItem`, `Booking.cs:123-125`). Additive migration on `booking` schema:

```
booking.line_item  (extend BookingLineItem)  -- or a sibling booking.line_cancellation_snapshot
  cancellation_model        int  not null
  resolved_first_tier_days  int  null
  resolved_second_tier_days int  null
  resolved_middle_tier_pct  int  null
  resolved_final_cutoff_hours int null
  refundable_upgrade_purchased bool not null default false
  refundable_upgrade_price_amount numeric(12,2) null
  tier_version              int  null       -- provenance back to admin.cancellation_tiers.version
```

This is the load-bearing immutability guarantee (VRB-215 `:389`, VRB-102 `:62`): later global-tier or property-config changes never mutate an in-flight booking.

### 1d. Audit (VRB-211) — reuse, do not rebuild

No new context/table. `AuditLogEntry` already carries `{ActorUserId, ActorRole, Action, TargetType, TargetId, Before, After, Ip, UserAgent, TraceId, TenantId, OccurredAt}` — a superset of the VRB-211 AC fields (`:294`). `AuditLogBehavior` already writes on success, writes `.failed` on throw (`:48-51`), resolves role without legacy literals (`:84-107`), never fails the handler. Settings commands implement `IAuditable` (`IAuditable.cs:13-23`) — the `SetFeatureFlagCommand.cs:16-22` precedent.

Two additions only:
1. **Redaction** for secret-valued keys (VRB-211 `:298`): keep secrets out of auditable command fields, or a `[Redacted]`-aware serializer (the behavior serializes the whole request, `AuditLogBehavior.cs:40`).
2. **Read query** `GetSettingsChangesQuery(section, tenantId?)` → the UI "Recent changes" panel. Lives in Identity, filtered by `Action` prefix + optional `TenantId`.

**Audit action naming convention (new):** `settings.<section>.<verb>` — e.g. `settings.cancellation.set-model`, `settings.platform.set-tiers`, `settings.platform.set-fee`. Mirrors `feature-toggle.set`.

---

## 2. API surface

### Auth pattern (ADR-0016 + M.15)
- **Tenant-admin** endpoints: `[Authorize]` on the controller + `HasTenantRole(tid,"tenant_admin")` at the handler. Tenant scoping via `ITenantScoped` + `TenantAuthorizationBehavior` + RLS. **No new `[Authorize(Roles=...)]`.**
- **Platform-admin** endpoints: `[Authorize(Roles="PlatformAdmin")]` — the one permitted literal, as `TogglesController` (`AdminController.cs:37`).

### Routes

Tenant-admin (`/api/v1/admin/settings/*`):
```
GET  /admin/settings/cancellation/{propertyId}      -> PropertyCancellationSettingsDto
PUT  /admin/settings/cancellation/{propertyId}      SetPropertyCancellationModelCommand (ITenantScoped, IAuditable)
GET  /admin/settings/changes?section=&propertyId=   -> IReadOnlyList<SettingsChangeDto>  (VRB-211 panel)
```

Platform-admin (`/api/v1/admin/platform/settings/*`):
```
GET  /admin/platform/settings/cancellation-tiers    -> GlobalCancellationTiersDto
PUT  /admin/platform/settings/cancellation-tiers    SetGlobalTiersCommand (IAuditable)
GET  /admin/platform/settings/platform-fee          -> PlatformFeeConfigDto (default + overrides)
PUT  /admin/platform/settings/platform-fee/{tenantId?} SetPlatformFeeCommand (IAuditable)
GET  /admin/platform/settings/tax-posture           -> TaxPostureDto
PUT  /admin/platform/settings/tax-posture           SetTaxPostureCommand (IAuditable)
```

### DTOs (in `VrBook.Contracts/Dtos`)
```csharp
public sealed record GlobalCancellationTiersDto(
    int FirstTierDays, int SecondTierDays, int MiddleTierRefundPct, int FinalCutoffHours,
    int Version, string? LastChangedBy, DateTimeOffset? LastChangedAt);

public sealed record PropertyCancellationSettingsDto(
    Guid PropertyId, CancellationModel Model,
    Money? RefundableUpgradePrice,               // model 1 only
    GlobalCancellationTiersDto ResolvedTiers,    // read-only echo for the "you get" preview
    string? LastChangedBy, DateTimeOffset? LastChangedAt);

public sealed record PlatformFeeConfigDto(int DefaultBps, IReadOnlyList<TenantFeeOverrideDto> Overrides);
public sealed record SettingsChangeDto(string Actor, string Action, string? Before, string? After, DateTimeOffset At);
```

### Validation + safe-default contract
- FluentValidation validators (precedent `SetFeatureFlagValidator`): tiers monotonic (`FirstTierDays > SecondTierDays`), `MiddleTierRefundPct ∈ [0,100]`, `PlatformFeeBps ∈ [0,10000]` (matches `SetPlatformFeeBps`, `Tenant.cs:181-188`), upgrade price non-negative + currency = tenant `DefaultCurrency`.
- **Safe defaults** (VRB-210 `:268`, VRB-216 `:419`): every field has a documented default — tiers 7/2/50/48, fee 1500 bps, model = Tiered. When a global-tiers row is absent, the provider returns the **config-seeded defaults** (`Cancellation:Tiers:*`, §3), never null/zero.
- **RFC-7807 mapping:** validation failures → `application/problem+json` with `type = ProblemTypes.Validation` and an `errors` dict keyed by field name — consumed directly by the web `ApiProblemError.problem.errors` (`client.ts:22`).

---

## 3. The 215/216 CONTRACT for PAY (HIGHEST PRIORITY)

**Goal:** let PAY build VRB-102/104/105 in parallel against stable types before SETTINGS ships the DB-backed impl. PAY consumes an **interface + DTO in `VrBook.Contracts`** — never a Settings-module type (ADR-0001).

### 3a. Reconcile the VRB-102 ↔ VRB-216 source-of-truth tension (technical decision — architect's call)

VRB-102 (`:72`) specifies config keys `Cancellation:Tiers:*` (defaults 7/2/50/48). VRB-216 (`:409`) specifies a **DB-backed, platform-admin-editable** table. Resolution: the **contract interface is the stable boundary**; the config keys become the **seed + fail-safe default**, and the DB table (VRB-216) is the runtime source once present.
- Ship `ICancellationTierProvider` in Contracts **now** (config-backed default impl reads `Cancellation:Tiers:*` — satisfies VRB-102's config AC + gives a working fallback).
- VRB-216 replaces the impl with a DB-backed one seeded from the same defaults.
- PAY's VRB-102 never changes when the swap happens.

### 3b. Contract definitions (new, in `VrBook.Contracts/Interfaces`)

```csharp
public interface ICancellationTierProvider {
    Task<GlobalCancellationTiers> GetActiveAsync(CancellationToken ct = default);
}
public sealed record GlobalCancellationTiers(
    int FirstTierDays, int SecondTierDays, int MiddleTierRefundPct, int FinalCutoffHours, int Version);

public interface ICancellationPolicyResolver {
    Task<CancellationPolicySnapshot> ResolveAsync(Guid propertyId, Guid tenantId, CancellationToken ct = default);
}
public sealed record CancellationPolicySnapshot(
    CancellationModel Model,
    int? FirstTierDays, int? SecondTierDays, int? MiddleTierRefundPct, int? FinalCutoffHours, int? TierVersion,
    bool RefundableUpgradePurchased, decimal? RefundableUpgradePriceAmount, string? RefundableUpgradePriceCurrency);

public enum CancellationModel { Tiered = 0, RefundableUpgrade = 1 }

public interface IPlatformFeeResolver {
    Task<int> GetFeeBpsAsync(Guid tenantId, CancellationToken ct = default);   // default 1500 -> override
}
```

- **Refund (VRB-102/104):** `RefundForBookingHandler` reads the **snapshot from the booking line**, computes the tiered/upgrade refund, passes the resolved amount into the existing fee-reversal path (`RefundForBookingCommand.cs:80,158-163`). VRB-104 owns fee-reversal; VRB-102 owns amount resolution.
- **Fee at booking:** fold the per-tenant override into Identity's `TenantStripeContextLookup` so `TenantStripeContext.PlatformFeeBps` stays PAY's single fee read (`CreatePaymentIntentForBookingHandler.cs:70`) — **no PAY code change**. Expose `IPlatformFeeResolver` only for the settings UI read.
- **Tax:** `ITaxCalculator` exists (`StubTaxCalculator` → zero). VRB-216 owns only the **posture read-model** (`ITaxPostureProvider` → `TaxPosture(bool FacilitatorActive, IReadOnlyDictionary<string,bool> PerStateEnabled)`). PAY VRB-103 swaps in the Stripe-Tax engine independently.

### 3c. Cross-module vs internal
- **Contracts (stable, cross-module):** `ICancellationTierProvider`, `ICancellationPolicyResolver`, `CancellationPolicySnapshot`, `CancellationModel`, `IPlatformFeeResolver`, `ITaxPostureProvider`, `TaxPosture`.
- **Internal to Admin:** DB entities + `Set*Command` handlers + `AdminDbContext` config.
- **Internal to Catalog:** `PropertyCancellationConfig` + `SetPropertyCancellationModelCommand`.
- **Internal to Booking:** snapshot columns + Place-time resolve call.

**PAY VRB-102 can start the moment the Contracts interfaces + `CancellationPolicySnapshot` land** — even against a config-backed stub.

---

## 4. Frontend framework (VRB-210)

New `web/src/components/settings/` + `web/src/app/admin/settings/**`. Behind `Features:Admin.SettingsUi` (VRB-203, shipped).

- **`SettingsLayout`** — section sidebar (collapses < md, closes mobile-nav gap G19), page title, sticky save/discard bar. Wrapped by existing `AdminAuthGuard` (Entra-local admin only, ADR-0016).
- **`useSettingsForm`** — dirty tracking, discard-to-pristine, optimistic save via TanStack Query + `apiFetch`. On `ApiProblemError`, map `problem.errors` onto per-field errors; save bar shows error count; focus first invalid field (WCAG 2.2 AA).
- **`SafeDefault`** — shows the documented default as placeholder/prefill, persisted only on confirm.
- **Audit line** — "last changed by X at T" + a "Recent changes" panel from `GET /admin/settings/changes` (VRB-211).
- **Role-gated sections** — tenant-admins see tenant-scoped only; platform sections hidden + 403-guarded server-side.
- **Destructive confirm** — switching cancellation model opens a focus-trapped modal (closes G33).

New shared primitives (buttons/inputs/cards/`FieldError`/`SaveBar`) seed the component library (G20). Tests: Vitest `SettingsLayout`/`useSettingsForm`/`SafeDefault` + Playwright `admin-settings-shell.e2e.ts`.

---

## 5. Build sequence + parallelization

Critical objective: **unblock PAY (VRB-102) soonest** → the Contracts boundary lands first.

**Phase A — the contract:**
1. **VRB-211 core** — `GetSettingsChangesQuery` + `settings.<section>.<verb>` `IAuditable` convention + redaction hook (reuses `AuditLogBehavior`). Lands first (every settings command emits audit).
2. **Contracts interfaces + DTOs** — the §3 types, with a **config-backed default impl** (`Cancellation:Tiers:*`). RED arch test: "no consumer reads legacy `CancellationPolicyCode`". → **PAY can now claim VRB-102.**

**Phase B — platform settings (VRB-216, backend-first):**
3. Admin tables + migrations seeded 7/2/50/48/1500. Swap config-backed provider → DB-backed (PAY untouched). Fold fee override into `TenantStripeContextLookup`.
4. Platform API + `[Authorize(Roles="PlatformAdmin")]` controllers + validators.

**Phase C — UI shell (VRB-210, parallel with A/B):**
5. `SettingsLayout`/`useSettingsForm`/primitives/audit panel (RED-then-GREEN against the API contract).

**Phase D — per-property + snapshot (VRB-215, the join):**
6. Catalog `property_cancellation_config` + `SetPropertyCancellationModelCommand` (tenant-scoped, RLS, `IAuditable`).
7. Booking snapshot columns + Place-time `ICancellationPolicyResolver.ResolveAsync` → copy resolved policy onto the line.
8. Web `admin/settings/cancellation/**` + `admin/platform/settings/**`.

**Fork point:** after A.2, **PAY (VRB-102) ∥ SETTINGS-backend (VRB-216) ∥ WEB (VRB-210)** run concurrently. VRB-215 is the join.

**RED-then-GREEN arch tests:** legacy-enum removal; "every settings command is `IAuditable`"; "platform-settings endpoints are `PlatformAdmin`-gated".

---

## 6. Owner decisions (RESOLVED 2026-07-16) + remaining questions

### PRODUCT / POLICY — OWNER-ANSWERED
1. **Tier numbers → FULLY CONFIGURABLE.** Owner: *"No hard-and-fast rule — this should be configurable and the parameters changeable at any time."* → Confirms the DB-backed, platform-admin-editable `admin.cancellation_tiers` design. **7/2/50/48 is the SEED default only** (not a locked value); the PUT endpoint + versioned rows are the point. No further owner input needed to build.
2. **Refundable-upgrade pricing → PLATFORM-SET % OF SUBTOTAL.** *(Corrected 2026-07-17 — the TL's direct owner-ask is authoritative; the owner delegated the call to the TL.)* One platform-admin-editable rate **`upgrade_price_pct`** (VRB-216, on the platform config row) applied to the booking subtotal. Hosts do **not** set an amount — they only **enable/disable** the RefundableUpgrade model per property (VRB-215). The upgrade amount is **computed at Place** (`upgrade_price_pct × subtotal`) and stored on the booking-line snapshot (`refundable_upgrade_price_amount`), so PAY reads a concrete amount. No per-property price columns; no cap (the single platform % is the price).
3. **Fee visibility → SHOW FEE % + NET.** `Property/PayoutSettingsDto` exposes both the platform fee % and the host's net; the settings/payout UI renders "Platform fee: N% — your net: $X". No hidden-fee mode.
4. **Model availability → BOTH MODELS, ALL TENANTS.** No per-tenant gating; **drop the per-tenant allow-flag** — every host freely picks Tiered or RefundableUpgrade. Simplifies VRB-215 (no gating column/check).

### REMAINING (non-blocking; sensible default applied)
5. **Per-state tax roster (Q25).** Not asked of the owner — the tax ENGINE is PAY VRB-103, and VRB-216 only owns the posture read-model. **Default applied:** seed `admin.tax_posture` with `facilitator_active = true` + **empty** `per_state_json` (operator fills the enabled-state roster at go-live). Revisit with the owner when VRB-103/go-live tax config lands.

### TECHNICAL (architect-decided, recorded for TL visibility)
- No new Settings context; settings owned per-domain (§1).
- Global tiers DB-backed in Admin; config keys are seed/fallback; the Contracts interface is the stable PAY boundary (§3a).
- Per-tenant fee override folded into `TenantStripeContextLookup` — PAY fee read unchanged (§3b).
- Policy snapshot copied onto the booking line at Place; tiers version-stamped for provenance (§1a/§1c).
- Legacy `CancellationPolicyCode {Flexible,Moderate,Strict}` → `CancellationModel {Tiered,RefundableUpgrade}`, enforced by a RED-then-GREEN arch test (§1b/§5).
- Audit reuses `identity.audit_log` + `AuditLogBehavior`; additions = redaction hook + `GetSettingsChangesQuery` (§1d).
- **Owner-driven adjustments (2026-07-16, Q2 corrected 2026-07-17):** upgrade price = **platform-set `upgrade_price_pct` × subtotal** (computed at Place, stored on the snapshot; hosts only enable the model — no per-property price/cap); **no per-tenant model gating**; fee shown to hosts (DTO exposes fee % + net). **Additive contract change for Phase B:** add `UpgradePricePct` to `GlobalCancellationTiers` (non-breaking for PAY) so Place-time can compute the amount.

### TECHNICAL (architect-decided, recorded for TL visibility)
- No new Settings context; settings owned per-domain (§1).
- Global tiers DB-backed in Admin; config keys are seed/fallback; the Contracts interface is the stable PAY boundary (§3a).
- Per-tenant fee override folded into `TenantStripeContextLookup` — PAY fee read unchanged (§3b).
- Policy snapshot copied onto the booking line at Place; tiers version-stamped for provenance (§1a/§1c).
- Legacy `CancellationPolicyCode {Flexible,Moderate,Strict}` → `CancellationModel {Tiered,RefundableUpgrade}`, enforced by a RED-then-GREEN arch test (§1b/§5).
- Audit reuses `identity.audit_log` + `AuditLogBehavior`; additions = redaction hook + `GetSettingsChangesQuery` (§1d).
