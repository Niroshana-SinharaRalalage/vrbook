# OPS.M.5 — Stripe Connect Express + Connect-Aware Payment Routing (Plan, rev 2)

**Status**: Proposed — awaiting user review. **Revision 2** supersedes commit `4095075`; the user pushed back on rev 1's §7 ("don't bounce technical questions to me — make the architect decide") and added a TDD + best-practices directive.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.5 ("Account.create, AccountLink.create, webhook routing, PaymentIntent transfer_data").
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §4 lines 95-117 (the brief verbatim).
**ADR**: `docs/adr/0003-stripe-connect-express.md` (Express ratified).
**Predecessors**: Slice OPS.M.1, Slice OPS.M.3, Slice OPS.M.4 closed `a0f58f8`.
**Sequence**: After Slice OPS.M.4; before Slice OPS.M.6 (Redis hold key — independent), Slice OPS.M.7 (onboarding wizard UI), Slice OPS.M.8 (Super Admin), Slice OPS.M.9 (RLS).
**Estimate**: **5 days, one engineer** — TDD-first, see §8.

This plan is the contract. Slice OPS.M.5 ships **Connect onboarding API + Connect-aware PaymentIntent routing + per-account webhook idempotency + `Tenant` Stripe readiness lifecycle**. Onboarding UI is Slice OPS.M.7; Super Admin override UI is Slice OPS.M.8; both out (§5).

---

## 1. Scope summary

Slice OPS.M.5 produces seven deliverables, one per Step in §9:

| # | Deliverable | Touches |
|---|---|---|
| 1 | Schema: `webhook_events.stripe_account_id` (nullable) + composite unique `(stripe_event_id, stripe_account_id)`; `tenants.charges_enabled`/`payouts_enabled` | 1 Payment migration, 1 Identity migration |
| 2 | `Tenant.UpdateStripeAccountReadiness` + auto-Activate; `TenantStripeOnboarded`/`TenantStripeSuspended` events | `Tenant.cs:139`, `TenantEvents.cs` |
| 3 | `StripeGateway` Connect surface: `CreateConnectAccountAsync`, `CreateAccountLinkAsync`, `CreateLoginLinkAsync`, Connect-aware `CreatePaymentIntentAsync` overload, refund flags | `StripeGateway.cs`, `IStripeGateway.cs` |
| 4 | `ITenantStripeContextLookup` cross-module contract + Identity impl | `src/VrBook.Contracts/Interfaces/`, `Identity/Infrastructure/` |
| 5 | `OnboardTenantStripeCommand`, `GenerateStripeAccountLinkCommand`, `OpenStripeLoginLinkCommand`, `SetTenantPlatformFeeBpsCommand` + 4 endpoints | `Identity/Application/Tenants/Commands/`, `IdentityController.cs` |
| 6 | Handler rewrites: `CreatePaymentIntentForBookingHandler`, `HandleStripeWebhookCommand`, `RefundForBookingHandler` | 3 handlers in Payment |
| 7 | Payment event payload `TenantId` bump (`PaymentAuthorized`/`Captured`/`Failed`/`RefundIssued`) | `PaymentEvents.cs` + raise sites |

### 1.1 What OPS.M.3/M.4 left for M.5 to clean up

1. `WebhookEvent.SetTenantId(Guid)` exists at `WebhookEvent.cs:43` but is unused; M.5 wires the first caller (§3.7).
2. `CreatePaymentIntentForBookingHandler.cs:60-70` raw-SQL `ResolveTenantIdAsync` + `?? new Guid("…0001")` widening — deleted in Step 6.
3. Payment events still missing `Guid TenantId` — bumped in Step 7.

---

## 2. Sequencing

Steps 1→7 in order. Waves (5 deploys):

