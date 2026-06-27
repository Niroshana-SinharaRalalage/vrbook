# OPS.M.5 — Stripe Connect Express + Connect-Aware Payment Routing (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.5 ("Account.create, AccountLink.create, webhook routing, PaymentIntent transfer_data").
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §4 lines 95-117 (the brief verbatim).
**ADR**: `docs/adr/0003-stripe-connect-express.md` (Express vs Standard vs Custom — Express ratified).
**Predecessors**: Slice OPS.M.1 (Tenant + `stripe_account_id`/`platform_fee_bps` columns), Slice OPS.M.3 (Payment `TenantId` non-nullable on `PaymentIntent`/`Refund`; `WebhookEvent.TenantId` nullable forever per §1.4), Slice OPS.M.4 closed `a0f58f8` (`ITenantScoped` + behavior + 13 events carry `TenantId`).
**Sequence**: After Slice OPS.M.4; before Slice OPS.M.6 (per-tenant Redis hold key — independent), Slice OPS.M.7 (onboarding wizard UI — consumer of M.5's API), Slice OPS.M.8 (Super Admin — consumer of `SetTenantPlatformFeeBpsCommand`), Slice OPS.M.9 (RLS — read-side defense on top of M.5's tenant-id stamping on webhook events).
**Estimate**: **5 days, not 4** (one-engineer). MTOP line 95's 4-day estimate is light by one day — see §8. The hidden cost the brief flagged ("24-48h KYC") is calendar, not engineer-time, and is not in the estimate; staging Connect uses test-mode `acct_*` IDs that bypass KYC.

This plan is the contract. Slice OPS.M.5 ships **Connect onboarding API + Connect-aware PaymentIntent routing + per-account webhook idempotency + `Tenant` Stripe readiness lifecycle**. Onboarding UI is Slice OPS.M.7; Super Admin override UI is Slice OPS.M.8; both explicitly out (§5).

---

## 1. Scope summary

Slice OPS.M.5 produces seven deliverables, one per Step in §9:

| # | Deliverable | Touches | Net code change |
|---|---|---|---|
| 1 | Schema: `webhook_events.stripe_account_id` (nullable) + composite unique `(stripe_event_id, stripe_account_id)`; `tenants.charges_enabled`/`payouts_enabled` bool cols | 1 Payment migration, 1 Identity migration | +2 cols + 2 cols, 1 unique-index swap |
| 2 | `Tenant` aggregate — `UpdateStripeAccountReadiness(bool, bool)` + auto-Activate when both true; `TenantStripeOnboarded`/`TenantStripeSuspended` events | `Tenant.cs:139`, `TenantEvents.cs` | +2 methods, +2 events |
| 3 | `StripeGateway` — `CreateConnectAccountAsync`, `CreateAccountLinkAsync`, `CreateLoginLinkAsync` + Connect-aware `CreatePaymentIntentAsync` overload + `refund_application_fee` flag on Refund | `StripeGateway.cs`, `IStripeGateway.cs` | +3 methods, +2 params |
| 4 | `ITenantStripeContextLookup` cross-module contract + Identity implementation | `src/VrBook.Contracts/Interfaces/`, `Identity/Infrastructure/` | new file + new impl |
| 5 | `OnboardTenantStripeCommand` + `GenerateStripeAccountLinkCommand` + `OpenStripeLoginLinkCommand` + `SetTenantPlatformFeeBpsCommand` + Identity controller endpoints | `Identity/Application/Tenants/Commands/`, `IdentityController.cs` | +4 commands, +4 endpoints |
| 6 | `CreatePaymentIntentForBookingHandler` rewrite — `TransferData`+`ApplicationFeeAmount`+`OnBehalfOf`; `HandleStripeWebhookCommand` rewrite — parse `account` field, per-account routing, type-specific dispatch; `RefundForBookingHandler` rewrite — `refund_application_fee` math + negative-balance guard | 3 handlers in Payment | substantial rewrite |
| 7 | Payment event payload `TenantId` bump (`PaymentAuthorized`/`PaymentCaptured`/`PaymentFailed`/`RefundIssued`); deferred from Slice OPS.M.4 | `PaymentEvents.cs` + raise sites | +1 param × 4 records |

