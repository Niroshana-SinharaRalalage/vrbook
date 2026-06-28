# OPS.M.7 — Tenant Admin Onboarding Wizard UI + First-Property → Stripe Link (Plan, rev 1)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.7 ("Tenant Admin onboarding wizard UI + first-property → Stripe link", 3-day estimate).
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §4 lines 95-117 (Stripe Connect onboarding UX surface).
**Predecessors**: Slice OPS.M.0 ✅ (Entra External ID), Slice OPS.M.2 ✅ (`ICurrentUser.TenantId`), Slice OPS.M.4 ✅ (`TenantAuthorizationBehavior`), Slice OPS.M.5 ✅ (`TenantsAdminController` + `OnboardTenantStripeCommand`/`GenerateStripeAccountLinkCommand`/`OpenStripeLoginLinkCommand`), Slice OPS.M.6 ✅ (no direct surface).
**Sequence**: After Slice OPS.M.6; before Slice OPS.M.8 (Super Admin console — reuses the read endpoint M.7 ships), Slice OPS.M.9 (RLS — no impact on M.7 wizard surface; M.7 reads its caller's own tenant only), Slice OPS.M.10 (cross-tenant isolation test pack — adds the read endpoint to its sweep).
**Estimate**: **3 days, one engineer** — TDD-first, see §5.

This plan is the contract. Slice OPS.M.7 ships **(i) a tenant-admin onboarding wizard UI that gates the existing tenant dashboard until both onboarding milestones (≥1 property, Stripe Connect Active) are reached**, **(ii) a new read endpoint `GET /api/v1/me/tenant` exposing the derived onboarding state**, **(iii) `/account/onboarding/complete` and `/account/onboarding/refresh` routes that bounce the Stripe AccountLink return back into the wizard with the correct verification step**, **(iv) an operator-manual welcome-email placeholder runbook entry (the ACS pipeline lands in Slice 4)**.

Stripe webhook routing, Stripe Connect command surface, `OnboardingReturnUrl`/`OnboardingRefreshUrl` config, the `Tenant.StripeAccountStatus` lifecycle, and the `OnboardTenantStripeCommand`/`GenerateStripeAccountLinkCommand`/`OpenStripeLoginLinkCommand` MediatR commands are all **prerequisite (OPS.M.5)** — not in scope here. The Super Admin's view of the same status is **OPS.M.8** — not in scope.

---

## 1. Scope summary

### 1.1 What this slice ships

| # | Deliverable | Touches |
|---|---|---|
| 1 | `GET /api/v1/me/tenant` read endpoint returning the wizard's derived state (status, charges/payouts flags, hasStripeAccount, propertyCount) | `IdentityController.cs` (new action), `GetMyTenantQuery` + handler + DTO in Identity module, cross-module `IPropertyCountByTenant` lookup in Catalog (read-only) |
| 2 | `MeTenantDto` contract shape in `VrBook.Contracts/Dtos/Identity.cs` | `Identity.cs` (extend) |
| 3 | Wizard route at `/admin/onboarding` (Next.js page in `web/src/app/admin/onboarding/page.tsx`) — three-step linear flow: Welcome → Create first property → Connect Stripe → Done | `web/src/app/admin/onboarding/page.tsx` + helper components |
| 4 | Onboarding return-trip routes `/admin/onboarding/complete` and `/admin/onboarding/refresh` (Next.js pages) that handle Stripe's redirect target | `web/src/app/admin/onboarding/complete/page.tsx`, `web/src/app/admin/onboarding/refresh/page.tsx` |
| 5 | Wizard gate logic in the admin dashboard layout — if `me/tenant.onboarding.isComplete === false`, the dashboard redirects to `/admin/onboarding` (D5) | `web/src/app/admin/layout.tsx` (edit) |
| 6 | Web API client surface: `getMyTenant()`, `onboardTenantStripe()`, `generateStripeAccountLink()`, `openStripeLoginLink()` | `web/src/lib/api/tenant.ts` (new) |
| 7 | React-query hooks: `useMyTenant()` (with polling-aware refetch on the return-trip page), `useStripeOnboardingFlow()` (orchestrates the three-call dance) | `web/src/hooks/useMyTenant.ts`, `web/src/hooks/useStripeOnboardingFlow.ts` |
| 8 | Operator-manual welcome-email runbook (one Markdown row in `docs/runbooks/tenant-onboarding-welcome-email.md`) — the ops-Powershell sketch for "send the welcome email after a tenant completes M.7" until Slice 4 ACS pipeline | `docs/runbooks/tenant-onboarding-welcome-email.md` (new, short) |
| 9 | Component-level tests (Vitest + React Testing Library) for the wizard state machine | `web/src/app/admin/onboarding/page.test.tsx`, `web/src/hooks/useStripeOnboardingFlow.test.tsx` |
| 10 | Integration test for the API endpoint (`GET /api/v1/me/tenant`) | `tests/VrBook.Api.IntegrationTests/Identity/GetMyTenantEndpointTests.cs` |

### 1.2 What's explicitly OUT of OPS.M.7

| Item | Owner slice | Why deferred |
|---|---|---|
| ACS welcome email send (`tenant.welcome` template + `TenantNotificationHandlers`) | Slice 4 (per master-plan re-attribution) | The ACS pipeline ships in Slice 4. M.7 ships the *trigger event* (`TenantStripeOnboarded` already raised by OPS.M.5 §3.8) and an operator-manual runbook placeholder. Slice 4 adds one MediatR handler subscribing to `TenantStripeOnboarded` + one `tenant.welcome` template row — that's the swap. See §3.8 (D8). |
| Super Admin's read view of all tenants' onboarding state | Slice OPS.M.8 | M.8 builds a similar read endpoint at `/api/v1/admin/platform/tenants/{id}` for cross-tenant viewing. M.7's `GET /api/v1/me/tenant` is caller-scoped only. The two share the DTO shape but not the auth boundary. |
| Multi-Stripe-account suppliers (Phase 4 OTA) | Phase 4 / Slice 10 | M.7 assumes one tenant = one Connect account. Phase 4's `tenant_connect_accounts` relationship table changes the wizard's surface to "Connect Stripe for each supplier role you take on"; out per OPS.M.5 §5. |
| Onboarding analytics (funnel, drop-off) | Slice 4 / Slice OPS.M.8 | M.7's structured-log fields are sufficient for ad-hoc query in Application Insights; a product-analytics dashboard belongs in Slice OPS.M.8's "platform observability" surface. |
| Hard-gate on Publish — "your property cannot be published until Stripe is Active" | Already shipped (OPS.M.5 §3.5/§3.15) | The booking-flow throws `payment.connect_account_missing` if a `CreatePaymentIntentForBookingCommand` fires for a tenant without a Stripe account; M.7's wizard surfaces the same fact pre-emptively. There is no *publish*-side gate today (the property can be published as a draft even without Stripe); the wizard nudges the user to onboard, but does not block publishing. **Decision: confirmed** — see §3.1 D1. |
| Re-onboarding flow when Stripe restricts an Active account | Slice OPS.M.8 (full); M.7 surfaces the state | When `Tenant.UpdateStripeAccountReadiness` transitions Active → Suspended (OPS.M.5 §3.8), `me/tenant.onboarding.status` reports `Suspended`. M.7's wizard shows an "Action required: Stripe is restricted" banner + a re-link button (calls `POST /stripe/account-link` again, which Stripe accepts on the existing account id per OPS.M.5 §3.13 D11). Full operator override (the Super Admin manual-resume flow) is OPS.M.8. |
| Multi-tenant switcher UI (when a user is `tenant_admin` of 2+ tenants) | Phase 2 | Phase 1.5 assumes one user is `tenant_admin` of exactly one tenant (per OPS.M.0 / OPS.M.2 — `ICurrentUser.TenantId` is a singleton, not a list). The wizard reads `currentUser.TenantId` only; multi-tenancy from the user-perspective is a Phase 2 product question. |
| Custom-domain branding on the Stripe-hosted onboarding page | Phase 2 | Stripe Express defaults to "VrBook is collecting payments on your behalf" with VrBook's platform-name. Custom-domain is a Phase 2 ops feature alongside DKIM/SPF (Slice OPS.8). |
| Wizard re-entry from an in-flight Stripe AccountLink (user closed the tab mid-form) | M.7 in scope | This is the §8 partial-state design. NOT out of scope — see §3.4 (D4) + §8. |

---

## 2. Atomic-deploy constraints

Steps 1→8 in §5 sequence into **two waves** (UI ships independently of the API where contracts don't change):

1. **Wave 1 — API + Contracts (Steps 1 + 2)**: ship `GET /api/v1/me/tenant`, the `MeTenantDto`, and the cross-module `IPropertyCountByTenant` lookup in one tag. Pure additive on the API surface; the UI is not yet aware of the endpoint, so this can roll alone safely. The web app continues to function with the OPS.M.5 surface (the dashboard does not gate yet).
2. **Wave 2 — UI (Steps 3 + 4 + 5 + 6 + 7 + 8)**: ship the wizard route, the return-trip routes, the dashboard gate, the API client surface, the React-query hooks, and the runbook in one tag. Wave 2 must NOT roll until Wave 1's API is live in the same environment, or the wizard's first call (`getMyTenant()`) 404s.

**Why split the waves**: the alternative is one big tag, which is fine in production rollouts but forces a co-deploy step in a Phase 1.5 deployment pipeline that prefers ratchet-style independent rolls. Splitting also lets the Slice OPS.M.8 development team start consuming the read endpoint (for cross-tenant reads, with their own auth scope swap) without waiting for the wizard UI to ship.

**Per OPS.M.5 §3.12 (D12)**: the `OnboardingReturnUrl` and `OnboardingRefreshUrl` config keys are already live in staging Key Vault. M.7's deploy must (one-time) flip these to:

- `OnboardingReturnUrl` → `${Frontend:BaseUrl}/admin/onboarding/complete`
- `OnboardingRefreshUrl` → `${Frontend:BaseUrl}/admin/onboarding/refresh`

That is **one Key Vault update + one Container App revision restart** — not a code change. The plan calls this out in §11 as a deploy-time check, not a Step.

**Forward-replay constraint**: M.7 introduces no new outbox events. `TenantStripeOnboarded` is already raised by OPS.M.5 §3.8 D8; the slot owning the wizard does NOT change that event's shape. M.7's Slice 4 forward-swap (D8) adds a *handler* subscription, not a payload change.

---

## 3. Design decisions

### 3.1 D1 — Wizard shape: dedicated `/admin/onboarding` route, dashboard-level gate

Three options were on the table per the brief:

- **(a) Multi-step modal inside the existing dashboard.** Welcome → property → Stripe → done, all rendered as a modal overlay on `/admin`. Pro: lightweight; con: every dashboard view has to know whether to mount the modal; mobile UX is constrained.
- **(b) Dedicated `/admin/onboarding` route that gates the dashboard.** Pro: clean separation, full-page real estate, deep-linkable from email, mobile-friendly. Con: one extra route, slight friction to "I closed the wizard, can I come back?" (resolved by D5's "Continue setup" CTA in the dashboard).
- **(c) Passive task-list widget at the top of the dashboard.** Pro: minimum disruption; con: violates the OPS.M.5 §3.5 contract that the wizard MUST get the tenant onboarded before their first booking (a passive nag does not).

**Verdict: (b) dedicated route.** Reasoning:

1. **OPS.M.5 §3.5 D5 (locked) throws `payment.connect_account_missing` on a booking against a Stripe-less tenant.** This is the *hard* contract: a guest cannot pay until the tenant's Stripe is Active. The wizard's job is to make that the user's blocking task, not a side-quest. A modal is dismissible; a dedicated route + dashboard gate is not (until the gate condition clears).
2. **First-impression UX.** A freshly-provisioned tenant lands on the dashboard with zero properties and zero bookings — the dashboard's existing KPI strip (`AdminDashboardPage` at `web/src/app/admin/page.tsx`) shows `0/0` properties, `0` tentative, `0` confirmed. A full-page guided flow is friendlier than an empty dashboard with a modal on top.
3. **Phase 4 future-proofing.** Phase 4 multi-supplier means "Connect Stripe" might become "Connect Stripe for each supplier role". A dedicated route can grow steps without scope creep; a modal cannot.
4. **Deep-linking.** The Stripe AccountLink return URL is `/admin/onboarding/complete` — a dedicated route is the natural target. With option (a), the return-trip would have to navigate to `/admin` AND open the modal AND advance it to step-3 — fragile.
5. **Phase-1 product brief** ("a fresh tenant must complete onboarding before publishing") — note the property publish is technically uncoupled from Stripe onboarding (the Catalog module does NOT check Stripe state on publish). The wizard's enforcement is *behavioral*, not *contract*: we make Stripe-onboarding the visually-prominent next step so the user doesn't skip it. The non-publish-gate is intentional — see the §1.2 row "Hard-gate on Publish".

**Decision: option (b) — dedicated `/admin/onboarding` route, with the admin dashboard layout's redirect-gate (D5) bouncing the user there until `MeTenantDto.onboarding.isComplete === true`.**

#### What the wizard's three steps look like

Each step is a stacked card in a single-column page; the user progresses linearly (no skipping ahead — but they CAN navigate backward to re-read a completed step). The wizard's state machine is documented in §8.

1. **Welcome** — "Hi {DisplayName}, let's set up your VrBook account." Auto-marked complete on first paint (it's a teaser card). One CTA: "Start setup →".
2. **Create your first property** — explains why the wizard needs a property before Stripe (rationale: Stripe Connect's onboarding asks for "what are you selling" type questions; the user benefits from having a property in mind). One CTA: "Add property →" links to the existing `/admin/properties/new` page. **The wizard does NOT replicate the property form** — it reuses the existing route. The user returns to `/admin/onboarding` after creating the property (we set a `?from=onboarding` query so the property-create page can know to bounce back).
3. **Connect Stripe to get paid** — explains the platform-fee model ("VrBook takes {PlatformFeeBps/100}% per booking; you keep the rest"). One CTA: "Connect Stripe →". Behind the scenes (D3): calls `POST /api/v1/admin/tenants/{tenantId}/stripe/onboard` then `POST /stripe/account-link`, then redirects to the Stripe-hosted onboarding URL.
4. **Done** — confirmation card; appears after the Stripe AccountLink return flow lands and the polling loop (D7) sees `chargesEnabled && payoutsEnabled`. One CTA: "Go to dashboard →" navigates to `/admin`.

`me/tenant.onboarding.isComplete` flips to `true` when step 2 AND step 3 are done (i.e. `propertyCount >= 1 && stripeAccountStatus === 'Active'`). Step 1 (Welcome) is purely visual — it doesn't gate anything.

### 3.2 D2 — Tenant-state endpoint: `GET /api/v1/me/tenant`

The wizard needs to read:

- `Tenant.Status` (PendingOnboarding / Active / Suspended / Closed).
- `Tenant.StripeAccountId` is not null (i.e. Onboard step has been called at least once).
- `Tenant.ChargesEnabled` AND `Tenant.PayoutsEnabled` (from OPS.M.5 §3.8 — the Stripe-readiness booleans).
- `Tenant.StripeAccountStatus` (the free-text mirror — string).
- `Tenant.PlatformFeeBps` (for the "VrBook takes X%" copy).
- `propertyCount` (count of `catalog.properties WHERE tenant_id = caller AND deleted_at IS NULL` — does NOT filter by `is_active`; a draft counts).

**Decision: ship as `GET /api/v1/me/tenant`** (NOT `GET /api/v1/admin/tenants/{tenantId}`). Reasoning:

- The wizard is caller-scoped — it reads "my tenant", not "tenant by id". `/api/v1/me/tenant` mirrors `/api/v1/me`'s existing shape (`IdentityController` is the home for caller-scoped reads). Verified `IdentityController.cs:14-25`.
- The route `/api/v1/admin/tenants/{tenantId}` already exists (`TenantsAdminController.cs:23`); shipping a GET there would be feasible but creates a cross-tenant read shape (Slice OPS.M.8's surface), which we do NOT want to expose to a regular `Owner` role today. M.7 deliberately ships only the caller-scoped read; OPS.M.8 will add the cross-tenant shape with a `[Authorize(Roles="PlatformAdmin")]` gate.
- The DTO is the same shape — see §4.

#### Endpoint shape

```
GET /api/v1/me/tenant
Authorization: Bearer <Entra access token>
→ 200 OK { tenant: { id, slug, displayName, status, defaultCurrency, platformFeeBps, stripeAccountStatus, chargesEnabled, payoutsEnabled, hasStripeAccount, propertyCount, onboarding: { isComplete, nextStep } } }
→ 401 Unauthorized (no bearer)
→ 403 Forbidden (authenticated but `currentUser.TenantId == null` — i.e. caller is a Guest without a tenant membership)
```

**Polling cadence (D7)**: the wizard's "Stripe completion is being verified" surface polls this endpoint every 1 second, capped at 30 seconds total, with a manual "Refresh now" button as the fallback. The endpoint is cheap (a single Identity-side JOIN + a Catalog-side count) — see §3.7.

**Cache strategy**: the endpoint sends `Cache-Control: no-store` (because the wizard polls and stale state defeats the purpose). React-query's stale-time is set to 0 for `useMyTenant()` to match.

#### Why ship this as a Query, not a derived JSX value

The wizard's gate condition `onboarding.isComplete` is *server-derived*, not client-derived. Reasons:

1. **Atomicity.** The "first booking arrives before Stripe is Active" race exists if the client computes `isComplete`. The server reads both states inside one DB read.
2. **Server is the audit surface.** Slice OPS.M.8's tenant-detail page reads the same DTO. Two computations would diverge.
3. **Auth boundary.** The endpoint can refuse anonymous callers cleanly; client-side derivation would expose the shape to anyone.

**Decision: `GET /api/v1/me/tenant`, caller-scoped, server-derived `isComplete`, `Cache-Control: no-store`, react-query stale-time 0.**

### 3.3 D3 — Stripe deep-link UX: two separate calls (`onboard` then `account-link`)

The two options:

- **(a) Wizard click → `POST /stripe/onboard` → `POST /stripe/account-link` → redirect to link URL.** Three round-trips: client→server→Stripe (Account.create), client→server→Stripe (AccountLink.create), client→Stripe-hosted page.
- **(b) Combine into one server call `POST /stripe/onboard-and-link` that does both.** Two round-trips.

**Verdict: (a) two separate calls.** Reasoning:

1. **OPS.M.5 §3.13 D11 confirms** `Account.create` is idempotent on tenantId (the idempotency-key is `tenant-onboarding:{tenantId:D}`). `AccountLink.create` is *not* idempotent (it returns a fresh 5-minute link each call by design — see OPS.M.5 §3.13 row 1).
2. **The user might revisit the wizard after closing the tab.** On revisit, `tenant.StripeAccountId` is already set (Stripe.Account exists). The wizard's "Connect Stripe →" CTA must NOT re-call `Onboard` (which would short-circuit but still hits the API) — it goes directly to `account-link`. The flag is `MeTenantDto.tenant.hasStripeAccount`. **The client logic**:
   ```ts
   if (!me.tenant.hasStripeAccount) {
     await onboardTenantStripe();   // creates Stripe.Account
   }
   const { url } = await generateStripeAccountLink();  // always — link expires in 5 min
   window.location.href = url;
   ```
3. **Combining the calls would require server-side branching** ("if tenant.StripeAccountId is null, do Onboard; otherwise just AccountLink") that lives better in the client. Server-side wisdom for *one* extra step lacks YAGNI justification.
4. **Failure surfacing**: if `Onboard` fails (Stripe API down), the wizard shows "Couldn't create your Stripe account — try again". If `account-link` fails (rate limit, transient), the wizard shows "Couldn't generate the onboarding link — try again". The two error surfaces are different and the user-facing copy should be too.

**Decision: option (a) — two separate calls, client-orchestrated; the wizard's `useStripeOnboardingFlow()` hook contains the `if !hasStripeAccount → onboard` short-circuit.**

#### Sequence diagram (D3)

```
[Wizard "Connect Stripe →" click]
        │
        ▼
  Read me.tenant.hasStripeAccount
        │
   ┌────┴────┐
   │  false  │ true
   ▼         ▼
[POST stripe/onboard] (skipped)
   │         │
   ▼         ▼
[POST stripe/account-link]   ── server creates Stripe AccountLink
        │
        ▼
[window.location.href = link.url]   ── browser redirects to Stripe-hosted page
        │
   ┌────┴────────────────────────────────────┐
   │ User completes onboarding on stripe.com │
   └────┴────────────────────────────────────┘
        │
        ▼
[Stripe redirects to OnboardingReturnUrl]   ── i.e. /admin/onboarding/complete
        │
        ▼
[Polling loop on /api/v1/me/tenant]   ── D7
        │
   ┌────┴────────────────────────────┐
   │ chargesEnabled && payoutsEnabled │
   └────┴────────────────────────────┘
        │
        ▼
[Wizard step 4 "Done" appears]
```

### 3.4 D4 — Return-trip verification: poll `/api/v1/me/tenant` until both flags flip

Stripe redirects back to `OnboardingReturnUrl` (i.e. `${Frontend:BaseUrl}/admin/onboarding/complete`) with NO signature, NO state token, NO success/failure parameter. (This is by Stripe Connect Express design — the AccountLink's return URL is dumb. The trustable signal is the `account.updated` webhook, which OPS.M.5 §3.7 handles server-side, which flips `Tenant.ChargesEnabled` / `Tenant.PayoutsEnabled` via `UpdateStripeAccountReadiness`.)

**Decision: the return-trip handler at `/admin/onboarding/complete` polls `GET /api/v1/me/tenant` every 1 second, capped at 30 seconds, with a fallback "Refresh now" button.** Reasoning:

1. **The webhook is the only trustable signal.** The return URL means "the user finished the form *or* abandoned mid-form *or* hit refresh". We can't trust it.
2. **The webhook is asynchronous.** OPS.M.5 §3.7 webhook handler updates the Tenant aggregate inside a request; from the browser's perspective the round-trip is "page loads → webhook fires server-side → DB updated → next poll picks it up". Typical lag: 1-3 seconds. The 30-second cap covers Stripe's documented worst-case webhook delay (~10s).
3. **Polling cadence**: 1-second interval is friendly to both the API and the user. 30 polls × 1s = 30s. Sufficient even at Stripe's worst case. **NOT exponential backoff** — exponential makes the user wait longer when the failure mode is "webhook never arrived" (no point lengthening the wait). Linear interval, fixed cap.
4. **Fallback "Refresh now" button.** If the polling expires without success, the page shows "Still verifying… click here to refresh" — manual click re-runs the same query, restarts the 30-second window. The user is never stuck.
5. **NOT SignalR.** SignalR Serverless (per Slice 7 / proposal §2.5) is not part of M.7. Polling is the Phase 1.5 right tool. M.7's React-query hooks are SignalR-swap-ready (the `useMyTenant()` callback shape works whether the trigger is poll-tick or SignalR push) — Phase 2 can swap if needed.
6. **Browser stays put on `/admin/onboarding/complete`**: the page does NOT auto-redirect to `/admin/onboarding`. It is its own self-contained "verifying…" surface that completes in-place to "Done!" then offers a "Go to dashboard →" button. The user keeps the URL they were sent to by Stripe.

**Decision: 1-second polling interval, 30-second cap, manual "Refresh now" fallback, no auto-redirect — the polling lives on `/admin/onboarding/complete` itself.**

#### Refresh page

`/admin/onboarding/refresh` is Stripe's other URL — it fires when the AccountLink has expired (typically because the user took >5 minutes to start the flow). Stripe redirects here with the message "your onboarding link expired". The page reads `me/tenant` (to confirm the user is not already Active), then re-calls `POST /stripe/account-link` and redirects again. The user experience is "you got bounced, here's a fresh link, off you go". Single CTA: "Continue onboarding →" (which fires the re-link automatically — no extra click). Failure mode: if the re-link fails, fall back to the wizard root at `/admin/onboarding` with a flash message.

### 3.5 D5 — Where the wizard lives, and how the dashboard gates

The gate logic:

- The admin dashboard layout (`web/src/app/admin/layout.tsx`) wraps every `/admin/*` route. It runs an effect on mount that reads `useMyTenant()`.
- If `useMyTenant()` returns `{ onboarding: { isComplete: false, nextStep: 'CreateProperty' | 'ConnectStripe' | 'AwaitingVerification' } }`, the layout redirects to `/admin/onboarding` UNLESS the user is *already* on a route under `/admin/onboarding/*` OR `/admin/properties/new` (so the user can complete step 2 without bouncing).
- If `isComplete === true`, no redirect — the user lands wherever they navigated.
- The "Continue setup" CTA in the admin sidebar (D6) is shown only when `isComplete === false`.

**Why the gate lives in the layout, not middleware**: Next.js middleware runs at the edge before React Query has fetched anything, so it has no awareness of the `me/tenant` state. The layout runs client-side, where the cached query result is available. This is also consistent with Phase 1.5's auth posture — auth is enforced server-side at the API (the layout is purely UX guidance, not a security boundary).

**Auth boundary check**: the wizard MUST verify the caller is a tenant Owner/Admin before showing any setup steps. We don't add a new auth check; we rely on `/api/v1/me/tenant` returning 403 if `currentUser.TenantId == null` (i.e. caller is a Guest). The wizard page renders an "Access denied" surface for 403 — same shape as the existing `/admin` pages.

**Cross-tenant safety (§7)**: every API call the wizard makes derives its tenant id from `ICurrentUser` server-side — NEVER from a query string or URL segment. Verified — the OPS.M.5 §3.13 D11 `TenantsAdminController` pattern is `CallerTenantId() => currentUser.TenantId`. The wizard does NOT pass a `tenantId` parameter anywhere.

**Decision: gate in the admin layout, not middleware; `/admin/onboarding/*` and `/admin/properties/new` are exempt from redirect; the sidebar's "Continue setup" CTA links to `/admin/onboarding` when `isComplete === false`.**

#### Specifically what changes in `admin/layout.tsx`

The existing layout (`web/src/app/admin/layout.tsx:1-28`) is server-rendered with no auth-aware logic. M.7 adds:

1. The layout becomes a thin server component that renders a new `<AdminShell>` client component as its only child.
2. `<AdminShell>` is `'use client'`, calls `useMyTenant()`, and:
   - On `isLoading`: renders a skeleton shell (sidebar + spinner in main).
   - On `error`: if 403, renders the "Access denied" surface; if 401, redirects to `/auth/callback`; if other, renders an inline error card.
   - On success: if `data.onboarding.isComplete === false` AND the current path is not on the exempt list, calls `router.replace('/admin/onboarding')`.
   - Renders `<AdminSidebar />` + content as today.

The shell's redirect is *one-way* (replace, not push) so the user doesn't accumulate back-stack noise.

### 3.6 D6 — Component reuse: existing Tailwind + Lucide; no new component library

The codebase ships **plain Tailwind** with **Lucide icons**, **react-query** for server state, and **react-hook-form** for forms. There is no shadcn/MUI/Radix component library. The component primitives in use (verified by reading existing pages):

| Need | Existing pattern | File reference |
|---|---|---|
| Section card | `<section className="rounded-xl border border-border bg-card p-6">` + `<h2>` | `web/src/app/admin/properties/new/page.tsx:414-418` |
| Primary button | `<button className="inline-flex items-center gap-2 rounded-md bg-brand-maroon-600 px-5 py-2.5 text-sm font-medium text-white hover:bg-brand-maroon-700 disabled:opacity-50">` | `web/src/app/admin/properties/new/page.tsx:395-401` |
| Secondary button | `<button className="rounded-md border border-input bg-background px-4 py-2 text-sm hover:bg-accent">` | (derived from sidebar items + existing admin pages) |
| Alert/banner | `<div className="rounded-md border border-yellow-300 bg-yellow-50 p-4 dark:border-yellow-700 dark:bg-yellow-900/20">` | `web/src/app/admin/page.tsx:83-100` |
| Step indicator | New — small custom component `<WizardStepIndicator step={n} of={3} />` rendered above the active card | New |
| Loading spinner | Existing inline pattern (Lucide `<Loader2 className="h-4 w-4 animate-spin" />` — no current import; we add it for M.7) | New (uses existing Lucide dep) |
| Layout helpers (cn, etc.) | `@/lib/utils/cn` | `web/src/lib/utils/cn.ts` |
| API client | `@/lib/api/client` (`apiFetch`) | `web/src/lib/api/client.ts` |
| Auth check | `useAuth()` returns `user.isOwner` / `user.isAdmin` | `web/src/lib/auth/useAuth.ts` |
| Toast notifications | None today — we DO NOT add one for M.7 (inline error messages only — same pattern as the property-create page at `page.tsx:166-170`) | (no new dep) |

**Decision: no new component library, no toast library, no UI dep churn. All wizard primitives are inline Tailwind + Lucide.**

#### New components introduced by M.7

| Component | File | Responsibility |
|---|---|---|
| `OnboardingWizardPage` (page component) | `web/src/app/admin/onboarding/page.tsx` | Top-level wizard view; orchestrates the three step-cards |
| `WizardStepIndicator` | `web/src/components/onboarding/WizardStepIndicator.tsx` | Renders "Step N of 3 · Welcome / Create your first property / Connect Stripe" with a progress dot row |
| `WelcomeStepCard` | inline within `page.tsx` | Step 1's card; static content + CTA |
| `CreatePropertyStepCard` | inline within `page.tsx` | Step 2's card; shows current property count, "Add your first property →" CTA linking to `/admin/properties/new?from=onboarding`, or "✓ You added {n} property(s)" if already done |
| `ConnectStripeStepCard` | inline within `page.tsx` | Step 3's card; the platform-fee copy + "Connect Stripe →" CTA; calls `useStripeOnboardingFlow()` |
| `DoneStepCard` | inline within `page.tsx` | Step 4's card; appears after the polling loop confirms Active |
| `OnboardingCompletePage` (page component) | `web/src/app/admin/onboarding/complete/page.tsx` | Stripe return URL handler; runs the polling loop |
| `OnboardingRefreshPage` (page component) | `web/src/app/admin/onboarding/refresh/page.tsx` | Stripe expired-link handler; re-runs `account-link` and redirects |
| `useMyTenant()` hook | `web/src/hooks/useMyTenant.ts` | react-query wrapper around `getMyTenant()`; configurable polling cadence |
| `useStripeOnboardingFlow()` hook | `web/src/hooks/useStripeOnboardingFlow.ts` | Encapsulates the D3 sequence: onboard-if-needed → account-link → window.location.href = url |

### 3.7 D7 — Polling cadence + error path

The polling concern lives on `/admin/onboarding/complete` (after the Stripe return) and (optionally) on `/admin/onboarding` step 3 if the user somehow lands there with a Stripe Account already created but not Active yet (e.g. they navigated away mid-onboarding).

**Polling configuration** (from `useMyTenant({ pollInterval: 1000, pollMax: 30 })`):

| Parameter | Value | Reason |
|---|---|---|
| Polling interval | 1000 ms | Friendly to API + perceptibly fast for the user |
| Max polls | 30 | 30 polls × 1s = 30s, covers Stripe's documented worst-case webhook lag |
| Backoff | None (linear) | Adding backoff slows down the worst case (webhook delayed) for no benefit |
| Cancellation | On unmount + on success | react-query handles this automatically |
| Success predicate | `data.onboarding.isComplete === true` (i.e. propertyCount >= 1 AND status === 'Active') | The Active status is the canonical signal |
| Fallback on exhaustion | Show "Still verifying…" with a "Refresh now" button (manual click resets the polling) | Never leave the user stuck |
| Error path | Inline error card; offer "Go back to wizard" link | No silent failures |

**Server load envelope**: at one tenant in active onboarding at a time, polling for 30 seconds, that's 30 requests/tenant/onboarding-session. With ~100 tenants onboarded per day (Phase 1.5 scale), that's ~3000 requests/day on this endpoint — trivially small. No rate-limit concern.

**Webhook-late edge case**: if 30s elapses without the webhook arriving, the user clicks "Refresh now". This re-fetches `me/tenant`, which now reads from the canonical Identity DB. If still PendingOnboarding, the page shows "Verification is taking longer than usual — please contact support" + a "Try once more" CTA (which resets the polling). We do NOT auto-retry past the 30-second cap; manual intervention is the correct signal.

**Error states the polling must handle**:

| Server response | Wizard surface |
|---|---|
| 200 + isComplete=false, step=AwaitingVerification | Continue polling (no UI change) |
| 200 + isComplete=true | Stop polling, advance to Done step |
| 200 + tenant.status="Suspended" | Stop polling, show "Action required: Stripe account is restricted" + a re-link button (calls `useStripeOnboardingFlow()` again) |
| 401 Unauthorized | Bounce to `/auth/callback` (existing auth flow) |
| 403 Forbidden | "Access denied" — the caller has no tenant membership; show "Contact support" copy |
| 5xx | Inline error card; retry once after 2s; if still failing, show "Connection error — please refresh" |
| Network failure (no response) | Same as 5xx |

**Decision: 1s interval, 30 polls max, linear, manual "Refresh now" fallback, server-state-driven error matrix (above).**

### 3.8 D8 — Welcome email operator placeholder + Slice 4 swap

Per master-plan re-attribution, the `tenant.welcome` ACS template ships with Slice 4. M.7 produces the welcome *screen* (the wizard's step 4 "Done" card is the in-product welcome surface) but does NOT send an email. The email goes out via an ops-Powershell sketch until Slice 4 ships the ACS pipeline + the `TenantNotificationHandlers` MediatR handler.

#### What the ops Powershell sketch needs (documented in `docs/runbooks/tenant-onboarding-welcome-email.md`):

| Field | Source | Notes |
|---|---|---|
| Tenant id | `identity.tenants.id` | The triggering aggregate id |
| Tenant display name | `identity.tenants.display_name` | For email salutation |
| Owner email | `identity.users.email` of the user whose `tenant_memberships.is_primary = true` for this tenant | The "to" address |
| Owner display name | `identity.users.display_name` | For email salutation |
| Stripe-onboarded timestamp | `identity.tenants.charges_enabled` flip timestamp (read from audit_log or domain_events) | For "you onboarded at X" line |
| Dashboard URL | `${Frontend:BaseUrl}/admin` | For "log in to your dashboard" CTA |
| Wizard CTA URL | `${Frontend:BaseUrl}/admin/properties` | "Add another property" CTA |
| ACS connection string | Key Vault `acs-connection-string` | Already in ops's Key Vault per OPS.M.0 |
| Sender address | `appsettings.json` `Acs.SenderAddress` (`donotreply@vrbook.example.com`) | Already configured |
| Subject line | "Welcome to VrBook — you're ready to accept bookings" | Hard-coded in the runbook |
| Body template | inline in the runbook (Markdown rendered to plain-text email) | Hard-coded in the runbook |

The runbook documents how to query the four most recent `TenantStripeOnboarded` events (e.g. read `domain_events` table or an audit query), iterate, and send the email via `New-AzCommunicationServicesEmail` (or `az communication email send`). Owner runs this manually weekly — Phase 1.5 scale is ~5 tenants/week at most, so a weekly batch is acceptable.

#### What the Slice 4 swap looks like

**One PR, ~3 file edits, no rewiring**:

1. New file `src/Modules/VrBook.Modules.Notifications/Application/TenantNotificationHandlers.cs` (1 class) — subscribes to `INotificationHandler<TenantStripeOnboarded>`, renders the `tenant.welcome` ACS template, queues the send through Slice 4's notification pipeline.
2. New row in `notifications.templates` table (the Slice 4 plan's templates table) — `tenant.welcome` Liquid/Handlebars body.
3. Slice 4's arch test catches the new handler; M.7's wizard surface is unchanged (the email is fire-and-forget from `TenantStripeOnboarded` — the wizard already knows the user is Active).

The swap touches **zero M.7 code**. The runbook entry stays in `docs/runbooks/` but a "deprecated — auto-sent by Slice 4 since {date}" header gets added.

**Decision: M.7 ships the wizard "Done" card + the runbook entry; Slice 4 adds the ACS handler subscription against the already-raised `TenantStripeOnboarded` event; no M.7 code changes when Slice 4 ships.**

### 3.9 D9 — Property-creation handler does NOT need a "first property" trigger

Re-verified against `src/Modules/VrBook.Modules.Catalog/Domain/Property.cs:116` — `Property.Create` raises `PropertyCreated(PropertyId, OwnerUserId, Slug, Title, TenantId)`. There is no "first property" flag in the event payload; whether this is the tenant's first or fiftieth property is not a domain concern. The wizard handles "is this the first property?" purely client-side by reading `MeTenantDto.tenant.propertyCount >= 1`.

**Decision: zero backend code change for the "first property → Stripe link" trigger. The trigger is a UI-derived computation against the read endpoint (D2). The brief's phrase "first-property → Stripe link" reads as a *UX* commitment ("after they create their first property, prompt them to connect Stripe"), not a *backend trigger* commitment.**

### 3.10 D10 — Browser-side wizard re-entry with hash-fragment step pointer

The wizard's step state is computed from the server state (`me/tenant`):

| Server state | Wizard step rendered |
|---|---|
| `propertyCount === 0 && !hasStripeAccount` | Step 1 (Welcome) → CTA advances to Step 2 (the user hasn't started anything) |
| `propertyCount >= 1 && !hasStripeAccount` | Step 3 (Connect Stripe) — Step 2 is shown as "✓ {n} property(ies)" |
| `propertyCount >= 1 && hasStripeAccount && status !== 'Active'` | Step 3 with a "Continue verification →" CTA (re-link if AccountLink expired) |
| `propertyCount >= 1 && hasStripeAccount && status === 'Active'` | Step 4 (Done) — but this state means `isComplete === true`, so the dashboard gate would have already redirected away from the wizard. The wizard only renders Step 4 as a transition state after the polling loop on `/admin/onboarding/complete` succeeds. |

The wizard maintains NO client-side state machine — it's a pure function of the server state. This simplifies re-entry: if the user closes the tab and comes back, the same server state computes the same step. No URL fragments, no localStorage, no cookies.

**Decision: the wizard's rendered step is a pure function of `MeTenantDto`. No client state. Re-entry is automatic.**

### 3.11 D11 — Accessibility commitments

The wizard surface must be keyboard-navigable and screen-reader-friendly. Specifically:

| Requirement | Implementation |
|---|---|
| Skip-link to main content | One link at top of `/admin/onboarding` that focuses the active step card. Standard pattern. |
| Step indicator readable by screen reader | `<ol aria-label="Onboarding steps">` with `<li aria-current="step">` on the active step |
| Each card has a labeled landmark | `<section aria-labelledby="step-N-heading">` + `<h2 id="step-N-heading">` |
| Buttons have visible focus indicator | Tailwind `focus-visible:ring-2 focus-visible:ring-brand-maroon-600` (matches existing admin pages) |
| Error messages associated with the relevant card | `<div role="alert">` for the error band on a card |
| Polling status announced | Live region `<div aria-live="polite">` on the verifying-state copy |
| Color contrast | Brand colors already meet WCAG AA (verified in Slice 1's design pass — `brand-maroon-600` on `bg-card` is 7.2:1) |
| Tab order | DOM order (no `tabIndex` overrides) |

**Decision: above table is the contract; component tests (§5 Step 7) assert the live region + step-indicator a11y attributes.**

### 3.12 D12 — Auth gating: Owner OR Admin role

The wizard renders for any caller with `currentUser.TenantId != null` AND a role of `Owner` or `Admin`. The `/api/v1/me/tenant` endpoint enforces this with `[Authorize(Roles = "Owner,Admin")]`, mirroring the `TenantsAdminController` pattern (`TenantsAdminController.cs:25`).

Why not narrower (e.g. only `tenant_admin`)? Because OPS.M.5's onboarding endpoints accept `Owner,Admin` as well — the wizard's auth contract must match what its called endpoints accept. Verified `TenantsAdminController.cs:25` says `[Authorize(Roles = "Owner,Admin")]`.

Why not wider (e.g. include a future `tenant_member` role)? Because Phase 1.5's role taxonomy is exactly two roles per the existing controllers; widening is a Phase 2 concern.

**Decision: `[Authorize(Roles = "Owner,Admin")]` on `GET /api/v1/me/tenant`; same role check in the wizard layout (read from `useAuth().user.isOwner || useAuth().user.isAdmin`).**

---

## 4. Endpoint inventory

Every API endpoint the wizard hits, with HTTP method, request/response DTO, and auth role. **All endpoints are already shipped by OPS.M.5 EXCEPT for the GET on `/api/v1/me/tenant`, which is the only new endpoint M.7 introduces.**

| Endpoint | Method | Request | Response | Auth role | New in M.7? |
|---|---|---|---|---|---|
| `/api/v1/me/tenant` | GET | (no body) | `MeTenantDto` (see §4.1) | `[Authorize(Roles="Owner,Admin")]` (caller MUST have `TenantId != null`) | **YES — M.7 introduces** |
| `/api/v1/admin/tenants/{tenantId}/stripe/onboard` | POST | `OnboardTenantStripeRequest { Country: "US" \| string }` | `OnboardTenantStripeResult { StripeAccountId: string }` | `[Authorize(Roles="Owner,Admin")]` + `TenantAuthorizationBehavior` (route id MUST match caller's TenantId) | No — OPS.M.5 §3.3 D3 |
| `/api/v1/admin/tenants/{tenantId}/stripe/account-link` | POST | (no body) | `GenerateStripeAccountLinkResult { Url: string, ExpiresAt: DateTimeOffset }` | Same | No — OPS.M.5 §3.3 D3 |
| `/api/v1/admin/tenants/{tenantId}/stripe/login-link` | POST | (no body) | `OpenStripeLoginLinkResult { Url: string }` | Same | No — OPS.M.5 §3.3 D3; used by the "Open Stripe dashboard" link on the Done card |

The `{tenantId}` URL segment is passed but discarded server-side; the OPS.M.5 `TenantsAdminController.cs:37/50/63/76` pattern is `_ = tenantId; var x = await mediator.Send(new …Command(CallerTenantId(), …))`. The wizard MUST always pass `currentUser.TenantId` (which is `data.tenant.id` from `useMyTenant()`) — never derived from anywhere else. See §7.

### 4.1 `MeTenantDto` shape

```csharp
public sealed record MeTenantDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,                  // "PendingOnboarding" | "Active" | "Suspended" | "Closed"
    string DefaultCurrency,         // ISO 4217
    int PlatformFeeBps,             // e.g. 1500 = 15%
    string? StripeAccountStatus,    // free-text mirror; nullable
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool HasStripeAccount,
    int PropertyCount,
    OnboardingProgressDto Onboarding);

public sealed record OnboardingProgressDto(
    bool IsComplete,
    string NextStep);  // "Welcome" | "CreateProperty" | "ConnectStripe" | "AwaitingVerification" | "Done"
```

**Why derive `NextStep` server-side?** Because the same logic powers Slice OPS.M.8's tenant-detail page (cross-tenant view); two computations would drift. The server's derivation is a single switch:

```csharp
public static string DeriveNextStep(MeTenantDto t) =>
    (t.HasStripeAccount, t.PropertyCount, t.Status) switch
    {
        (false, 0, _) => "Welcome",
        (false, >= 1, _) => "ConnectStripe",
        (true, 0, _) => "CreateProperty",           // edge case — Stripe done but no property
        (true, >= 1, "Active") => "Done",
        (true, >= 1, "PendingOnboarding") => "AwaitingVerification",
        (true, >= 1, "Suspended") => "AwaitingVerification",  // re-link path, show same UI
        (true, >= 1, "Closed") => "Done",            // shouldn't happen, but safe
        _ => "Welcome",
    };

public static bool DeriveIsComplete(MeTenantDto t) =>
    t.HasStripeAccount && t.PropertyCount >= 1 && t.Status == "Active";
```

`IsComplete` and `NextStep` are computed in the handler (read-side; never persisted).

### 4.2 Cross-module read — property count

The Identity module does NOT have direct DB access to the Catalog schema (per OPS.M.3 module-boundary contract). Two options:

- **(a) New cross-module read contract `IPropertyCountByTenant.GetCountAsync(Guid tenantId, CancellationToken)` in `VrBook.Contracts/Interfaces/`**, with the impl in Catalog (a single `EF Count` against `catalog.properties WHERE TenantId = @tid AND DeletedAt IS NULL`).
- **(b) Raw SQL in the GetMyTenant handler, reading the `catalog.properties` table directly via `NpgsqlDataSource`.**

**Verdict: (a) — same shape as OPS.M.5 §3.4 `ITenantStripeContextLookup`.** Reasoning: raw SQL inside a handler is exactly the pattern OPS.M.5 §3.4 deleted; this slice does not re-introduce it.

```csharp
namespace VrBook.Contracts.Interfaces;

/// <summary>OPS.M.7 §4.2 — cross-module count read for the onboarding wizard's
/// "have you created your first property yet?" check. Read-only.</summary>
public interface IPropertyCountByTenant
{
    Task<int> GetCountAsync(Guid tenantId, CancellationToken ct);
}
```

Impl in `VrBook.Modules.Catalog/Infrastructure/PropertyCountByTenant.cs`:

```csharp
internal sealed class PropertyCountByTenant(CatalogDbContext db) : IPropertyCountByTenant
{
    public Task<int> GetCountAsync(Guid tenantId, CancellationToken ct) =>
        db.Properties
            .Where(p => p.TenantId == tenantId && p.DeletedAt == null)
            .CountAsync(ct);
}
```

DI registration in `CatalogModule.cs`. Wired into the `GetMyTenantHandler` via constructor injection.

**Decision: option (a), `IPropertyCountByTenant` contract in Contracts, impl in Catalog, no raw SQL in the handler.**

### 4.3 `GetMyTenantHandler` shape

```csharp
internal sealed class GetMyTenantHandler(
    ICurrentUser currentUser,
    IdentityDbContext db,
    IPropertyCountByTenant propertyCount)
    : IRequestHandler<GetMyTenantQuery, MeTenantDto>
{
    public async Task<MeTenantDto> Handle(GetMyTenantQuery request, CancellationToken ct)
    {
        if (currentUser.TenantId is null)
            throw new ForbiddenException("Caller has no tenant membership.");

        var tenant = await db.Tenants.FirstOrDefaultAsync(
            t => t.Id == currentUser.TenantId.Value, ct)
            ?? throw new NotFoundException("Tenant", currentUser.TenantId.Value);

        var count = await propertyCount.GetCountAsync(tenant.Id, ct);

        var dto = new MeTenantDto(
            tenant.Id, tenant.Slug, tenant.DisplayName, tenant.Status,
            tenant.DefaultCurrency, tenant.PlatformFeeBps,
            tenant.StripeAccountStatus, tenant.ChargesEnabled, tenant.PayoutsEnabled,
            HasStripeAccount: tenant.StripeAccountId is not null,
            PropertyCount: count,
            Onboarding: default!);   // populated next

        return dto with
        {
            Onboarding = new OnboardingProgressDto(
                IsComplete: OnboardingProgress.DeriveIsComplete(dto),
                NextStep: OnboardingProgress.DeriveNextStep(dto)),
        };
    }
}
```

The static `OnboardingProgress` class holds the two derivation functions for unit-testability (the §5 Step 2 unit tests exercise all 7 switch branches).

---

## 5. Step-by-step TDD plan (Red → Green)

Every step is red-first. Red commit + green commit are tracked in the §11 ledger.

### Step 1 — `MeTenantDto` + `OnboardingProgressDto` contract shapes (XS, ~1h)

**Tests (red first)** — `tests/VrBook.Architecture.Tests/MeTenantDtoShapeTests.cs`:

- `MeTenantDto_exists_in_VrBook_Contracts_Dtos` — reflects on type, asserts file location.
- `OnboardingProgressDto_exists_in_VrBook_Contracts_Dtos` — same.
- `MeTenantDto_has_Onboarding_property_of_type_OnboardingProgressDto` — sentinel reflection.
- `Both_records_are_sealed` — `typeof(MeTenantDto).IsSealed`.

**Min implementation**: extend `src/VrBook.Contracts/Dtos/Identity.cs` with the two records per §4.1.

**Refactor**: none.

**§3 cross-reference**: §3.2 (D2), §4.1.

### Step 2 — `OnboardingProgress` derivation logic (S, ~1.5h)

**Tests (red first)** — `tests/VrBook.Modules.Identity.UnitTests/Application/OnboardingProgressTests.cs` — one `[Theory]` per switch branch in `DeriveNextStep`:

- `[InlineData(false, 0, "PendingOnboarding", "Welcome")]`
- `[InlineData(false, 1, "PendingOnboarding", "ConnectStripe")]`
- `[InlineData(false, 5, "PendingOnboarding", "ConnectStripe")]`
- `[InlineData(true, 0, "PendingOnboarding", "CreateProperty")]`
- `[InlineData(true, 1, "Active", "Done")]`
- `[InlineData(true, 1, "PendingOnboarding", "AwaitingVerification")]`
- `[InlineData(true, 1, "Suspended", "AwaitingVerification")]`
- `[InlineData(true, 1, "Closed", "Done")]`

Plus three `IsComplete` facts: `[true, 1, "Active"] → true`, `[true, 0, "Active"] → false`, `[false, 1, "Active"] → false`.

**Min implementation**: create `src/Modules/VrBook.Modules.Identity/Application/Tenants/Common/OnboardingProgress.cs` with the static derivation functions per §4.1.

**Refactor**: extract a small `OnboardingNextSteps` constants holder if the magic strings appear elsewhere.

**§3 cross-reference**: §3.1 (D1), §4.1.

### Step 3 — `IPropertyCountByTenant` cross-module read (S, ~1.5h)

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Catalog/PropertyCountByTenantTests.cs`:

- `Returns_0_for_tenant_with_no_properties` — seed a tenant with zero properties, assert 0.
- `Returns_correct_count_for_tenant_with_active_and_inactive_properties` — seed 2 active + 1 deleted (`DeletedAt != null`), assert 2 (the deleted is excluded).
- `Returns_count_scoped_to_tenant` — seed tenant A with 3 properties, tenant B with 5; assert A → 3, B → 5 (cross-tenant isolation).

**Min implementation**:

1. New file `src/VrBook.Contracts/Interfaces/IPropertyCountByTenant.cs` (the interface).
2. New file `src/Modules/VrBook.Modules.Catalog/Infrastructure/PropertyCountByTenant.cs` (the EF impl).
3. Edit `src/Modules/VrBook.Modules.Catalog/CatalogModule.cs` — register `services.AddScoped<IPropertyCountByTenant, PropertyCountByTenant>();`.

**Refactor**: none.

**§3 cross-reference**: §4.2.

### Step 4 — `GetMyTenantQuery` + handler + endpoint (M, ~3h)

**Tests (red first)** — three test classes:

- `tests/VrBook.Modules.Identity.UnitTests/Application/GetMyTenantHandlerTests.cs`:
  - `Throws_ForbiddenException_when_currentUser_TenantId_is_null`.
  - `Throws_NotFoundException_when_tenant_row_missing`.
  - `Returns_MeTenantDto_with_all_fields_populated_from_tenant_and_count`.
  - `Onboarding_isComplete_true_when_active_and_one_property_and_has_stripe_account`.
  - `Onboarding_nextStep_is_CreateProperty_when_zero_properties_and_has_stripe_account` (edge case).
  - `HasStripeAccount_is_false_when_StripeAccountId_is_null`.
- `tests/VrBook.Api.IntegrationTests/Identity/GetMyTenantEndpointTests.cs`:
  - `Endpoint_returns_401_for_anonymous_caller`.
  - `Endpoint_returns_403_for_authenticated_caller_with_no_tenant_membership` — DevAuth as Guest persona.
  - `Endpoint_returns_200_with_dto_for_Owner_persona_in_default_tenant`.
  - `Endpoint_sets_Cache_Control_no_store_header`.
- `tests/VrBook.Architecture.Tests/MeTenantQueryShapeTests.cs`:
  - `GetMyTenantQuery_does_not_implement_ITenantScoped` — sentinel; it derives tenant id from `ICurrentUser`, not the request, so the `TenantAuthorizationBehavior` is not the auth surface here (the controller's `[Authorize]` is). Pin the negative assertion so a future contributor doesn't accidentally add `ITenantScoped` and break behavior.

**Min implementation**:

1. New file `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/GetMyTenantQuery.cs`:
   ```csharp
   public sealed record GetMyTenantQuery : IRequest<MeTenantDto>;
   ```
2. New file `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/GetMyTenantHandler.cs` per §4.3.
3. Edit `src/VrBook.Api/Controllers/IdentityController.cs` — add a second action:
   ```csharp
   [HttpGet("tenant")]
   [Authorize(Roles = "Owner,Admin")]
   [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
   [SwaggerOperation(Summary = "Get the caller's tenant + onboarding progress (OPS.M.7).")]
   [ProducesResponseType(typeof(MeTenantDto), StatusCodes.Status200OK)]
   [ProducesResponseType(StatusCodes.Status401Unauthorized)]
   [ProducesResponseType(StatusCodes.Status403Forbidden)]
   public async Task<ActionResult<MeTenantDto>> GetTenant(CancellationToken ct) =>
       Ok(await mediator.Send(new GetMyTenantQuery(), ct));
   ```

**Refactor**: none.

**§3 cross-reference**: §3.2 (D2), §3.5 (D5), §3.12 (D12), §4.1-§4.3.

### Step 5 — Web API client + react-query hooks (S, ~2h)

**Tests (red first)** — `web/src/lib/api/tenant.test.ts` + `web/src/hooks/useMyTenant.test.tsx`:

- `tenant.test.ts`:
  - `getMyTenant_calls_GET_api_v1_me_tenant` (using `vi.fn()` mock of fetch).
  - `onboardTenantStripe_calls_POST_with_tenant_id_in_url`.
  - `generateStripeAccountLink_calls_POST_with_tenant_id_in_url`.
  - `openStripeLoginLink_calls_POST_with_tenant_id_in_url`.
  - **Cross-tenant safety**: `every_function_takes_tenantId_as_first_argument_only` — a structural assertion (no global default, no env-var read for tenant id).
- `useMyTenant.test.tsx`:
  - `useMyTenant_initially_returns_isLoading_true`.
  - `useMyTenant_calls_getMyTenant_once_on_mount`.
  - `useMyTenant_with_poll_option_refetches_at_the_specified_interval` — uses `vi.useFakeTimers()` to advance time.
  - `useMyTenant_stops_polling_when_isComplete_becomes_true`.
  - `useMyTenant_stops_polling_after_pollMax_attempts_and_marks_polling_exhausted`.
  - `useMyTenant_cancels_polling_on_unmount`.

**Min implementation**:

1. New file `web/src/lib/api/tenant.ts`:
   ```ts
   import { apiFetch } from './client';

   export interface MeTenant { /* mirrors MeTenantDto */ }
   export interface OnboardingProgress { /* mirrors OnboardingProgressDto */ }
   export const getMyTenant = (): Promise<MeTenant> => apiFetch<MeTenant>('/api/v1/me/tenant');
   export const onboardTenantStripe = (tenantId: string, country = 'US') =>
     apiFetch<{ stripeAccountId: string }>(
       `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/onboard`,
       { method: 'POST', body: { country } });
   export const generateStripeAccountLink = (tenantId: string) =>
     apiFetch<{ url: string; expiresAt: string }>(
       `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/account-link`,
       { method: 'POST' });
   export const openStripeLoginLink = (tenantId: string) =>
     apiFetch<{ url: string }>(
       `/api/v1/admin/tenants/${encodeURIComponent(tenantId)}/stripe/login-link`,
       { method: 'POST' });
   ```
2. New file `web/src/hooks/useMyTenant.ts`:
   ```ts
   export interface UseMyTenantOptions {
     readonly pollIntervalMs?: number;   // default undefined (no poll)
     readonly pollMax?: number;          // default 30
     readonly stopWhen?: (t: MeTenant) => boolean;
   }
   export const useMyTenant = (opts: UseMyTenantOptions = {}) => {
     const query = useQuery({
       queryKey: ['me', 'tenant'],
       queryFn: getMyTenant,
       staleTime: 0,
       refetchInterval: (data) => {
         if (opts.pollIntervalMs === undefined) return false;
         if (data && opts.stopWhen?.(data)) return false;
         return opts.pollIntervalMs;
       },
     });
     // wrapper for pollMax (count attempts, return exhausted state)
     return { ...query, /* pollAttempts, isExhausted */ };
   };
   ```

**Refactor**: none.

**§3 cross-reference**: §3.6 (D6), §3.7 (D7).

### Step 6 — `useStripeOnboardingFlow()` hook (S, ~2h)

**Tests (red first)** — `web/src/hooks/useStripeOnboardingFlow.test.tsx`:

- `Click_when_hasStripeAccount_false_calls_onboard_then_account_link_then_redirect`.
- `Click_when_hasStripeAccount_true_skips_onboard_calls_only_account_link_then_redirect`.
- `Click_redirects_window_location_to_link_url`.
- `If_account_link_call_fails_sets_error_state_and_does_not_redirect`.
- `If_onboard_call_fails_sets_error_state_and_does_not_call_account_link`.
- `In_flight_state_isLoading_returns_true`.

**Min implementation**:

```ts
// web/src/hooks/useStripeOnboardingFlow.ts
export const useStripeOnboardingFlow = (tenant: MeTenant) => {
  const [state, setState] = useState<{ status: 'idle' | 'loading' | 'error', error?: string }>({ status: 'idle' });
  const start = useCallback(async () => {
    setState({ status: 'loading' });
    try {
      if (!tenant.hasStripeAccount) await onboardTenantStripe(tenant.id);
      const { url } = await generateStripeAccountLink(tenant.id);
      window.location.href = url;
    } catch (e) {
      setState({ status: 'error', error: e instanceof Error ? e.message : 'Stripe call failed' });
    }
  }, [tenant.id, tenant.hasStripeAccount]);
  return { start, ...state };
};
```

**§3 cross-reference**: §3.3 (D3).

### Step 7 — Wizard page + step cards + dashboard gate (M, ~4h)

**Tests (red first)** — `web/src/app/admin/onboarding/page.test.tsx`:

- `Renders_WelcomeStepCard_when_tenant_state_is_zero_zero_pending` (propertyCount=0, !hasStripeAccount, PendingOnboarding).
- `Renders_ConnectStripeStepCard_when_propertyCount_geq_1_and_no_stripe_account`.
- `Renders_DoneStepCard_when_isComplete_true` — but only the transition state; the dashboard gate would normally have redirected by now.
- `Renders_action_required_banner_when_status_is_Suspended_and_hasStripeAccount`.
- `Step_indicator_shows_step_2_of_3_when_on_create_property_step`.
- `Step_indicator_has_aria_current_step_on_the_active_step`.
- `Connect_Stripe_button_calls_useStripeOnboardingFlow_start_on_click`.
- `Add_property_button_links_to_admin_properties_new_with_from_onboarding_query`.

Plus `web/src/app/admin/layout.test.tsx`:

- `AdminShell_redirects_to_admin_onboarding_when_isComplete_false_and_path_is_not_exempt`.
- `AdminShell_does_NOT_redirect_when_path_is_admin_onboarding` (exempt list).
- `AdminShell_does_NOT_redirect_when_path_is_admin_properties_new` (exempt list).
- `AdminShell_renders_Continue_setup_link_when_isComplete_false`.
- `AdminShell_does_NOT_render_Continue_setup_link_when_isComplete_true`.
- `AdminShell_shows_Access_denied_surface_when_useMyTenant_errors_with_403`.

**Min implementation**:

1. New file `web/src/app/admin/onboarding/page.tsx` per §3.1's three-step layout (verified inline component pattern matches `properties/new/page.tsx`).
2. New file `web/src/components/onboarding/WizardStepIndicator.tsx` — the step-row with the dots.
3. Refactor `web/src/app/admin/layout.tsx` into the thin server-component + `<AdminShell>` client component split (§3.5).
4. Edit `web/src/components/layout/AdminSidebar.tsx` — add a conditional "Continue setup" link at the top of the nav (only when `me.tenant.onboarding.isComplete === false`); read state via `useMyTenant()`.

**Refactor**: extract the step-card primitive `<WizardCard step={n} active={true|false} done={true|false} title="…">` if the three cards diverge less than expected (likely).

**§3 cross-reference**: §3.1 (D1), §3.5 (D5), §3.6 (D6), §3.10 (D10), §3.11 (D11).

### Step 8 — Stripe return-trip pages (S, ~2h)

**Tests (red first)** — `web/src/app/admin/onboarding/complete/page.test.tsx` + `…/refresh/page.test.tsx`:

- `complete.test`:
  - `Shows_verifying_message_on_mount_and_starts_polling`.
  - `Polls_useMyTenant_every_1000ms_up_to_30_times`.
  - `Transitions_to_Done_card_when_isComplete_becomes_true`.
  - `Shows_action_required_when_status_becomes_Suspended_during_polling`.
  - `Shows_refresh_now_button_when_polling_exhausts_at_30_attempts`.
  - `Refresh_now_button_resets_polling_attempts_and_starts_again`.
  - `Has_aria_live_polite_region_announcing_polling_state` (a11y).
- `refresh.test`:
  - `Calls_generateStripeAccountLink_on_mount`.
  - `Redirects_window_location_to_the_new_link_url`.
  - `Falls_back_to_admin_onboarding_if_generateStripeAccountLink_fails`.
  - `Does_not_call_onboard_again_since_hasStripeAccount_is_true_by_design_on_this_path`.

**Min implementation**:

1. New file `web/src/app/admin/onboarding/complete/page.tsx`:
   ```tsx
   'use client';
   export default function OnboardingCompletePage() {
     const { data, isExhausted, refetch } = useMyTenant({
       pollIntervalMs: 1000,
       pollMax: 30,
       stopWhen: (t) => t.onboarding.isComplete || t.status === 'Suspended',
     });
     /* render */
   }
   ```
2. New file `web/src/app/admin/onboarding/refresh/page.tsx`:
   ```tsx
   'use client';
   export default function OnboardingRefreshPage() {
     const { data } = useMyTenant();
     useEffect(() => {
       if (!data) return;
       generateStripeAccountLink(data.id)
         .then(({ url }) => { window.location.href = url; })
         .catch(() => router.replace('/admin/onboarding'));
     }, [data]);
     return <p>Refreshing your onboarding link…</p>;
   }
   ```

**§3 cross-reference**: §3.4 (D4), §3.7 (D7).

### Step 9 — Playwright e2e happy path (S, ~2h)

**Tests (red first)** — `web/tests/e2e/onboarding.e2e.spec.ts`:

- `Owner_lands_on_dashboard_with_zero_properties_is_redirected_to_admin_onboarding`.
- `Wizard_shows_Step_1_Welcome_initially_for_fresh_tenant`.
- `Clicking_Add_property_navigates_to_admin_properties_new_with_from_onboarding_query`.
- `After_property_created_returning_to_admin_onboarding_advances_to_Step_3_Connect_Stripe`.
- `Clicking_Connect_Stripe_calls_the_two_API_endpoints_in_order` — mocks Stripe (via API-test-mode or a WireMock-style intercept on the API gateway) so the redirect lands on a Playwright-controlled stub.
- `Stripe_return_trip_to_complete_page_polls_until_isComplete_true` — simulates the webhook-induced flag flip via a test-only API endpoint that fires `Tenant.UpdateStripeAccountReadiness(true, true)`.
- `Done_card_appears_with_Go_to_dashboard_CTA`.

The Playwright config is already in place (`web/playwright.config.ts`) with `tests/e2e` as the test dir. Smoke vs full e2e split: the onboarding test is a full e2e (not a smoke), so the file ends in `.e2e.spec.ts`.

**Min implementation**: only the test file; no production code added at this step.

**§3 cross-reference**: §6.

### Step 10 — Operator-manual welcome-email runbook + Slice 4 swap doc (XS, ~0.5h)

**No tests** — pure docs.

**Min implementation**: new file `docs/runbooks/tenant-onboarding-welcome-email.md` per §3.8 D8 — list of fields the ops Powershell needs, sample `az communication email send` invocation, weekly batch instructions, and a "Slice 4 swap" section that documents how to remove this runbook entry once Slice 4 ships.

**§3 cross-reference**: §3.8 (D8).

---

## 6. UI test posture — Vitest + RTL for components, Playwright for e2e

The web stack already has:

- **Vitest 2.1.5** (verified `web/package.json:60`) — used for unit / component tests. Setup file at `web/vitest.setup.ts`.
- **@testing-library/react 16.0.1** + **@testing-library/user-event 14.5.2** + **@testing-library/jest-dom 6.6.3** — verified `web/package.json:41-43`.
- **jsdom 25.0.1** — verified `web/package.json:53`.
- **Playwright 1.49.0** — verified `web/package.json:39`. Config at `web/playwright.config.ts` points at `./tests/e2e`. Two projects: `smoke` (matching `*.smoke.spec.ts`) and `e2e` (default).

**Decision: M.7 uses both Vitest+RTL (for component tests) and Playwright (for e2e).** Reasoning:

- **Component tests (Vitest+RTL)** cover the wizard's state machine — what renders for each `MeTenantDto` shape, how the cards transition, accessibility attributes, button click handlers. These run in milliseconds and pin the unit-level behavior.
- **One Playwright e2e** (`onboarding.e2e.spec.ts`) covers the full happy path — a fresh Owner lands on `/admin`, gets redirected, walks through Add Property + Connect Stripe (with a stubbed Stripe endpoint), and ends on the Done card. This catches integration-level regressions the component tests can't see (the layout gate, the navigation flow, the polling loop's react-query interaction with real time).
- **No new test framework**. Both Vitest and Playwright are already in the project. The test files follow existing patterns (`*.test.tsx` for component, `*.e2e.spec.ts` under `tests/e2e/` for Playwright). The existing `web/src/lib/csv.test.ts` and `web/src/lib/auth/msalConfig.test.ts` are the in-tree pattern templates.

**Test posture summary**:

| Surface | Test stack | Count |
|---|---|---|
| `MeTenantDto` shape | xUnit + reflection (arch) | 4 facts |
| `OnboardingProgress` derivation | xUnit (`[Theory]`) | 11 facts |
| `IPropertyCountByTenant` impl | xUnit + Postgres testcontainer | 3 facts |
| `GetMyTenantHandler` | xUnit + mocks | 6 facts |
| `GET /api/v1/me/tenant` | xUnit + integration fixture | 4 facts |
| `MeTenantQueryShapeTests` arch | xUnit | 1 fact |
| Web API client (`getMyTenant`, etc.) | Vitest + fetch mock | 5 facts |
| `useMyTenant` hook | Vitest + RTL renderHook + fake timers | 6 facts |
| `useStripeOnboardingFlow` hook | Vitest + RTL renderHook | 6 facts |
| Wizard page + step cards | Vitest + RTL | 8 facts |
| Admin layout gate (`AdminShell`) | Vitest + RTL | 6 facts |
| Return-trip pages (complete + refresh) | Vitest + RTL | 11 facts |
| E2E happy path | Playwright | 1 spec, ~7 assertions |

**Total**: ~78 facts across 13 test classes/files (well within the M.5/M.6 baseline).

---

## 7. Cross-tenant safety review

The wizard's API surface MUST NEVER trust a tenant id from anywhere other than `ICurrentUser`. The audit:

| Call site | Tenant id source | Safe? |
|---|---|---|
| `GET /api/v1/me/tenant` handler | `currentUser.TenantId` (server-side) | ✅ — caller-derived only |
| `POST /api/v1/admin/tenants/{tenantId}/stripe/onboard` | Web client passes `data.tenant.id` from `useMyTenant()`. Server's `TenantsAdminController.cs:37` ignores the URL value (`_ = tenantId`) and calls `CallerTenantId()`. The `TenantAuthorizationBehavior` (OPS.M.4) rejects mismatch. | ✅ — server overrides URL value |
| `POST /api/v1/admin/tenants/{tenantId}/stripe/account-link` | Same | ✅ |
| `POST /api/v1/admin/tenants/{tenantId}/stripe/login-link` | Same | ✅ |
| Wizard's `useStripeOnboardingFlow(tenant)` hook | Receives `MeTenantDto` from `useMyTenant()`. `tenant.id` came from the server, which derived it from `currentUser.TenantId`. | ✅ — closed loop, no URL/query/cookie tenant input ever flows to the client-side tenant id |
| Dashboard's `<AdminShell>` redirect | Reads `useMyTenant().data.onboarding.isComplete`. No tenant id involvement. | ✅ |

**The CRITICAL anti-pattern to forbid**: the wizard MUST NOT read a tenant id from `useSearchParams()`, `window.location.search`, a cookie, or a route parameter. The §9 best-practices arch test enforces this via a Vitest test that greps the wizard surface for `useParams|useSearchParams|window.location.search`. See §9 row 2.

**Sentinel check**: the wizard's `tenant.id` value is ONLY ever sourced from `useMyTenant().data.id`. If a future refactor wires a `tenantId` URL parameter in, the test in Step 7 (`every_function_takes_tenantId_as_first_argument_only`) catches the structural drift.

**Decision: cross-tenant safety is achieved by (a) server-side `CallerTenantId()` in every onboarding endpoint, (b) closed-loop tenant id flow on the client (only ever from `useMyTenant()` payload), (c) a Vitest arch-test pattern matching against URL-source reads.**

---

## 8. Failure / partial states

Every state the wizard surface might be in, and what happens.

### 8.1 The state machine

```
                ┌─────────────────────┐
                │   Wizard route hit  │
                └──────────┬──────────┘
                           │
                  fetch /me/tenant
                           │
                ┌──────────┴──────────┐
                │      Loading        │
                │ (skeleton spinner)  │
                └──────────┬──────────┘
                           │
                ┌──────────┴──────────────────────┐
                │   Server response               │
                ├─────────────────────────────────┤
                │ 401 → /auth/callback             │
                │ 403 → Access denied surface      │
                │ 5xx → Connection-error card      │
                │ 200 → derive step from DTO       │
                └──────────┬──────────────────────┘
                           │ (200 branch)
                           ▼
        ┌──────────────────┴───────────────────┐
        │  nextStep ?                          │
        ├──────────────────────────────────────┤
        │ "Welcome"             → Step 1 card  │──── "Start setup →" ──┐
        │ "CreateProperty"      → Step 2 card  │                       │
        │ "ConnectStripe"       → Step 3 card  │                       │
        │ "AwaitingVerification" → Step 3      │                       │
        │   (+ "Continue verification →" CTA)   │                       │
        │ "Done"                → Step 4 card  │                       │
        └──────────────────────────────────────┘                       │
                                                                       │
   ┌───────────────────────────────────────────────────────────────────┘
   │
   ▼
 (CTAs branch out — see §8.2 below)
```

### 8.2 CTA branches

- **Step 1 "Start setup →"**: navigates within the same page; advances the visual focus to Step 2's card (smooth scroll + `focus()`).
- **Step 2 "Add property →"**: navigates to `/admin/properties/new?from=onboarding`. The property-create page (existing `web/src/app/admin/properties/new/page.tsx`) is NOT modified by M.7 — but its post-create redirect at `page.tsx:138` already navigates to `/admin/properties` on success. **M.7 edit: read `searchParams.get('from')`; if `=== 'onboarding'`, redirect to `/admin/onboarding` instead of `/admin/properties`.** One-line edit.
- **Step 3 "Connect Stripe →"**: fires `useStripeOnboardingFlow().start()`; redirects to Stripe-hosted URL.
- **Step 3 "Continue verification →"** (when status is PendingOnboarding but AccountLink expired): fires `generateStripeAccountLink()` and redirects to the new URL.
- **Step 3 "Re-link Stripe →"** (when status is Suspended): same as Connect Stripe — re-runs `account-link` against the existing account.
- **Step 4 "Go to dashboard →"**: navigates to `/admin`.
- **Step 4 "Open Stripe dashboard ↗"**: opens `openStripeLoginLink()` URL in a new tab.

### 8.3 Failure modes

| Failure | User-visible surface | Recovery |
|---|---|---|
| `POST /stripe/onboard` returns 500 | Inline error band on Step 3: "Couldn't create your Stripe account — try again." | Button re-enables; click to retry |
| `POST /stripe/account-link` returns 500 | Inline error band: "Couldn't generate the onboarding link — try again." | Same |
| Stripe returns user without completing form (returns to `/admin/onboarding/complete` but `chargesEnabled=false`) | Polling loop runs 30s; if still not Active, "Verification is taking longer than usual" + manual refresh | User clicks "Refresh now" or contacts support |
| Stripe AccountLink expired (>5min) and user clicked it | Stripe redirects to `/admin/onboarding/refresh` → re-issues link → redirects to Stripe again | Automatic; no user action |
| User closes the tab during Stripe form | Server state unchanged; user can re-open the dashboard → gate sends to `/admin/onboarding` → resumes at Step 3 with "Continue Stripe onboarding →" CTA | Automatic |
| User completes Stripe but webhook never arrives (e.g. ACS down) | Polling exhausts at 30s with "Verification is taking longer than usual" copy | Manual "Refresh now" retries; if persistent, support intervention; ops can manually flip `chargesEnabled`/`payoutsEnabled` via SQL (documented in runbook) |
| User's Stripe account ends up Restricted (charges_enabled false) post-Active | Status flips to Suspended; the dashboard gate kicks in again on next page load; the wizard now shows "Action required: your Stripe account is restricted" | User clicks "Re-link Stripe →" which re-runs `account-link` on the existing account; Stripe's UI walks them through the missing capability |
| Caller is a Guest (no tenant membership) | `useMyTenant()` returns 403; the AdminShell renders an "Access denied — you don't have a tenant" surface with a "Contact support" CTA | (Manual onboarding via support) |
| Caller's tenant is `Closed` | `MeTenantDto.status === 'Closed'`; `isComplete` derives true (Closed shouldn't show the wizard); the gate doesn't redirect; the user lands on the dashboard with all features disabled (existing Closed-state surface from OPS.M.5) | N/A — out of scope for the wizard |
| Polling loop's request itself fails (network) | Single error-banner above the polling status; react-query's retry default attempts twice before surfacing | If the user is offline, the error is correct |

### 8.4 The `?from=onboarding` query param on `/admin/properties/new`

The wizard's Step 2 CTA navigates with `?from=onboarding`. The existing property-create page submits and currently routes to `/admin/properties` on success. **M.7's edit at `web/src/app/admin/properties/new/page.tsx`** (one-line):

```tsx
// Before (line 138):
router.push('/admin/properties');

// After:
const from = useSearchParams().get('from');
router.push(from === 'onboarding' ? '/admin/onboarding' : '/admin/properties');
```

This is a 4-line addition: import `useSearchParams`, read it, branch on the value. Tested as part of Step 7's component tests.

---

## 9. Implementation guard rails (best practices)

Every M.7 PR must satisfy these. Arch tests enforce items marked **[arch]**; code review enforces the rest.

1. **No `fetch` calls bypass `apiFetch`** — every API request from the wizard goes through `web/src/lib/api/client.ts`'s `apiFetch`, which injects the bearer token and propagates `traceparent`. **[arch — Vitest test]** `tests/grep/no-direct-fetch-in-onboarding.test.ts` greps for `fetch\(` in `web/src/app/admin/onboarding/**` and `web/src/components/onboarding/**` and asserts zero matches.
2. **No URL/query-derived `tenantId` in client code** — the wizard never reads `tenantId` from `useSearchParams()`, `useParams()`, `window.location.search`, or a cookie. **[arch — Vitest test]** `tests/grep/no-url-tenant-read.test.ts` greps for `tenantId.*search|search.*tenantId|params\.tenantId|cookie.*tenantId` in `web/src/app/admin/onboarding/**` and `web/src/hooks/useStripeOnboardingFlow.ts` and asserts zero matches.
3. **The wizard state machine is documented in §8 and pinned in code** — every conditional render in `page.tsx` corresponds to a row in §8.1. The component tests in Step 7 cover all six rows; missing a row fails CI.
4. **Accessibility per §3.11** — Wizard must include: `aria-current="step"` on the active step indicator, `<section aria-labelledby="…">` on each card, `aria-live="polite"` on the polling-status copy. **[arch — Vitest test]** `web/src/app/admin/onboarding/page.test.tsx` `Has_aria_attributes_per_d11`.
5. **`useMyTenant()` polling honors `pollMax`** — never infinite. **[arch — Vitest test]** Step 5's `useMyTenant_stops_polling_after_pollMax_attempts` test pins it.
6. **Stripe redirect uses `window.location.href`, not `router.push`** — Next.js's `router.push` only handles same-origin URLs. **[code review]** — easy to regress; reviewers check.
7. **No hard-coded base URLs** — every URL is composed from `process.env.NEXT_PUBLIC_API_BASE_URL` (server URLs) or relative paths (client routes). The Stripe URL is opaque (whatever the server returns). **[code review]**
8. **Cross-tenant safety per §7** — the wizard's `tenant.id` is sourced ONLY from `useMyTenant().data.id`. The closed loop is documented in code with a comment block in `tenant.ts` (header doc). **[code review]** — high-stakes; the test in row 2 plus the closed-loop comment is the contract.
9. **`Cache-Control: no-store` on `GET /api/v1/me/tenant`** — set via `[ResponseCache(NoStore = true)]` on the controller action. Polling against a cached response is incorrect. **[arch — integration test]** Step 4's `Endpoint_sets_Cache_Control_no_store_header` pins it.
10. **Structured logging on the server side** — `GetMyTenantHandler` logs at `Information` with fields `tenant_id`, `caller_user_id`, `property_count`, `stripe_account_status`, `is_complete`. **[code review]** — same pattern as OPS.M.5 §10 rule 6.

**Arch tests summary** (count includes Vitest grep tests):

- `MeTenantDtoShapeTests` (Step 1) — 4 facts.
- `OnboardingProgressTests` (Step 2) — 11 facts.
- `PropertyCountByTenantTests` (Step 3) — 3 facts.
- `GetMyTenantHandlerTests` + `GetMyTenantEndpointTests` + `MeTenantQueryShapeTests` (Step 4) — 11 facts.
- `tenant.test.ts` + `useMyTenant.test.tsx` (Step 5) — 11 facts.
- `useStripeOnboardingFlow.test.tsx` (Step 6) — 6 facts.
- `page.test.tsx` + `layout.test.tsx` (Step 7) — 14 facts.
- `complete/page.test.tsx` + `refresh/page.test.tsx` (Step 8) — 11 facts.
- `onboarding.e2e.spec.ts` (Step 9) — 1 spec, ~7 assertions.
- Grep arch tests (rule 1, 2) — 2 facts.

**Total: ~78 facts across 13 test classes/files**.

---

## 10. (Reserved — no removed sections)

This rev does not promote any deferred decision, so no rev-summary block needed. All decisions in §3 are locked at first authoring.

---

## 11. Close-out — TBD

### Per-step commit ledger

| Step | Module(s) | Red commit (tests fail) | Green commit (impl) | Files touched |
|---|---|---|---|---|
| 1 | Contracts | _pending_ | _pending_ | `Identity.cs` (extend), `MeTenantDtoShapeTests.cs` |
| 2 | Identity | _pending_ | _pending_ | `OnboardingProgress.cs`, `OnboardingProgressTests.cs` |
| 3 | Contracts + Catalog | _pending_ | _pending_ | `IPropertyCountByTenant.cs`, `PropertyCountByTenant.cs`, `CatalogModule.cs`, `PropertyCountByTenantTests.cs` |
| 4 | Identity + Api | _pending_ | _pending_ | `GetMyTenantQuery.cs`, `GetMyTenantHandler.cs`, `IdentityController.cs` (extend), 3 test classes |
| 5 | Web | _pending_ | _pending_ | `tenant.ts`, `useMyTenant.ts`, 2 test files |
| 6 | Web | _pending_ | _pending_ | `useStripeOnboardingFlow.ts`, 1 test file |
| 7 | Web | _pending_ | _pending_ | `admin/onboarding/page.tsx`, `WizardStepIndicator.tsx`, `admin/layout.tsx` refactor, `AdminSidebar.tsx` edit, `properties/new/page.tsx` edit, 2 test files |
| 8 | Web | _pending_ | _pending_ | `admin/onboarding/complete/page.tsx`, `admin/onboarding/refresh/page.tsx`, 2 test files |
| 9 | Web | _pending_ | _pending_ | `tests/e2e/onboarding.e2e.spec.ts` (Playwright) |
| 10 | Docs | _pending_ | _pending_ | `docs/runbooks/tenant-onboarding-welcome-email.md` (new) |

**Deploy-time check (not a Step)**:

1. Stripe `OnboardingReturnUrl` Key Vault value updated to `${Frontend:BaseUrl}/admin/onboarding/complete` — date / actor / value-before / value-after recorded here.
2. Stripe `OnboardingRefreshUrl` Key Vault value updated to `${Frontend:BaseUrl}/admin/onboarding/refresh` — same.
3. Container App revision restart confirmed via `az containerapp revision list`.

### Deviations from this plan

_None recorded yet — populate during implementation._

### Forward links

- **Slice OPS.M.8 — Super Admin console**: M.8 ships a cross-tenant view of the same `MeTenantDto` shape (renamed to `TenantDto` server-side, but the same fields). The DTO shape is stable; M.8 reuses §4.1 verbatim with one additional field `OwnerEmail` for the operator's contact UX. The `OnboardingProgress` derivation in §4.1 is reused.
- **Slice OPS.M.9 — RLS policies**: M.7's `GET /api/v1/me/tenant` reads from `identity.tenants` (caller-scoped). M.9's RLS policy on `identity.tenants` will use `tenant_id = current_setting('app.tenant_id')::uuid`. The handler runs under the caller's app DB connection (NOT bypass), so the RLS policy works naturally. **No M.7 code change needed.**
- **Slice OPS.M.10 — Cross-tenant isolation test pack**: M.10 will add the `GET /api/v1/me/tenant` endpoint to its sweep (assert that calling with tenant A's bearer returns tenant A's DTO, and tenant B's bearer returns tenant B's). The endpoint's caller-scoped shape makes this a one-line addition to the M.10 test list.
- **Slice 4 — Notifications that actually send**: per §3.8 D8, Slice 4 adds `TenantNotificationHandlers.cs` subscribing to `INotificationHandler<TenantStripeOnboarded>` and renders the `tenant.welcome` ACS template. Single PR; the runbook entry from §3.8 / Step 10 gets a deprecated header.
- **Phase 4 / Slice 10 — Multi-supplier OTA**: the wizard's "Connect Stripe" step becomes "Connect Stripe for each supplier role you take on" (Property Host / Activity Operator / Car Hire). The state derivation (§4.1) gains a second axis (`per_role_status`). The `IPropertyCountByTenant` lookup expands to `IReservableCountByTenant` (rooms + activities + vehicles). M.7's component tree (the WizardStepIndicator + step-card primitive) is reusable. **No M.7 code is wasted by the Phase 4 shape change.**
- **Phase 2 — SignalR-pushed verification**: the polling loop in `/admin/onboarding/complete` can be swapped for a SignalR subscription to a `tenant.{id}.stripe.updated` channel. The `useMyTenant()` hook's callback shape is the same either way. ~1 day swap when SignalR Serverless is wired (Slice 7).
- **Phase 2 — Per-tenant branding on the Stripe-hosted page**: Stripe Custom Connect with per-tenant `branding.business_url` / `branding.primary_color`. Belongs in Slice OPS.8 (custom-domain DKIM/SPF for ACS) — the branding work piggybacks on the domain ownership the user proves there.

---

## Appendix A — Verified codebase claims

Every concrete file/class name in §3-§4 is grounded in one of these. If any line drifts, the plan's *contract claim* is the contract — adjust the file path, not the contract.

| Claim | Source |
|---|---|
| Web framework is Next.js 14 (App Router) | `web/package.json:29` (`"next": "14.2.18"`) |
| React 18.3.1 | `web/package.json:31` |
| React Query 5.62.0 | `web/package.json:26` |
| MSAL Browser 3.27.0 + MSAL React 2.2.0 | `web/package.json:18-19` |
| Stripe.js dep present (for client-side Stripe Elements if ever needed) | `web/package.json:24-25` — NOT used by M.7; the wizard only redirects to Stripe-hosted pages |
| Tailwind 3.4.15 + Lucide-react 0.460.0 | `web/package.json:28, :58` |
| Vitest 2.1.5 + RTL 16.0.1 + jsdom 25.0.1 + user-event 14.5.2 | `web/package.json:41-43, :53, :60` |
| Playwright 1.49.0 | `web/package.json:39` |
| Playwright test dir is `./tests/e2e`, no current e2e files | `web/playwright.config.ts:6` + `find web/tests/e2e` returns empty |
| Vitest test glob is `src/**/*.{test,spec}.{ts,tsx}` | `web/vitest.config.ts:13` |
| Existing component-library: none — plain Tailwind | `web/src/components/ui/` is empty; `find web/src` for `shadcn|MUI|Radix` returns zero |
| Existing apiFetch is the only HTTP wrapper | `web/src/lib/api/client.ts:89-150` |
| `setTokenProvider` is wired in Providers.tsx | `web/src/components/Providers.tsx:66-79` |
| `useAuth()` exposes `user.isOwner` / `user.isAdmin` | `web/src/lib/auth/useAuth.ts:26-29` |
| Admin shell currently has no auth-aware logic | `web/src/app/admin/layout.tsx:1-28` (server component, no useEffect) |
| Existing property-create page redirects to `/admin/properties` on success | `web/src/app/admin/properties/new/page.tsx:138` |
| Admin sidebar exists and is a 'use client' component | `web/src/components/layout/AdminSidebar.tsx:1` |
| `GET /api/v1/me` exists and returns `UserDto` | `src/VrBook.Api/Controllers/IdentityController.cs:14-25` |
| `UserDto` does NOT include `TenantId` today | `src/VrBook.Contracts/Dtos/Identity.cs:4-13` |
| `ICurrentUser.TenantId` (nullable Guid) exists | `src/VrBook.Contracts/Interfaces/ICurrentUser.cs:27` |
| `TenantsAdminController` route is `api/v1/admin/tenants/{tenantId:guid}` | `src/VrBook.Api/Controllers/TenantsAdminController.cs:23` |
| `TenantsAdminController` always calls `CallerTenantId()`, never the URL `tenantId` | `src/VrBook.Api/Controllers/TenantsAdminController.cs:37, :50, :63, :76` |
| `TenantsAdminController` is `[Authorize(Roles="Owner,Admin")]` | `src/VrBook.Api/Controllers/TenantsAdminController.cs:25` |
| Stripe options have `OnboardingReturnUrl` + `OnboardingRefreshUrl` | `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeOptions.cs:18, :21` |
| `StripeGateway.CreateAccountLinkAsync` reads those URLs from options | `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeGateway.cs:224-225` |
| `Tenant` aggregate has `StripeAccountId`, `StripeAccountStatus`, `ChargesEnabled`, `PayoutsEnabled`, `PlatformFeeBps`, `Status`, `Slug`, `DisplayName`, `DefaultCurrency` | `src/Modules/VrBook.Modules.Identity/Domain/Tenant.cs:28-44` |
| `Tenant.UpdateStripeAccountReadiness` auto-transitions Active and raises `TenantStripeOnboarded` | `src/Modules/VrBook.Modules.Identity/Domain/Tenant.cs:161-179` |
| `OnboardTenantStripeCommand`, `GenerateStripeAccountLinkCommand`, `OpenStripeLoginLinkCommand` already exist | `src/Modules/VrBook.Modules.Identity/Application/Tenants/Commands/StripeOnboardingCommands.cs:16-26` |
| `PropertyCreated` event already carries `Guid TenantId` (no edit needed in M.7) | `src/VrBook.Contracts/Events/CatalogEvents.cs:7-12` |
| `Property.Create` raise site already passes `TenantId` | `src/Modules/VrBook.Modules.Catalog/Domain/Property.cs:116` |
| `Frontend:BaseUrl` config key exists (default `http://localhost:3000`) | `src/VrBook.Api/appsettings.json` "Frontend" section |
| ACS sender already in config (`donotreply@vrbook.example.com`) | `src/VrBook.Api/appsettings.json` "Acs" section |

---

## Appendix B — Open questions (none)

All decisions in §3 are locked. The brief mentioned three options for wizard shape (D1) and two for Stripe deep-link UX (D3); §3.1 and §3.3 lock both with reasoning.

**One soft-flag for the user's awareness, not an open question**: the brief said "the wizard MUST get the user onboarded before their first booking, but does not need to block property creation itself" (per OPS.M.5 §3.5 D5). §3.1 D1's lock is consistent with this — the wizard nudges Stripe onboarding heavily but does NOT prevent the user from creating a property (Step 2 of the wizard literally walks them through it). If the product team later wants a hard publish-gate ("can't publish a property without Stripe Active"), that's a one-line check in `PublishPropertyHandler` — a future product call, not an M.7 contract.

**Soft-flag for `?from=onboarding` URL contract**: M.7 introduces a small additive query-string convention on `/admin/properties/new`. If a future slice wants to wire other onboarding-style flows through the property-create page, the convention is `from=<slice-name>`. M.7's edit at `web/src/app/admin/properties/new/page.tsx:138` is generic enough to extend without breaking.

**If the user disagrees with §3.1 D1 (modal vs route)** they raise it against the §3.1 row; the lock holds otherwise. The Phase-2 swap from route to modal is feasible (the cards are pure components — they'd re-mount in a `<Dialog>`), but the dashboard-gate reasoning in §3.1 #1-#5 makes route the right Phase 1.5 call.