1. **Step 1** alone (schema).
2. **Steps 2 + 7** co-tag (events come online with their raise sites — outbox replay constraint, identical to OPS.M.4 §4).
3. **Steps 3 + 4** co-tag (gateway + cross-module contract are consumed jointly by Step 6).
4. **Step 5** alone (commands, dormant SetFee).
5. **Step 6** alone (handler rewrites — sees 2/3/4/5/7 in).

---

## 3. Design decisions

### 3.1 D1 — Express, confirmed

**Decision: Express, no change.** `docs/adr/0003-stripe-connect-express.md` already ratified it; nothing in the codebase or Phase 3 reconnaissance argues otherwise. Express gives us the Stripe-hosted onboarding (no KYC UI build), Stripe-hosted payouts dashboard (no payouts UI build), and acceptable Phase 1.5 negative-balance liability per MTOP §4.

### 3.2 D2 — Onboarding trigger: at first-listing-published (locked, was rev 1 §7 Q1)

See §3.14 for the full justification — the rev 1 promotion of this to a user-resolvable question was wrong; it's a Stripe-funnel call.

### 3.3 D3 — Two commands, not one

**Decision: split into `OnboardTenantStripeCommand` (Account.create, idempotent) + `GenerateStripeAccountLinkCommand` (AccountLink.create, re-runnable).** Stripe AccountLinks expire after **5 minutes**; users will refresh the page, hit "resume onboarding," etc. A single combined command would re-`Account.create` on every refresh (wasteful, idempotency-key churn) or carry a stale link. Split lets the controller call only `GenerateStripeAccountLink` on re-visit. Also: `OpenStripeLoginLinkCommand` (Account dashboard for completed onboarding) lives in the same slot as a third command. All three implement `ITenantScoped`.

### 3.4 D4 — `ITenantStripeContextLookup`, option (a)

**Decision: option (a) — new `ITenantStripeContextLookup` in `src/VrBook.Contracts/Interfaces/`.** Returns `(Guid TenantId, string? StripeAccountId, int PlatformFeeBps, string DefaultCurrency)`. Identity-side implementation reads from `IdentityDbContext.Tenants`. Rejected: (b) overloading `IPropertyOwnerLookup` muddies it (booking-side concept, not payment-side); (c) raw SQL inside Payment handler is the pattern we're *deleting* in this step; (d) caller-side stamping is too coupled. The lookup returns a value-type record; Phase 4's `tenant_connect_accounts` (per Phase 3 recon) will replace the implementation without changing the contract.

### 3.5 D5 — Empty Stripe-account behavior: throw (locked, was rev 1 §7 Q2)

See §3.15 for the full justification.

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
        Status = StatusSuspended;
        SuspendedReason = "stripe_capability_lost";
        Raise(new TenantStripeSuspended(Id, StripeAccountId!, "stripe_capability_lost"));
    }
}
```

`AssignStripeAccount(string)` at `Tenant.cs:133` stays as the initial-creation hook. The free-text `UpdateStripeAccountStatus(string)` at line 139 stays as an escape hatch for operator scripts but is no longer called from the M.5 handler path.

### 3.9 D9 — Payment events: bump now

**Decision: bump `PaymentAuthorized`/`PaymentCaptured`/`PaymentFailed`/`RefundIssued` with `Guid TenantId` (positional, leading position to match OPS.M.4's pattern).** `DisputeOpened` is deferred — no consumer that needs tenant scope ships in M.5 (auto-respond is Phase 2 per MTOP §11).

**New events introduced by M.5:**
- `TenantStripeOnboarded(Guid TenantId, string StripeAccountId)` — fires when `UpdateStripeAccountReadiness` transitions to Active.
- `TenantStripeSuspended(Guid TenantId, string StripeAccountId, string Reason)` — fires on capability loss or deauthorization.

Both land in `src/VrBook.Contracts/Events/TenantEvents.cs`.

### 3.10 D10 — `SetTenantPlatformFeeBpsCommand`: ship in M.5 (locked, was rev 1 §7 Q3)

See §3.16 for the full justification.

### 3.11 D11 — Stripe.net 47.x supports Connect

**Decision: no SDK upgrade.** `src/Directory.Packages.props` pins `Stripe.net 47.0.0`; Connect Account, AccountLink, LoginLink, and `TransferData`/`ApplicationFeeAmount`/`OnBehalfOf` on PaymentIntent have all been first-class since v40.

### 3.12 D12 — Onboarding return + refresh URLs

**Decision: config-driven URLs pointing at the M.7 wizard route.** Add `StripeOptions.OnboardingReturnUrl` + `StripeOptions.OnboardingRefreshUrl`. Staging values: `https://staging.vrbook.app/account/onboarding/complete` / `…/refresh`. M.5 ships the config keys + reads them in `CreateAccountLinkAsync`; Slice OPS.M.7 ships the actual wizard pages at those routes. The return-URL handler doesn't need to *do* anything in M.5 beyond returning 200; the `account.updated` webhook drives state transitions.