**Net behavioral change**: every PaymentIntent on a tenant whose `StripeAccountStatus = Active` routes funds destination=connected-account with a 15%-default platform fee; webhooks dispatch per-connected-account with `(event_id, account_id)` idempotency; refunds proportionally reverse the application fee and hard-block partial refunds that would push the connected account negative.

### 1.1 What OPS.M.3/M.4 left for M.5 to clean up

1. **`WebhookEvent.SetTenantId(Guid)` exists at `WebhookEvent.cs:43`** but is never called; M.5 wires the first caller (§3.7).
2. **`CreatePaymentIntentForBookingHandler.cs:60-70` `ResolveTenantIdAsync` raw-SQL cross-schema lookup + default-tenant fallback** — replaced by `ITenantStripeContextLookup` in Step 4 (§3.4). The `?? new Guid("…0001")` widening also dies.
3. **Payment events still have no `Guid TenantId`** — OPS.M.4 §4 deferred to M.5. Step 7.

---

## 2. Sequencing — schema, aggregate, gateway/contracts, commands, handler rewrites, events, tests

**Recommended: Steps 1→7 strictly in order.** Reasons:
1. **Schema first** — Step 6 webhook handler depends on the new `stripe_account_id` column existing, and Step 2 aggregate depends on the two new bool columns existing. Migration is purely additive (no Connect data yet); zero backfill risk.
2. **Aggregate + events next** — Step 6 webhook handler raises `TenantStripeOnboarded`/`TenantStripeSuspended`; the contract must exist before consumers can subscribe. Decoupled from gateway/contracts so they can parallelize.
3. **Gateway + cross-module contract** (Steps 3, 4) — both feed Steps 5+6. Independent of each other; can interleave commits.
4. **Onboarding commands (Step 5)** — depends on Steps 2-4. Ships endpoints; UI is Slice OPS.M.7.
5. **Handler rewrites (Step 6)** — the load-bearing change. Depends on Steps 2-4. Co-deploys with Step 7 because the rewritten PaymentIntent handler raises events with the new `TenantId` field (positional ctor).
6. **Event payload bump (Step 7)** — co-deploys with Step 6 single tag. Same atomic-deploy constraint as OPS.M.4 §4 used (outbox replay mid-deploy on a record-ctor mismatch crashes deserialization).
7. **Integration test pack last** — sees the final shape.

**Total wave count: 5 deploys.** Step 1 ships alone. Steps 2 + 7 co-tag (event/raise atomicity). Steps 3 + 4 co-tag (contract pair). Step 5 alone. Step 6 alone, after 5 is in.

---

## 3. Design decisions

### 3.1 D1 — Express, confirmed

**Decision: Express, no change.** `docs/adr/0003-stripe-connect-express.md` already ratified it; nothing in the codebase or Phase 3 reconnaissance argues otherwise. Express gives us the Stripe-hosted onboarding (no KYC UI build), Stripe-hosted payouts dashboard (no payouts UI build), and acceptable Phase 1.5 negative-balance liability per MTOP §4.

### 3.2 D2 — Onboarding trigger: at first-listing-published

**Decision: at first-listing-published (MTOP framing locked).** Drafts pre-KYC are allowed; publishing requires `Tenant.StripeAccountStatus = Active`. Rationale: drafts have zero financial exposure; publish is the first moment a booking can be placed. Alternatives rejected:
- **Sign-up trigger** ships KYC friction before the user knows if they'll list. Highest abandonment.
- **Save-as-draft trigger** front-loads KYC before any commercial intent.
- **First-booking trigger** racks up booking-attempt failures during KYC's 24-48h window.

**Promoted to §7 Q1** (judgment call): the alternative arguments could swing if user research data changes.

### 3.3 D3 — Two commands, not one

**Decision: split into `OnboardTenantStripeCommand` (Account.create, idempotent) + `GenerateStripeAccountLinkCommand` (AccountLink.create, re-runnable).** Stripe AccountLinks expire after **5 minutes**; users will refresh the page, hit "resume onboarding," etc. A single combined command would re-`Account.create` on every refresh (wasteful, idempotency-key churn) or carry a stale link. Split lets the controller call only `GenerateStripeAccountLink` on re-visit. Also: `OpenStripeLoginLinkCommand` (Account dashboard for completed onboarding) lives in the same slot as a third command. All three implement `ITenantScoped`.

