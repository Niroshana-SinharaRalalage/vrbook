# OPS.M.10.2 — F11 Staging Enablement Plan

**Status:** PROPOSED (architect-reviewed). Not committed. Author: system-architect, 2026-06-29.
**Goal:** Guest can walk `property detail → quote → book → /me/bookings → cancel`
on `https://ca-vrbook-web-staging…` end-to-end. Outbound iCal URL retrievable
by the owner from `/admin/sync`.
**Non-goal:** Production-grade Stripe Connect onboarding (real bank account /
1099-K). Stripe sandbox account creation IS in scope — VrBook already integrates
the Stripe SDK and KV secrets are wired (the smoke test proves Stripe.Configured).

---

## Part 1 — Diagnosis

### Blocker A — `Property.Activate()` enforces zero preconditions

File: `src/Modules/VrBook.Modules.Catalog/Domain/Property.cs:167`
```csharp
public void Activate() => IsActive = true;
```
No Stripe-status check, no completeness check (price set? images present?
tenant `Active`?). And there is **no controller endpoint** wired to call it
(grep across `src/VrBook.Api/Controllers/PropertiesController.cs` returns zero
hits). So the 4 staging properties became `is_active=true` via direct DB write
or an early-slice handler that's since been removed.

The error string the user saw on `/api/v1/bookings` —
> Publishing should be gated on StripeAccountStatus = Active; this is upstream-bug territory.

(from `CreatePaymentIntentForBookingHandler.cs:56-57`) is **aspirational**: it
describes the gate that *should* exist. The gate is not implemented.

**Fix:** add `Property.Activate(Tenant)` overload requiring
`tenant.Status == "Active"` AND `tenant.ChargesEnabled && tenant.PayoutsEnabled`,
keep the bare `Activate()` `[Obsolete]` for the migration path. Add the
gate-check at the publish endpoint when it lands. For F11 we **don't** need to
de-activate the seeded properties — once the tenant is onboarded (Blocker C),
the gate is satisfied for them retroactively.

### Blocker B — Tenant `…0001` has no `StripeAccountId`

File: `src/Modules/VrBook.Modules.Identity/Infrastructure/TenantStripeContextLookup.cs:28-36`
selects `t.StripeAccountId` from `identity.tenants`. Tenant `…0001` was created
by an `InitTenant…` migration that doesn't set this column (it's nullable per
`Tenant.cs:35`).

**Two paths to populate it:**

1. **Real Stripe sandbox onboarding** via `POST /api/v1/admin/tenants/{tid}/stripe/onboard`
   (`TenantsAdminController.cs:35-45` → `OnboardTenantStripeHandler` in
   `StripeOnboardingCommands.cs:58-78`). Calls Stripe SDK
   `AccountService.CreateAsync` (Express, US, capabilities=card_payments+transfers).
   Returns `acct_xxx`, persists to `Tenant.StripeAccountId`. Then
   `/stripe/account-link` → operator clicks through Stripe-hosted form → Stripe
   fires `account.updated` webhook → `HandleStripeWebhookCommand` →
   `IConnectAccountReadinessUpdater.UpdateAsync` → `Tenant.UpdateStripeAccountReadiness(true, true)`
   → tenant flips to `Active`.

2. **Stub bridge** (`/api/v1/dev-auth/stub-stripe-account`, prod-guarded per
   F8 pattern — see `IdentityController.cs:185-196` for the existing
   `if (hostEnv.IsProduction()) return NotFound();` guard). Sets
   `Tenant.StripeAccountId = "acct_stub_<tenantId>"` and calls
   `IConnectAccountReadinessUpdater.UpdateAsync(stubId, true, true)` to flip
   status. **Bypasses real Stripe sandbox.**

**Architect's call:** ship **path 1** as the primary (it's the real flow and
also exercises Slice OPS.M.5 + M.7 end-to-end). Ship **path 2** as the **dev
bridge fallback** — staging operators use it when the Stripe-hosted form has
sandbox flake, AND it's the only path that works if DevAuth ever needs to come
back on for a triage. Both paths exist after F11.

### Blocker C — No `PricingPlan` for the 4 seed properties

`POST /quotes` 404s with `PricingPlan '<propertyId>' not found.` There's no
plan seed (no `InsertData` for `pricing.pricing_plans` in any migration —
verified via grep). The owner UI exists at `web/src/app/admin/pricing/page.tsx`
and the API endpoint is `PUT /api/v1/properties/{propertyId}/pricing`
(`PricingController.cs:33-41`, `UpdatePricingPlanHandler.cs:30-46` happy-path
creates a plan on first PUT).