### 3.13 D13 — Best-practices contract (locked)

The implementation is required to meet the eight rules in §10. They are not negotiable per-PR; deviating requires a §11 close-out entry. Summary: explicit `Stripe.RequestOptions.IdempotencyKey` on every Stripe write, Polly retry for transient `StripeException`, signature-verified webhooks, `decimal` + banker's rounding for currency, stable error `Code` strings, structured Serilog tenant/booking/account/event fields, no ambient state in the gateway, nullable-aware boundaries, and four arch tests (commands `ITenantScoped`, no direct Stripe SDK use outside the gateway, dispatch-table enumeration, event positional-ctor shape).

### 3.14 D14 — Onboarding trigger is first-listing-published (was rev 1 §7 Q1)

**Locked: first-listing-published.** This is a Stripe-semantics + funnel-engineering call, not a product/value call. Sign-up trigger forces KYC friction before any commercial intent (industry benchmark: ~20% incremental abandonment); save-as-draft front-loads the same friction; first-booking trigger racks up booking-attempt failures inside the 24-48h KYC window. First-listing-published is the first moment a real booking can be placed and the first moment we know the tenant is commercial. The trigger flip is one IF in `PublishPropertyHandler` if telemetry later contradicts. **No user open question.** If the product team later wants a different funnel shape based on observed conversion data, they raise it then; for OPS.M.5 the implementation locks here.

### 3.15 D15 — Empty Stripe-account behavior: throw, with staging-only flag (was rev 1 §7 Q2)

**Locked: throw `BusinessRuleViolationException("payment.connect_account_missing", …)`; `Payment:AllowPlatformFallback` is a staging-only escape valve, default false, absent in production.** Routing-to-platform when a tenant's `StripeAccountId` is null is a silent audit-trail corruption (charges on platform, `payment_intents.tenant_id = X`, irreconcilable on reporting). The OPS.M.7 onboarding wizard hard-gates Publish on `StripeAccountStatus = Active`; a 422 here means the upstream gate failed and we want it loud. The staging flag exists so QA can exercise the booking path on staging before M.7 wizard pages exist; the flag's absence in production is enforced by the §10 best-practices arch test that asserts no environment-keyed override of `Payment:AllowPlatformFallback` ships in `appsettings.Production.json`. **Not a product question.**

### 3.16 D16 — `SetTenantPlatformFeeBpsCommand` ships in M.5 dormant (was rev 1 §7 Q3)

**Locked: command + endpoint in M.5; reachable only after OPS.M.8 lights up `IsPlatformAdmin`.** Cost of shipping dormant: ~1h engineer. Cost of punting to M.8: a tenant negotiating a custom rate in the M.5→M.8 gap is handled by `UPDATE identity.tenants` on prod (ugly, audit-log-bypassing). Shipping the contract surface now without the call site is a standard .NET/CQRS pattern — the alternative leaks raw SQL into operator runbooks. Cross-tenant attempts before M.8 throw `CrossTenantAccessException` (per OPS.M.4 §3.3). **Not a product question.**