### 3.4 D4 — `ITenantStripeContextLookup`, option (a)

**Decision: option (a) — new `ITenantStripeContextLookup` in `src/VrBook.Contracts/Interfaces/`.** Returns `(Guid TenantId, string? StripeAccountId, int PlatformFeeBps, string DefaultCurrency)`. Identity-side implementation reads from `IdentityDbContext.Tenants`. Rejected: (b) overloading `IPropertyOwnerLookup` muddies it (booking-side concept, not payment-side); (c) raw SQL inside Payment handler is the pattern we're *deleting* in this step; (d) caller-side stamping is too coupled. The lookup returns a value-type record; Phase 4's `tenant_connect_accounts` (per Phase 3 recon) will replace the implementation without changing the contract — the return shape stays "single Stripe destination per (tenant, booking)" because a booking still resolves to one supplier even in the multi-supplier topology.

### 3.5 D5 — Empty Stripe-account behavior: throw (staging flag)

**Decision: option (a) — throw `BusinessRuleViolationException("payment.connect_account_missing", …)`.** A booking reaching `CreatePaymentIntentForBookingHandler` with `Tenant.StripeAccountId is null` is a contract violation: listing-publish should have gated this. Defensive throw + a 422 from the API + a structured log lets us detect the upstream bug fast. Falling back to platform-account hides the bug and creates an audit-trail mess (charges go to platform, but `payment_intents.tenant_id` says tenant X — irreconcilable on reporting).

**Promoted to §7 Q2** because staging needs a relaxation knob: feature-flag `Payment:AllowPlatformFallback=true` on staging only, default false. The flag short-circuits to today's single-account path. Production: flag absent (defaults false).

### 3.6 D6 — Refund routing + negative-balance guard

**Decision: `refund_application_fee=true` for full refunds; proportional for partials.**