**Fix:** once the owner can sign in (Blocker D) they `PUT` a plan via the
existing UI. F11 does not need a new endpoint, but DOES need a documented
default body (`baseNightlyRate=200, currency=USD`).

### Blocker D — Owner cannot reach `/admin/*` (Entra-only, no PlatformAdmin)

Staging has `DevAuth__AllowAnonymous=false` (`infra/main.bicep:272`). All
`/admin` controllers are `[Authorize(Roles="Owner,Admin")]` or `="PlatformAdmin"`.
Role mapping reads `c.Type == ClaimTypes.Role || c.Type == "roles"`
(`HttpCurrentUser.cs:138`) — so an Entra App Role with value `Owner` or
`Admin` on the signed-in user is sufficient.

The user has already signed in to Entra (commits `63cd52a` + `b21afc7` per
the workflow comment at `cd-staging-api.yml:503-504`). What is **not**
verified: that the signed-in user (a) has the `Owner` or `Admin` App Role
assigned in Entra, AND (b) has a `tenant_memberships` row for tenant `…0001`
so `currentUser.TenantId` resolves (per OPS.M.2 DB-wins precedence, see
`Slice5b_DevAuth_Default_Tenant_Membership.cs:10-31` — that DevAuth seed only
covers the synthetic DevAuth oids, not real Entra users).

**Fix:** a one-shot platform-admin bootstrap. The user is a `PlatformAdmin`
(or we make them one via SQL once) → they hit
`POST /api/v1/admin/platform/tenants/{tid}/…` to operate against tenant
`…0001` cross-tenant (route id is trusted under PlatformAdmin per
`TenantsPlatformController.cs:16-19`). For per-tenant operations like Stripe
onboarding, the user needs a real `tenant_memberships` row attached to their
Entra user_id. F11 ships an `/api/v1/admin/platform/tenants/{tid}/memberships`
endpoint or a SQL one-liner the user runs once.

### Blocker E — Outbound iCal feed token

`ChannelFeed.OutboundToken` (see `src/Modules/VrBook.Modules.Sync/Domain/ChannelFeed.cs:26`)
is generated server-side at `CreateChannelFeedCommand` time
(`ChannelFeedHandlers.cs` — confirmed exists). Public read path is
`GET /api/v1/feeds/{outboundToken}.ics` (`SyncController.cs:22-32`,
`[AllowAnonymous]`). So once the owner reaches `/admin/sync` and creates a
feed, they get a copyable URL. The UI at `web/src/app/admin/sync/page.tsx:1-50`
already renders the list with copy buttons. **No code fix needed in F11 — this
unblocks itself when Blocker D is fixed.**

---

## Part 2 — F11 sub-commit sequence (copy-pasteable)

Each commit ends in a curl-verifiable assertion. Local-validated before push.
**Each goes through `gh run watch` per the "check CI after every push" rule.**