---

## 4. Per-module checklist (Payment as worked example)

1. **Step 1 (schema)**: 1 migration adds `stripe_account_id varchar(120) NULL` + drops unique index `IX_webhook_events_stripe_event_id` + creates unique `IX_webhook_events_account_event` on `(stripe_event_id, stripe_account_id)`. Per Postgres semantics NULL is distinct so platform-scope rows don't collide.
2. **Step 2 (aggregate)**: Identity-side — `Tenant.UpdateStripeAccountReadiness`. Payment-side no edit.
3. **Step 3 (gateway)**: `IStripeGateway` gains `CreateConnectAccountAsync(email, country)`, `CreateAccountLinkAsync(accountId, returnUrl, refreshUrl)`, `CreateLoginLinkAsync(accountId)`, and an overloaded `CreatePaymentIntentAsync` that accepts `(string destinationAccountId, long applicationFeeAmount)`. `RefundAsync` gains `(bool refundApplicationFee, decimal? applicationFeeRefund)`.
4. **Step 4 (cross-module)**: Payment depends on `ITenantStripeContextLookup` (Contracts). Identity implements.
5. **Step 5 (commands)**: Identity-side, not Payment.
6. **Step 6 (handlers)**: rewrites per §3.5/§3.6/§3.7.
7. **Step 7 (events)**: `PaymentEvents.cs` — add `Guid TenantId` as leading positional param to 4 records; raise sites in `PaymentIntent.cs` pass `TenantId`.

Identity module: Step 1 migration (tenants bool cols), Step 2 aggregate, Step 5 commands + 3 controller endpoints + 1 endpoint for `SetTenantPlatformFeeBps`.

Booking module: zero edits (already raises events with `TenantId` from OPS.M.4).

---

## 5. Non-goals (explicitly OUT of Slice OPS.M.5)

| Item | Owner slice | Why deferred |
|---|---|---|
| Onboarding UI wizard | Slice OPS.M.7 | M.5 ships the API + the static config URL targets; M.7 builds the pages |
| Super Admin override UI for platform fee | Slice OPS.M.8 | Command lands in M.5 (§3.16); UI in M.8 |
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

1. **`SetTenantPlatformFeeBpsCommand` + endpoint (§3.16)** — operator can `UPDATE identity.tenants SET platform_fee_bps = …` directly until Slice OPS.M.8 needs the API.
2. **`OpenStripeLoginLinkCommand`** — tenants can navigate to dashboard.stripe.com manually.
3. **`payout.paid`/`payout.failed` log-only handlers** — pure observability seed; Slice OPS.M.8 will add when the Super Admin payouts view needs them.
4. **`account.application.deauthorized` handler** — rare in practice; manual `UPDATE` covers it short-term.
5. **`TenantStripeOnboarded`/`TenantStripeSuspended` events as new contracts** — fire `TenantActivated`/`TenantSuspended` instead.

Never falls: schema migration; `UpdateStripeAccountReadiness`; `ITenantStripeContextLookup`; `OnboardTenantStripeCommand`+`GenerateStripeAccountLinkCommand`; the three handler rewrites in Step 6; negative-balance guard in refund; the 4 payment-event payload bumps; the connect-routing integration test pack; all four §10 arch tests.

---

## 7. (Removed in rev 2.)

Rev 1 §7 ("Open questions for user") deleted. Q1/Q2/Q3 promoted to §3.14/§3.15/§3.16 with conviction. **No user-resolvable technical questions remain.** If the user disagrees with §3.14/§3.15/§3.16 on a product-value basis, they raise it against the §3 row; otherwise the locks hold.

---

## 8. Estimate basis — 5 days, TDD-shaped

Tests-first work re-baselines the per-step hours: test packs land before implementation, so the totals below include both.