Math (decimal arithmetic, banker's rounding):
- `applicationFeeAmount = round(capturedAmount × PlatformFeeBps / 10_000, 2)`
- For a refund of amount `r ≤ capturedAmount`:
  - Full refund (`r == capturedAmount`): pass `refund_application_fee=true` (reverses the entire fee).
  - Partial (`r < capturedAmount`): compute `feeReversal = round(r × PlatformFeeBps / 10_000, 2)`; pass `refund_application_fee=true` + explicit `application_fee_refund=feeReversal` on the RefundCreateOptions.
- **Negative-balance guard** (the MTOP §4 invariant): a refund must not push the connected account balance negative. The exact Stripe-side balance isn't known at validation time, so we apply the local sufficient condition: `r - feeReversal ≤ capturedAmount - applicationFeeAmount`. Algebraically this is always true with proportional reversal, so the guard fires only when caller passes an **explicit override fee amount** (admin manual refund path) or when prior partial refunds have already drawn down the connected balance. Track cumulative reversed amounts on `Refund` rows for the same `PaymentIntentId`.
- Exception type: **new `NegativeBalanceRefundException : BusinessRuleViolationException`** (subclass for greppability + telemetry filter, same pattern as OPS.M.4 §3.3 `CrossTenantAccessException`). Carries `attemptedRefund`, `availableConnectedBalance`, `paymentIntentId`.

### 3.7 D7 — Webhook routing + idempotency, option (a)

**Decision: option (a) — add `stripe_account_id` (nullable string) to `webhook_events`; flip unique constraint to `(stripe_event_id, stripe_account_id)`** (nullable side: NULL is a distinct value, which matches platform-level events). Stripe-side reasoning: per Connect docs, an event affecting a connected account is delivered twice (once at platform scope with `account=null`, once at connected scope with `account=acct_…`); same `evt_…` ID. Option (b)'s "trust Stripe's per-account uniqueness" would block the second delivery as a duplicate. Migration: 1 column added + drop `IX_webhook_events_stripe_event_id` + create composite unique. Backfill N/A (no Connect data exists).

**Event-type routing — M.5 explicitly handles:**

| Event | Action |
|---|---|
| `account.updated` | Lookup tenant by `account.id`; call `Tenant.UpdateStripeAccountReadiness(charges_enabled, payouts_enabled)`; aggregate auto-Activates per §3.8 |
| `account.application.deauthorized` | Flip `Tenant.StripeAccountStatus = Suspended`; raise `TenantStripeSuspended` |
| `payment_intent.succeeded` | Existing handler (Status → Succeeded, raise `PaymentCaptured`) — no behavior change beyond reading from per-account event |
| `payment_intent.payment_failed` | Existing handler — no behavior change |
| `payment_intent.canceled` | Existing handler — no behavior change |
| `charge.refunded` | Update `Refund.Status` for the matching `StripeRefundId` |
| `payout.paid` / `payout.failed` | Log + persist row only; no domain action Phase 1.5 (observability seed for the Super Admin payouts view in Slice OPS.M.8) |

Everything else: log + persist; ignore.

### 3.8 D8 — `Tenant` Stripe readiness, option (a)

**Decision: option (a) — two new bool columns `ChargesEnabled`/`PayoutsEnabled` on `Tenant`.** New aggregate method:

```csharp
public void UpdateStripeAccountReadiness(bool chargesEnabled, bool payoutsEnabled)
{
    ChargesEnabled = chargesEnabled;
    PayoutsEnabled = payoutsEnabled;
    if (chargesEnabled && payoutsEnabled && Status == StatusPendingOnboarding)
    {
        Status = StatusActive;
        StripeAccountStatus = "Active";
        Raise(new TenantStripeOnboarded(Id, StripeAccountId!));
        Raise(new TenantActivated(Id));   // existing event, free downstream wiring
    }
    else if ((!chargesEnabled || !payoutsEnabled) && Status == StatusActive)
    {
        // Stripe revoked capabilities (e.g. KYC info expired). Operator-only re-Activate.
        Status = StatusSuspended;
        SuspendedReason = "stripe_capability_lost";
        Raise(new TenantStripeSuspended(Id, StripeAccountId!, "stripe_capability_lost"));
    }
}
```

`AssignStripeAccount(string)` at `Tenant.cs:133` stays as the initial-creation hook (sets `StripeAccountId`, status stays `PendingOnboarding`). The free-text `UpdateStripeAccountStatus(string)` at line 139 stays as an escape hatch for operator scripts but is no longer called from the M.5 handler path. Option (b) parse-from-string was rejected as ugly; (c) value-object was rejected as overkill for two booleans.

### 3.9 D9 — Payment events: bump now

**Decision: bump `PaymentAuthorized`/`PaymentCaptured`/`PaymentFailed`/`RefundIssued` with `Guid TenantId` (positional, leading position to match OPS.M.4's pattern).** `DisputeOpened` is deferred — no consumer that needs tenant scope ships in M.5 (auto-respond is Phase 2 per MTOP §11).

**New events introduced by M.5:**
- `TenantStripeOnboarded(Guid TenantId, string StripeAccountId)` — fires when `UpdateStripeAccountReadiness` transitions to Active.
- `TenantStripeSuspended(Guid TenantId, string StripeAccountId, string Reason)` — fires on capability loss or deauthorization.

Both land in `src/VrBook.Contracts/Events/TenantEvents.cs`.

### 3.10 D10 — `SetTenantPlatformFeeBpsCommand`: ship in M.5

**Decision: ship the command in M.5; UI ships in Slice OPS.M.8.** The aggregate method exists (`Tenant.cs:145`); a thin `SetTenantPlatformFeeBpsCommand` + Identity controller endpoint costs ~1 engineer-hour. Slice OPS.M.8 will wire the Super Admin UI to call it; until then, it's reachable via the API for operator scripts (the value proposition is "Super Admin override at month X for tenant Y" — operator script suffices for Phase 1.5). The command implements `ITenantScoped` but is also tagged on the §10 Q3 `PlatformAdminCommands` allowlist from OPS.M.4 §10 Q3 (a Super Admin acting on a tenant they don't belong to). Until Slice OPS.M.8 lights up the `IsPlatformAdmin` carve-out, the command is dormant — any caller without `currentUser.TenantId == targetTenantId` gets `CrossTenantAccessException`. Acceptable: M.5 ships the contract surface, M.8 lights it up. **Promoted to §7 Q3.**

### 3.11 D11 — Stripe.net 47.x supports Connect

**Decision: no SDK upgrade.** `src/Directory.Packages.props` pins `Stripe.net 47.0.0`; Connect Account, AccountLink, LoginLink, and `TransferData`/`ApplicationFeeAmount`/`OnBehalfOf` on PaymentIntent have all been first-class since v40. Verified against `docs/adr/0003-stripe-connect-express.md`.

### 3.12 D12 — Onboarding return + refresh URLs

**Decision: option (c), pragmatic — config-driven URLs pointing at the M.7 wizard route.** Add `StripeOptions.OnboardingReturnUrl` + `StripeOptions.OnboardingRefreshUrl`. Staging values: `https://staging.vrbook.app/account/onboarding/complete` / `…/refresh`. M.5 ships the config keys + reads them in `CreateAccountLinkAsync`; Slice OPS.M.7 ships the actual wizard pages at those routes. Bicep already exposes the static web app domain so no infra change. Option (a) standalone page = Slice OPS.M.7's job; (b) postponing the URLs means M.5 can't be exercised end-to-end on staging before M.7 (unacceptable — Connect onboarding is the load-bearing Phase 1.5 demo). The return-URL handler doesn't need to *do* anything in M.5 beyond returning 200; the `account.updated` webhook drives state transitions.

---

## 4. Per-module checklist (Payment as worked example)

1. **Step 1 (schema)**: 1 migration adds `stripe_account_id varchar(120) NULL` + drops unique index `IX_webhook_events_stripe_event_id` + creates unique `IX_webhook_events_account_event` on `(stripe_event_id, stripe_account_id)`. Per Postgres semantics NULL is distinct so platform-scope rows don't collide.
2. **Step 2 (aggregate)**: Identity-side — `Tenant.UpdateStripeAccountReadiness`. Payment-side no edit.
3. **Step 3 (gateway)**: `IStripeGateway` gains `CreateConnectAccountAsync(email, country)`, `CreateAccountLinkAsync(accountId, returnUrl, refreshUrl)`, `CreateLoginLinkAsync(accountId)`, and an overloaded `CreatePaymentIntentAsync` that accepts `(string destinationAccountId, long applicationFeeAmount)`. `RefundAsync` gains `(bool refundApplicationFee, decimal? applicationFeeRefund)`.
4. **Step 4 (cross-module)**: Payment depends on `ITenantStripeContextLookup` (Contracts). Identity implements.
5. **Step 5 (commands)**: Identity-side, not Payment.
6. **Step 6 (handlers)**: `CreatePaymentIntentForBookingHandler.cs` — lookup → throw if `StripeAccountId is null` (unless flag) → call Connect-overload of `CreatePaymentIntentAsync` → pass `applicationFeeAmount = round(amount × bps / 10_000, 2)`. `HandleStripeWebhookCommand.cs` — extract `account` field from event JSON; lookup tenant; call `wh.SetTenantId(tenantId)`; dispatch with the per-event-type table from §3.7. `RefundForBookingHandler.cs` — compute `feeReversal` per §3.6; apply guard; invoke `RefundAsync` with new params.
7. **Step 7 (events)**: `PaymentEvents.cs` — add `Guid TenantId` as leading positional param to 4 records; raise sites in `PaymentIntent.cs` pass `TenantId`.
8. **Tests**: `tests/VrBook.Api.IntegrationTests/Payment/ConnectRoutingTests.cs` (4 scenarios — happy path with active Connect tenant, throws on null Stripe account, partial refund proportional reversal, negative-balance guard fires); `tests/VrBook.Api.IntegrationTests/Payment/WebhookPerAccountIdempotencyTests.cs` (3 scenarios — platform+account dual delivery distinct rows, replay no-op, tenant-id stamped on the account-scope row).

Identity module: Step 1 migration (tenants bool cols), Step 2 aggregate, Step 5 commands + 3 controller endpoints + 1 endpoint for `SetTenantPlatformFeeBps`.

Booking module: zero edits (already raises events with `TenantId` from OPS.M.4).

---

## 5. Non-goals (explicitly OUT of Slice OPS.M.5)

| Item | Owner slice | Why deferred |
|---|---|---|
| Onboarding UI wizard | Slice OPS.M.7 | M.5 ships the API + the static config URL targets; M.7 builds the pages |
| Super Admin override UI for platform fee | Slice OPS.M.8 | Command lands in M.5 (§3.10); UI in M.8 |
| `connect_account_kind` enum on `tenants` | Phase 4 / Slice 10 | Phase 3 reconnaissance ratified: it's a relationship (`tenant_connect_accounts`), not a kind |
| `tenant_connect_accounts` relationship table | Phase 4 / Slice 10 | YAGNI — Phase 1.5 is one Connect account per tenant |
| Payouts UI | Never (use Stripe-hosted) | Express dashboard via `CreateLoginLinkAsync` magic-link |
| Multi-currency conversion | Phase 2 | One currency per tenant (`Tenant.DefaultCurrency` is the authority) |
| Stripe disputes auto-evidence | Phase 2 | MTOP §11 |
| Tighten `WebhookEvent.TenantId` to `Guid` non-null | Never | Per OPS.M.3 §1.4 — platform-level events legitimately have no tenant |
| `DisputeOpened` event `TenantId` bump | Later | No consumer in M.5 |
| Webhook event-type handlers for `transfer.*`, `balance.available`, etc. | Phase 2 | Persist + ignore for now |
| Tighten OPS.M.4 §3.5 `IsPlatformAdmin` claim | Slice OPS.M.8 | Stays dormant |

---

## 6. Scope-cut order (drop top first if 5-day budget bites)

1. **`SetTenantPlatformFeeBpsCommand` + endpoint (§3.10)** — operator can `UPDATE identity.tenants SET platform_fee_bps = …` directly until Slice OPS.M.8 needs the API.
2. **`OpenStripeLoginLinkCommand`** — tenants can navigate to dashboard.stripe.com manually (the login link is convenience).
3. **`payout.paid`/`payout.failed` log-only handlers** — pure observability seed; Slice OPS.M.8 will add when the Super Admin payouts view needs them.
4. **`account.application.deauthorized` handler** — rare in practice; manual `UPDATE` covers it short-term.
5. **`TenantStripeOnboarded`/`TenantStripeSuspended` events as new contracts** — fire `TenantActivated`/`TenantSuspended` instead (reuse existing events). Loses semantic clarity but downstream wiring is identical.

Never falls: schema migration; `UpdateStripeAccountReadiness`; `ITenantStripeContextLookup`; `OnboardTenantStripeCommand`+`GenerateStripeAccountLinkCommand`; the three handler rewrites in Step 6; negative-balance guard in refund; the 4 payment-event payload bumps; the connect-routing integration test pack.

---

## 7. Open questions for user

D1, D3, D4, D6-D9, D11, D12 are decided above. Three judgment calls promoted here.

### Q1. Onboarding trigger: first-listing-published (D2)?

§3.2 lock: **first-listing-published.** Default holds. Cost of being wrong: if "at sign-up" were correct, owners hit KYC friction before commercial intent (~20% extra abandonment per industry benchmark) — but a code change to flip the trigger is one IF in `PublishPropertyHandler` calling `OnboardTenantStripeCommand` directly. Cheap to revisit post-Phase-1.5 from telemetry. Reversible.

### Q2. Empty Stripe-account fallback: throw with staging flag (D5)?

§3.5 lock: **throw `BusinessRuleViolationException`; staging-only `Payment:AllowPlatformFallback` flag.** Cost of being wrong: in production, a single unset config in a tenant's Stripe row turns paying customers into 422s instead of silently routing to platform. Mitigation: Slice OPS.M.7 onboarding wizard hard-gates Publish on `StripeAccountStatus = Active`; a 422 here means upstream gate failed (real bug, want it loud).

### Q3. `SetTenantPlatformFeeBpsCommand` slot — M.5 or M.8 (D10)?

§3.10 lock: **M.5 (command + endpoint); M.8 (UI).** Cost of being wrong if punted to M.8: operator needs `UPDATE identity.tenants` access for the one tenant per month who negotiates a custom rate. Acceptable but ugly (raw SQL on prod). Cost if shipped in M.5 unused: ~1h engineer time; endpoint sits dormant until M.8.

---

## 8. Estimate basis — why 5 days, not 4

- Step 1 (1 Payment migration + 1 Identity migration, additive): ~2h.
- Step 2 (`UpdateStripeAccountReadiness` + 2 new event records + EF config for bool cols + unit tests for the auto-transition logic): ~3h.
- Step 3 (`IStripeGateway` 4 new methods, real Stripe-SDK calls with `Account.CreateAsync`/`AccountLinkService`/`LoginLinkService`/`AccountId` on RequestOptions + `RefundCreateOptions.RefundApplicationFee`/`ApplicationFeeRefund` wiring): ~5h.
- Step 4 (`ITenantStripeContextLookup` interface + Identity impl + DI registration in `IdentityModule`): ~2h.
- Step 5 (3 onboarding commands + `SetTenantPlatformFeeBpsCommand` + 4 Identity controller endpoints + DTOs + `ITenantScoped` markers + per-command unit tests): ~5h.
- Step 6 (`CreatePaymentIntentForBookingHandler` rewrite ~3h; `HandleStripeWebhookCommand` rewrite — event-type dispatch table, account-id extraction, tenant lookup, `SetTenantId` call ~4h; `RefundForBookingHandler` rewrite with proportional fee math + guard + cumulative-prior-refunds tracking ~3h): ~10h.
- Step 7 (4 payment-event payload bumps + raise-site updates at 4 sites in `PaymentIntent.cs` + retest event-consumer handlers): ~2h.
- Integration test pack (4 connect-routing scenarios + 3 webhook-idempotency scenarios + 2 onboarding-flow scenarios): ~6h.
- Doc footers + ADR `0003` cross-reference + close-out: ~2h.

Total: ~37 engineer-hours = **~5 single-engineer working days at sustainable pace**. MTOP's 4d underestimate misses Steps 4, 7, and the refund math.

---

## 9. Implementation step list

### Step 1 — Schema migrations (S, ~2h)
1 Payment migration `OpsM5a_Payment_WebhookEvents_StripeAccountId` + 1 Identity migration `OpsM5a_Identity_Tenants_StripeReadiness`. Both purely additive; deploy alone.

### Step 2 — Tenant aggregate + new events (S, ~3h)
Append `ChargesEnabled`/`PayoutsEnabled` to `Tenant.cs`; add `UpdateStripeAccountReadiness` per §3.8. Add `TenantStripeOnboarded`/`TenantStripeSuspended` to `TenantEvents.cs`. EF config for the bool columns. **Co-deploys with Step 7** under one tag (events come online together).

### Step 3 — `StripeGateway` Connect surface (M, ~5h)
`IStripeGateway` gains the 4 new methods per §3.3 + §3.6. Implementation uses `Stripe.AccountService`, `Stripe.AccountLinkService`, `Stripe.LoginLinkService`. The Connect-aware `CreatePaymentIntentAsync` overload passes `Stripe.PaymentIntentCreateOptions { TransferData = new() { Destination = destinationAccountId }, ApplicationFeeAmount = applicationFeeAmount, OnBehalfOf = destinationAccountId }`. Refund gains `RefundApplicationFee` + optional `ApplicationFeeRefund` (cents).

### Step 4 — `ITenantStripeContextLookup` (S, ~2h)
New interface in `src/VrBook.Contracts/Interfaces/ITenantStripeContextLookup.cs` returning `(Guid TenantId, string? StripeAccountId, int PlatformFeeBps, string DefaultCurrency)?`. Identity-side implementation in `src/Modules/VrBook.Modules.Identity/Infrastructure/TenantStripeContextLookup.cs` reads from `IdentityDbContext.Tenants`. DI registration in `IdentityModule`. **Co-tags with Step 3.**

### Step 5 — Onboarding commands + endpoints (M, ~5h)
- `OnboardTenantStripeCommand(Guid TenantId) : IRequest<string accountId>, ITenantScoped` — `Account.create` (Express, country from `Tenant.DefaultCurrency`-derived default), persist `Tenant.AssignStripeAccount(id)`.
- `GenerateStripeAccountLinkCommand(Guid TenantId) : IRequest<string url>, ITenantScoped` — `AccountLink.create` using `StripeOptions.OnboardingReturnUrl`/`RefreshUrl`.
- `OpenStripeLoginLinkCommand(Guid TenantId) : IRequest<string url>, ITenantScoped` — for active tenants only.
- `SetTenantPlatformFeeBpsCommand(Guid TenantId, int Bps) : IRequest<Unit>, ITenantScoped` — dormant until Slice OPS.M.8 carve-out lights up.
- 4 endpoints on `IdentityController`.

### Step 6 — Handler rewrites (L, ~10h)
`CreatePaymentIntentForBookingHandler.cs` — delete the raw-SQL `ResolveTenantIdAsync` block at lines 60-70; inject `ITenantStripeContextLookup`; throw `BusinessRuleViolationException` per §3.5 when account missing (modulo staging flag); call Connect overload. `HandleStripeWebhookCommand.cs` — extract `event.account` (JSON), tenant lookup by `stripe_account_id`, set `wh.SetTenantId(...)`, dispatch per §3.7 table; idempotency check uses both `stripe_event_id` and `stripe_account_id`. `RefundForBookingHandler.cs` — proportional fee math per §3.6, negative-balance guard, new `NegativeBalanceRefundException`.

### Step 7 — Payment events `TenantId` bump (S, ~2h)
4 event records gain leading `Guid TenantId` positional param; 4 raise sites in `PaymentIntent.cs` pass `TenantId`. **Co-tags with Step 2** for atomic-deploy.

---

## 10. What gets approved by this document

If you approve:
1. Plan commits as `docs/OPS_M_5_PLAN.md`.
2. **Step 1** ships alone (schema-only).
3. **Steps 2 + 7** co-tag (Tenant aggregate + payment event payloads — event/raise atomicity).
4. **Steps 3 + 4** co-tag (gateway surface + cross-module contract — they ship together because Step 6 consumes both).
5. **Step 5** ships alone (onboarding endpoints + dormant SetFee command).
6. **Step 6** ships last alone (handler rewrites — sees all of 2/3/4/5/7 already in).
7. Integration test pack ships as the close-out commit.

If you reject or want changes: point at the specific §3.x decision, §2 sequencing argument, §7 open Q resolution, or §9 step that needs reshaping. The two highest-impact decisions to challenge first: §3.5 (D5 throw-vs-fallback) and §3.7 (D7 webhook idempotency key shape) — getting either wrong on Phase 1.5 production data is expensive to reverse.

---

## 11. Close-out — TBD

### Per-step commit ledger

| Step | Module(s) | Commit | Files touched |
|---|---|---|---|
| 1 | Payment + Identity | _pending_ | 2 migrations; webhook_events `stripe_account_id` col + composite unique; tenants `charges_enabled`/`payouts_enabled` cols |
| 2 + 7 | Identity + Contracts + Payment | _pending_ | `Tenant.UpdateStripeAccountReadiness`; `TenantStripeOnboarded`/`TenantStripeSuspended` events; `PaymentAuthorized/Captured/Failed/RefundIssued` get leading `Guid TenantId`; 4 raise sites in `PaymentIntent.cs` updated |
| 3 + 4 | Payment + Contracts + Identity | _pending_ | `IStripeGateway` gains Connect methods + Connect-PI overload + refund flags; `StripeGateway` impl; `ITenantStripeContextLookup` interface + Identity impl + DI |
| 5 | Identity + Api | _pending_ | 4 commands + ITenantScoped markers + 4 IdentityController endpoints + DTOs |
| 6 | Payment | _pending_ | `CreatePaymentIntentForBookingHandler` rewrite (raw-SQL fallback deleted); `HandleStripeWebhookCommand` rewrite (per-account dispatch); `RefundForBookingHandler` rewrite (proportional fee + negative-balance guard) + `NegativeBalanceRefundException` |
| Tests | Api.IntegrationTests | _pending_ | `ConnectRoutingTests` (4 facts), `WebhookPerAccountIdempotencyTests` (3 facts), `OnboardingFlowTests` (2 facts) |

### Deviations from this plan
_TBD at close-out._

### Forward links
- **Slice OPS.M.6** — per-tenant Redis hold key (independent; no link).
- **Slice OPS.M.7** — onboarding wizard UI consumes `OnboardTenantStripeCommand`/`GenerateStripeAccountLinkCommand`; lives at the URLs M.5 configured in §3.12.
- **Slice OPS.M.8** — Super Admin lights up `IsPlatformAdmin`; the `SetTenantPlatformFeeBpsCommand` dormant in M.5 becomes reachable.
- **Slice OPS.M.9** — RLS on `payment.payment_intents`/`refunds`/`webhook_events`. The `webhook_events.tenant_id` nullable stays nullable; M.9's policy must allow NULL-tenant rows.
- **Phase 4 / Slice 10** — `tenant_connect_accounts` relationship table. `ITenantStripeContextLookup`'s implementation swaps; the interface contract is stable.