```
F11.1  Add Property.Activate(Tenant) precondition (RED+GREEN+REFACTOR)
       - Tighten the domain gate. `Activate()` (no-arg) -> [Obsolete]; new
         overload throws BusinessRuleViolationException("property.tenant_not_payment_ready")
         when tenant.Status != Active OR !ChargesEnabled OR !PayoutsEnabled.
       - Unit tests in tests/VrBook.Architecture.Tests (existing fact-pack site).
       - No new endpoint; no behavior change for already-active properties.
       - Verify: `dotnet test --filter "Category!=Integration"` green.

F11.2  Add IConnectAccountReadinessUpdater dev-bridge endpoint
       - New: POST /api/v1/dev-auth/stub-stripe-readiness
         body: { tenantId, stripeAccountId?, chargesEnabled=true, payoutsEnabled=true }
       - Three guards (F8 pattern):
           if (hostEnv.IsProduction()) return NotFound();
           if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous")) return NotFound();
           AND a third: configuration["DevAuth:AllowStripeStub"] == "true"
             (defaults false; lit only when an operator explicitly opts in
              via az containerapp update env, never via Bicep).
       - Persists Tenant.SetStripeAccount(stubId) if absent, then
         IConnectAccountReadinessUpdater.UpdateAsync(...).
       - This is the fallback for D's path. Staging keeps DevAuth=false today;
         F11.6 enables it only briefly for the stub call.
       - Verify (local): unit test; CI: existing /dev-auth route shape tests.

F11.3  Add PlatformAdmin membership-seeding endpoint
       - New: POST /api/v1/admin/platform/tenants/{tenantId}/memberships
         body: { entraOid, role="tenant_admin", isPrimary=true }
       - [Authorize(Roles="PlatformAdmin")]; route tenantId is trusted (same
         pattern as Suspend/Reactivate, see TenantsPlatformController.cs:16-19).
       - Idempotent on (user_id, tenant_id); soft-deleted rows revived.
       - Verify (curl, staging, after F11.6 deploy):
           POST /api/v1/admin/platform/tenants/00000000-0000-0000-0000-000000000001/memberships
             { "entraOid": "<your-entra-oid>", "role": "tenant_admin", "isPrimary": true }
           expect 201 {membershipId}.
         Then GET /api/v1/me/tenant → tenantId is …0001.

F11.4  Add Refresh-from-Stripe endpoint (real-flow companion to F11.2 stub)
       - New: POST /api/v1/admin/tenants/{tenantId}/stripe/refresh-readiness
       - [Authorize(Roles="Owner,Admin")] (tenant-scoped, M.4 behavior gates).
       - Re-calls Stripe AccountService.GetAsync(accountId), pulls
         charges_enabled + payouts_enabled, dispatches
         IConnectAccountReadinessUpdater.UpdateAsync.
       - Use case: webhook drops in staging happen; operator can re-pull
         without waiting for Stripe to retry the webhook.
       - Verify (curl): after onboard+account-link flow, call refresh →
         GET /api/v1/me/tenant → onboarding.nextStep == "complete".

F11.5  Backfill SetPlatformFee / smoke: validate the publish gate doesn't
       break the existing 4 seed properties
       - After Blocker B resolves (tenant gets stripe account + Active status),
         existing `is_active=true` properties continue to pass implicit checks
         (the F11.1 gate is on Activate(), not on read paths).
       - Add a one-time SQL migration ONLY if we discover the gate trips
         existing flows (e.g. the booking handler also reads tenant status
         — TODO confirm in F11.1 RED).
       - Skip if no breakage; document the decision in the §11 close-out.

F11.6  Ops: env-var flip + deploy
       - az containerapp update --name ca-vrbook-api-staging
           --set-env-vars DevAuth__AllowStripeStub=true  (TEMPORARY, F11.7
                                                         flips it back)
       - Redeploy API revision; smoke goes green.
       - Verify: GET /api/v1/dev-auth/personas still 404 (DevAuth off),
                POST /api/v1/dev-auth/stub-stripe-readiness still 404 unless
                  the operator explicitly flips DevAuth=true for the call,
                ELSE document that path 1 (real Stripe) is the only live
                path on staging and F11.2 is dormant insurance.

F11.7  Manual operator walk (no code) — see Part 3.

F11.8  §11 close-out doc commit: docs/OPS_M_10_2_F11_CLOSE_OUT.md
       - What shipped, what was deferred (the publish gate at the controller
         layer is deferred to OPS.M.11 properties-lifecycle slice).
       - Reset DevAuth__AllowStripeStub=false (or document it's still off).
```

---

## Part 3 — API verification commands (paired with each F11 step)

Assumes `API=https://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io`
and `TOKEN=<entra-access-token-for-signed-in-owner>` (extract from browser
devtools after sign-in; cookie name is set by the SPA — check
`web/src/lib/auth/msal.ts`).

### After F11.3 deploy + F11.7 manual seeding

```bash
# 1. PlatformAdmin assigns my Entra oid to tenant …0001 (one-time bootstrap)
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$API/api/v1/admin/platform/tenants/00000000-0000-0000-0000-000000000001/memberships" \
  -d '{ "entraOid": "<my-oid>", "role": "tenant_admin", "isPrimary": true }'
# expect: 201 { "membershipId": "<uuid>" }

# 2. Confirm tenant resolves
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/v1/me/tenant" | jq '.id, .status, .hasStripeAccount, .onboarding.nextStep'
# expect: "00000000-…0001", "PendingOnboarding", false, "stripe"

# 3. Onboard tenant Stripe (creates acct_xxx via Stripe SDK)
curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$API/api/v1/admin/tenants/00000000-0000-0000-0000-000000000001/stripe/onboard" \
  -d '{ "country": "US" }'
# expect: 200 { "stripeAccountId": "acct_…" }

# 4. Get hosted onboarding URL → hand to user → user completes Stripe form
curl -s -X POST -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/admin/tenants/00000000-0000-0000-0000-000000000001/stripe/account-link"
# expect: 200 { "url": "https://connect.stripe.com/…", "expiresAt": "…" }
# → USER opens the url in browser, fills test data (Stripe test mode auto-approves),
#   gets redirected back to /admin/onboarding/complete.

# 5. Refresh readiness (in case webhook is slow)
curl -s -X POST -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/admin/tenants/00000000-0000-0000-0000-000000000001/stripe/refresh-readiness"
# expect: 200 { "chargesEnabled": true, "payoutsEnabled": true, "status": "Active" }

# 6. Set price for beach-villa via existing endpoint
PROP=$(curl -s "$API/api/v1/properties" | jq -r '.items[] | select(.slug=="beach-villa") | .id')
curl -s -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  "$API/api/v1/properties/$PROP/pricing" \
  -d '{ "baseNightlyRate":200, "weekendRate":250, "currency":"USD",
        "minStayNights":1, "maxStayNights":30, "dynamicEnabled":false, "fees":[] }'
# expect: 200 plan dto
```