- Step 1: ~3h (schema + 2 information_schema fixtures)
- Step 2: ~4h (6 transition facts + aggregate impl + 2 events + EF config)
- Step 3: ~6h (6 gateway behavior facts + Polly retry + idempotency-key helpers + 4 new methods + PI overload + refund flags)
- Step 4: ~3h (4 lookup facts + interface + impl + DI)
- Step 5: ~6h (4 command test classes + endpoint facts + arch-test extension + 4 commands + 4 endpoints)
- Step 6: ~12h (3 handler test classes — ~15 facts total + 3 handler rewrites + `NegativeBalanceRefundException` + dispatch table)
- Step 7: ~3h (arch test + 4 record bumps + 4 raise sites + consumer-handler test re-runs)
- Close-out: ~2h

Total ~39h ≈ **5 single-engineer working days**. The TDD overhead vs rev 1's ~37h (~2h) is recouped in Step 6 — pinning the contract before rewriting three handlers eliminates the ad-hoc "is this still passing the existing tests?" loop.

---

## 9. Implementation step list — **Tests → Min impl → Refactor**

Every step is red-first. Red commit + green commit are tracked in the §11 ledger.

### Step 1 — Schema migrations (S, ~3h)

**Tests (red first)**
- `tests/VrBook.Api.IntegrationTests/Payment/WebhookEventsSchemaTests.cs` — three `[Fact]`s against `information_schema`: column `stripe_account_id` exists with `data_type='character varying'` and `is_nullable='YES'`; old single-column `IX_webhook_events_stripe_event_id` is gone; composite unique `IX_webhook_events_account_event` on `(stripe_event_id, stripe_account_id)` is present. Same fixture pattern as `TenantIdRolloutTests`.
- `tests/VrBook.Api.IntegrationTests/Identity/TenantStripeReadinessSchemaTests.cs` — two `[Fact]`s: `tenants.charges_enabled` and `tenants.payouts_enabled` exist, `boolean NOT NULL DEFAULT false`.

**Min implementation**: `OpsM5a_Payment_WebhookEvents_StripeAccountId` + `OpsM5a_Identity_Tenants_StripeReadiness`. Additive only.

**Refactor**: none expected; if the migration generator emits drop-and-recreate for the unique index, hand-author the `RenameIndex` form for replay-safety.

### Step 2 — `Tenant.UpdateStripeAccountReadiness` (S, ~4h) **co-tag with Step 7**

**Tests (red first)** — `tests/VrBook.Identity.UnitTests/Domain/TenantStripeReadinessTests.cs`, one `[Fact]` per transition:
- `PendingOnboarding_with_both_flags_true_transitions_to_Active_and_raises_TenantStripeOnboarded_and_TenantActivated`
- `Active_with_charges_lost_transitions_to_Suspended_with_reason_stripe_capability_lost_and_raises_TenantStripeSuspended`
- `Active_with_payouts_lost_transitions_to_Suspended`
- `PendingOnboarding_with_only_charges_true_remains_PendingOnboarding_no_events`
- `Active_with_both_flags_true_is_no_op_no_events`
- `Suspended_cannot_auto_re_Activate_from_flags_alone` (operator-only path)

**Min implementation**: aggregate method per §3.8 + 2 event records in `TenantEvents.cs` + EF config for the two bool columns.

**Refactor**: extract `StatusActive`/`StatusPendingOnboarding`/`StatusSuspended` constants if not present.

### Step 3 — `StripeGateway` Connect surface (M, ~6h) **co-tag with Step 4**

**Tests (red first)** — `tests/VrBook.Payment.UnitTests/Infrastructure/StripeGatewayConnectTests.cs`, behavior tests against a fake `StripeClient` (request capture) or Stripe test-mode:
- `CreatePaymentIntent_with_connect_account_sets_TransferData_Destination_ApplicationFeeAmount_OnBehalfOf` — captures the outgoing `PaymentIntentCreateOptions`, asserts all three fields equal the values passed in, and asserts `RequestOptions.IdempotencyKey` follows the `booking:{bookingId:N}:pi` shape.
- `CreateConnectAccountAsync_uses_idempotency_key_tenant_onboarding_{tenantId}` — asserts idempotency-key format.
- `RefundAsync_full_refund_sets_RefundApplicationFee_true_and_no_explicit_ApplicationFeeRefund`.
- `RefundAsync_partial_refund_sets_RefundApplicationFee_true_and_explicit_ApplicationFeeRefund_cents`.
- `Transient_StripeException_retries_three_times_with_backoff_then_succeeds` — fake throws `StripeException(IsRetriable=true)` twice then returns OK; assert exactly 3 attempts logged with `attempt_count` field.
- `Non_retriable_StripeException_does_not_retry`.

**Min implementation**: 4 new methods on `IStripeGateway`; overloaded `CreatePaymentIntentAsync(…, string destinationAccountId, long applicationFeeAmount, …)`; `RefundAsync` gains `(bool refundApplicationFee, decimal? applicationFeeRefund)`. Polly retry pipeline (§10).

**Refactor**: pull idempotency-key construction into a private `static class StripeIdempotency` to make Step 5/6 callers compile-time match the format.

### Step 4 — `ITenantStripeContextLookup` (S, ~3h) **co-tag with Step 3**

**Tests (red first)** — `tests/VrBook.Identity.IntegrationTests/TenantStripeContextLookupTests.cs`:
- `Returns_expected_record_shape_for_existing_tenant` (TenantId, StripeAccountId, PlatformFeeBps, DefaultCurrency).
- `Returns_null_when_tenant_absent`.
- `Reads_PlatformFeeBps_from_tenants_table_not_default` — seeded row with `platform_fee_bps=2000`, asserts 2000 returned.
- `Reads_StripeAccountId_null_when_unassigned`.

**Min implementation**: interface in Contracts; impl reads from `IdentityDbContext.Tenants`; DI registration in `IdentityModule`.

**Refactor**: none expected.

### Step 5 — Onboarding commands + endpoints (M, ~6h)

**Tests (red first)** — per command, unit test class `tests/VrBook.Identity.UnitTests/Application/{Command}Tests.cs`:
- For each of `OnboardTenantStripeCommand`, `GenerateStripeAccountLinkCommand`, `OpenStripeLoginLinkCommand`, `SetTenantPlatformFeeBpsCommand`:
  - `Implements_ITenantScoped`.
  - `Calls_expected_gateway_method_with_TenantId_passed_as_arg`.
  - `Returns_expected_DTO`.
- Controller-level: `tests/VrBook.Api.IntegrationTests/Identity/StripeOnboardingEndpointsTests.cs` — `Endpoint_stamps_TenantId_from_currentUser` (4 facts, one per endpoint).
- Arch test: extend `tests/VrBook.Architecture.Tests/TenantScopedCommandTests.cs` with the M.5 marker-list. New `[Fact]` `All_M5_onboarding_commands_implement_ITenantScoped`.

**Min implementation**: 4 commands + 4 IdentityController endpoints + DTOs.

**Refactor**: none expected.

### Step 6 — Handler rewrites (L, ~12h)

This is the load-bearing change. Each handler gets its contract pinned before any edit.

**Tests (red first)** —
- `tests/VrBook.Payment.UnitTests/Application/CreatePaymentIntentForBookingHandlerTests.cs`:
  - `Throws_payment_connect_account_missing_when_lookup_returns_null_StripeAccountId`.
  - `Throws_when_lookup_returns_null` (whole row missing).
  - `Does_not_use_raw_SQL_resolves_tenant_via_ITenantStripeContextLookup` (DI-substituted lookup is called exactly once; raw `NpgsqlCommand` calls = 0 — assert via mock infra).
  - `Passes_destinationAccountId_equal_to_lookup_StripeAccountId_to_gateway`.
  - `Passes_applicationFeeAmount_equal_to_round_amount_times_bps_div_10000_banker_rounding`.
  - `Staging_flag_AllowPlatformFallback_true_short_circuits_to_legacy_path`.