### Repeat for the other 3 seed properties (or accept one is enough for the user walk)

### Final guest-walk (anonymous tab, browser) — F11.7

```
1. https://…/properties/beach-villa     → 200 page renders
2. Pick dates → POST /quotes            → 200 { totalAmount, lineItems[…] }
3. "Book" button → POST /bookings       → 201 (PaymentIntent created via Connect)
4. /me/bookings                          → row appears (status=Tentative)
5. Cancel → POST /bookings/{id}/cancel  → 200 (status=Cancelled)
```

---

## Part 4 — Outbound iCal verification

After F11.3 + F11.7-step-1, the owner reaches `/admin/sync` (Entra-signed,
has `tenant_admin` membership). Steps:

1. Click "Add feed" → channel=AirBnB (or any), inboundUrl=`https://example.com/cal.ics`
   (any valid URL; staging poll will 404 but the create succeeds), pollInterval=30.
2. Row appears with **Copy outbound URL** button → copies
   `https://…/api/v1/feeds/{outboundToken}.ics`.
3. Verify the URL is publicly fetchable:
   ```bash
   curl -i "https://…/api/v1/feeds/<token>.ics"
   # expect: 200, Content-Type: text/calendar; charset=utf-8, body starts BEGIN:VCALENDAR
   ```
4. If the owner's just-placed booking from §3 is `Confirmed`, the ICS should
   contain a `VEVENT` for it. (Tentative bookings may or may not — depends on
   `GetOutboundFeedQuery` filter; verify with the architect if missing.)

**No new code needed for outbound iCal beyond what unblocks Blocker D.**

---

## Part 5 — Risks + dependencies

| Risk | Severity | Mitigation |
|---|---|---|
| Stripe sandbox onboarding form changes/breaks during operator click-through | HIGH | F11.2 stub is the explicit fallback. Don't remove it. |
| `tenant_memberships` row gets created but RLS prevents the user from reading their own tenant | MED | OPS.M.9 identity carve-out (`OpsM9_Identity_RlsPolicies`) already permits self-reads. Verify in F11.3 unit test. |
| F11.1 publish gate retroactively breaks existing seeded properties' read paths | MED | Gate is on `Activate()`, not on reads. Reads check `is_active=true` (already set). Confirm in F11.1 RED. |
| F11.4 refresh endpoint hits Stripe rate limit during retries | LOW | Use existing `StripeRetryPipeline.cs`; no new infra. |
| F11.6 env-var flip on `DevAuth__AllowStripeStub` leaks into prod if Bicep isn't updated | HIGH | Three-guard pattern (Production + DevAuth + AllowStripeStub all required). F11.8 close-out includes "verify flag is OFF in prod Bicep". |
| Scope creep: real Stripe Connect onboarding requires bank-account, 1099-K, etc | HIGH | Stripe Express **test mode** auto-approves with placeholder data. Document the test-mode constraint in close-out; production-grade onboarding is OPS.M.11. |
| `currentUser.TenantId` resolves to NULL for a freshly-membered user until cache invalidates | LOW | Membership cache TTL is request-scoped (M.4). Curl re-issues a fresh token; the user sees it on next request. |

### Hard line — STOP if any of these triggers

- F11.3 membership creation works but `GET /me/tenant` still 401s → escalate; do not proceed to Stripe onboarding.
- F11.1 RED reveals that booking-place or quote-compute READ the publish gate → architect re-consult (the gate scope expands).
- The Stripe Express test-mode form requires real bank info on click-through → use F11.2 stub immediately; do not block on Stripe.

---

## Open architectural questions deferred to OPS.M.11

1. Add `POST /api/v1/properties/{id}/publish` endpoint that calls `Property.Activate(tenant)` — owner-facing publish gesture. F11 ships the domain gate; the controller endpoint is M.11.
2. Add tenant-status gate to `BookingsController.Place` so even if a property somehow gets `is_active=true` without an active tenant, the booking-place itself 422s with a clearer code than `payment.connect_account_missing`.
3. Real per-tenant Stripe Connect onboarding UX (already partial in `/admin/onboarding`) — M.11 hardens the polling + error states.