- `tests/VrBook.Payment.UnitTests/Application/HandleStripeWebhookCommandTests.cs`:
  - `Extracts_account_from_event_json_and_calls_SetTenantId_on_webhook_event`.
  - `Per_event_type_dispatch_table_fires_expected_handler` (one fact per event type in §3.7).
  - `Duplicate_event_id_for_same_account_id_is_noop` (idempotency).
  - `Same_event_id_different_account_id_persists_as_distinct_row` (platform + connected dual delivery).
  - `Signature_verification_runs_before_any_deserialization_or_db_write` (mock `EventUtility` throws → no row written).
- `tests/VrBook.Payment.UnitTests/Application/RefundForBookingHandlerTests.cs`:
  - `Full_refund_passes_RefundApplicationFee_true_and_no_explicit_amount`.
  - `Partial_refund_passes_proportional_ApplicationFeeRefund_cents_per_formula`.
  - `Over_refund_attempt_throws_BusinessRuleViolationException_payment_over_refund`.
  - `Negative_balance_guard_fires_NegativeBalanceRefundException_with_attemptedRefund_availableConnectedBalance_paymentIntentId`.
  - `Cumulative_prior_partial_refunds_are_summed_when_evaluating_guard`.

**Min implementation**: rewrites per §3.5/§3.6/§3.7; raw-SQL `ResolveTenantIdAsync` deleted; per-event-type dispatch table; `NegativeBalanceRefundException` class.

**Refactor**: extract `WebhookDispatchTable` into a `static readonly` field so the §10 arch test can reflect on it.

### Step 7 — Payment events `TenantId` bump (S, ~3h) **co-tag with Step 2**

**Tests (red first)** —
- `tests/VrBook.Architecture.Tests/PaymentEventTenantIdShapeTests.cs`: `Every_payment_event_positional_ctor_has_Guid_TenantId_at_position_0` — reflects on `PaymentAuthorized`, `PaymentCaptured`, `PaymentFailed`, `RefundIssued`.
- Existing event-consumer handler tests under `tests/VrBook.*.UnitTests` re-run; failures here pin the raise-site updates needed in `PaymentIntent.cs`.

**Min implementation**: positional `Guid TenantId` added to 4 records; 4 raise sites in `PaymentIntent.cs` updated.

**Refactor**: none expected.

---

## 10. Best-practices contract (D13 detail)

Every M.5 PR must satisfy these. Arch tests enforce items marked **[arch]**; code review enforces the rest.

1. **Idempotency keys** — every Stripe write passes explicit `Stripe.RequestOptions.IdempotencyKey`. Per-call format:
   - `Account.create` → `tenant-onboarding:{tenantId:D}`.
   - `AccountLink.create` → no idempotency key (returns a fresh 5-minute link each call by design).
   - `PaymentIntent.create` → existing `booking:{bookingId:N}:pi` (`StripeGateway.cs:38`).
   - `Refund.create` → `refund:{refundId:N}` keyed on domain `Refund.Id`.
2. **Retry policy** — every Stripe SDK call wrapped in Polly retry-with-exponential-backoff for `StripeException` where `IsRetriable` (transient 5xx, 429). 3 attempts, 250ms / 750ms / 2s. `attempt_count` logged. Non-retriable passes through.
3. **Webhook signature verification** — `EventUtility.ConstructEvent(…)` with the platform signing secret runs BEFORE any deserialization or DB write. The existing path in `HandleStripeWebhookCommand.cs` already does this; the Step 6 rewrite must not reorder it.
4. **Decimal arithmetic** — all currency math uses `decimal` with `MidpointRounding.ToEven`. No `double`/`float`. `long cents` only at the Stripe boundary.
5. **Error envelope** — every handler-thrown domain exception carries a stable `Code` string for telemetry. New codes for M.5: `payment.connect_account_missing`, `payment.negative_balance_refund`, `payment.over_refund`. Use existing `BusinessRuleViolationException(rule, message)`.
6. **Logging fields** — every Stripe-side call logs structured Serilog: `tenant_id`, `booking_id` (when present), `stripe_account_id`, `stripe_event_id` (when present), `attempt_count`, `idempotency_key`.
7. **No ambient state** — gateway methods accept `Stripe.RequestOptions` as a parameter; nothing read from a static.
8. **Null-safety at boundaries** — every cross-module DTO returns nullable explicitly; consumers check + throw or fall back. No `!`-suppression (per OPS.M.4 §14 deviation 6).

**Arch tests [arch]**:
- (a) Every M.5 command implements `ITenantScoped` — `TenantScopedCommandTests`.
- (b) Every Stripe API call goes through `IStripeGateway` — `NoDirectStripeSdkUsageOutsideGatewayTests` reflects on `Stripe.*Service.CreateAsync` callers and asserts the call set ⊆ `StripeGateway.cs`.
- (c) Every webhook event type listed in §3.7 has a `[Fact]` in `HandleStripeWebhookCommandTests` — adding a sixth event without registering it fails CI.
- (d) Every Payment event positional ctor has leading `Guid TenantId` — `PaymentEventTenantIdShapeTests`.

**Single-tag deploys** for Steps 2+7 and Steps 3+4 already called out in §2. Re-verified for Steps 5+6: Step 5 commands do NOT raise new events that Step 6 handlers consume (Step 5 raises only `TenantStripeOnboarded`/`Suspended` from the aggregate; Step 6 consumes only existing booking events and Stripe webhook payloads). Independent deploys safe.

---

## 11. Close-out — TBD

### Per-step commit ledger

| Step | Module(s) | Red commit (tests fail) | Green commit (impl) | Files touched |
|---|---|---|---|---|
| 1 | Payment + Identity | _pending_ | _pending_ | `WebhookEventsSchemaTests`, `TenantStripeReadinessSchemaTests`; 2 migrations |
| 2 + 7 | Identity + Contracts + Payment | _pending_ | _pending_ | `TenantStripeReadinessTests`, `PaymentEventTenantIdShapeTests`; aggregate + 2 events + 4 event-payload bumps + 4 raise sites |
| 3 + 4 | Payment + Contracts + Identity | _pending_ | _pending_ | `StripeGatewayConnectTests`, `TenantStripeContextLookupTests`; 4 gateway methods + Connect PI overload + refund flags + lookup interface + impl |
| 5 | Identity + Api + Architecture | _pending_ | _pending_ | 4 command-test classes, `StripeOnboardingEndpointsTests`, `TenantScopedCommandTests` extension; 4 commands + 4 endpoints |
| 6 | Payment | _pending_ | _pending_ | `CreatePaymentIntentForBookingHandlerTests`, `HandleStripeWebhookCommandTests`, `RefundForBookingHandlerTests`, `NoDirectStripeSdkUsageOutsideGatewayTests`; 3 handler rewrites + `NegativeBalanceRefundException` |

### Deviations from this plan

_TBD at close-out._

### Forward links

- **Slice OPS.M.6** — per-tenant Redis hold key (independent; no link).
- **Slice OPS.M.7** — onboarding wizard UI consumes `OnboardTenantStripeCommand`/`GenerateStripeAccountLinkCommand` at the §3.12 URLs.
- **Slice OPS.M.8** — Super Admin lights up `IsPlatformAdmin`; `SetTenantPlatformFeeBpsCommand` becomes reachable.
- **Slice OPS.M.9** — RLS on `payment.payment_intents`/`refunds`/`webhook_events`. The `webhook_events.tenant_id` nullable stays nullable; M.9's policy must allow NULL-tenant rows.
- **Phase 4 / Slice 10** — `tenant_connect_accounts` relationship table. `ITenantStripeContextLookup`'s implementation swaps; the interface contract is stable.
