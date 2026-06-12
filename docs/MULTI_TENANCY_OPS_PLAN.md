# OPS Multi-Tenancy Plan (Phase 1.5)

**Status**: Proposed — awaiting user review.
**Author**: System-architect consult, 2026-06-12.
**Lands**: OPS phase, after Slices 3-7 complete (per user's REPLAN Option A decision).

Scope: bring the SaaS-ification line items from proposal §22.4 forward into OPS so real owners can be onboarded, paid, and supported. Single platform-operator (Super Admin) over many tenants (Property Owners), each managing their own properties, bookings, payouts, calendars and conversations.

This plan supersedes informal notes — it is the contract for OPS.M.1 through OPS.M.10 below.

---

## 1. Tenancy Model

**Tenant is a separate aggregate, not a renamed Owner User.** Introduce `identity.tenants` as a first-class aggregate (`id`, `slug`, `display_name`, `status`, `created_at`, `default_currency`, `default_timezone`, `support_email`, `platform_fee_bps`, `stripe_account_id`, `stripe_account_status`, `suspended_reason`). A `User` has zero-or-more `TenantMembership` rows; one is marked `is_primary`. We keep `owner_user_id` on `Catalog.Property` (the principal who created it) but add `tenant_id` as the authoritative scoping column for everything that today reasons about ownership.

Why separate: (a) a tenant survives an owner-of-record changing email or transferring the business to a co-founder; (b) the Stripe Connect account belongs to the business, not the human; (c) per-tenant feature toggles and platform fee % are tenant attributes, not user attributes; (d) future "team members" (housekeeper, co-host) are trivially additive without re-shaping data.

**Role hierarchy** (claim values):

| Role | Scope | Notes |
|---|---|---|
| `super_admin` | platform | Maps to today's `IsAdmin`, renamed. ~3 internal humans. |
| `tenant_admin` | one tenant | Maps to today's `IsOwner`. The "Property Owner" persona. |
| `tenant_member` | one tenant | Deferred. Schema supports it; UI ships in Phase 2. |
| `guest` | none | Authenticated end user; no tenant claim. |

DevAuth's three personas stay in dev only; OPS.7 (real Entra) replaces them.

**Sign-up flow**: **invite-only by Super Admin for Phase 1.5**, self-serve in Phase 2. Reasons: KYC/Stripe onboarding has real fraud surface; the client is hand-curating their first 5-20 owners anyway; self-serve sign-up is one screen, not an architecture change — preserve the optionality.

**Lifecycle:** `PendingOnboarding` → `Active` → `Suspended` (Super Admin action; bookings frozen, existing payments still settle) → `Closed` (owner-initiated, 90-day soft-delete) → `Deleted` (purge after retention window).

---

## 2. Authentication & Authorization

**Single Entra External ID tenant with role + tenant claims**, not per-tenant Entra tenants. Per-tenant Entra is overkill, complicates token validation, and breaks our single Stripe Customer-on-platform model. ADR-0012 already commits to single-tenant Entra; we extend it with custom claims `app_tenant_id` and `app_role` populated from our DB at token issuance via a claims-mapping policy / token augmentation endpoint.

**Token shape after OPS.7:**
```
sub = entra oid
email = ...
app_user_id = guid (our identity.users.id)
app_role = super_admin | tenant_admin | guest
app_tenant_id = guid | null
```

A user with multiple tenant memberships gets `app_tenant_id` set to their primary on login; switching tenants triggers a re-issue (silent renew with a `tenant_hint` parameter). This avoids the React-side "active tenant" bug class.

**Super Admin** rides the same Entra flow with a privileged role claim, but with two non-negotiable additions: (a) MFA enforced at the Entra policy level for that role, (b) an IP allowlist for `/super-admin/*` enforced in API middleware. Separate management plane is overkill; role + MFA + IP gate is enough.

**`ICurrentUser` shape changes** (today at `src/VrBook.Contracts/Interfaces/ICurrentUser.cs`):
```csharp
Guid? UserId, string? B2CObjectId, string? Email
bool IsAuthenticated, IsSuperAdmin, IsTenantAdmin     // renamed
Guid? TenantId                                         // NEW
bool HasRole(string role)
```

**Authorization changes.** Every owner-scoped handler today checks `currentUser.UserId == aggregate.OwnerUserId` (e.g. `PlaceBookingHandler.cs:51`). The replacement rule is `currentUser.TenantId == aggregate.TenantId`. We add an `ITenantScoped` marker interface and a new `TenantAuthorizationBehavior` next to `AuditLogBehavior.cs` (proven extension point — the pipeline already wraps every MediatR request). The new behavior auto-rejects requests where the resolved aggregate's `tenant_id` ≠ caller's claim, eliminating per-handler boilerplate. The self-booking check at `PlaceBookingHandler.cs:51` stays (a tenant_admin still can't book their own property even though tenant_id matches).

---

## 3. Data Model & Migration

**Add `tenant_id uuid NOT NULL` to** (module → table):

| Module | Table |
|---|---|
| Catalog | `properties`, `amenities` (if tenant-customizable, see §6), `property_images` (denorm for RLS) |
| Booking | `bookings`, `booking_holds`, `sla_events` |
| Sync | `channel_feeds`, `sync_conflicts`, `inbound_events` |
| Payment | `payment_intents`, `refunds`, `webhook_events` |
| Pricing | `pricing_plans`, `pricing_rules`, `pricing_quote_cache` |
| Messaging | `threads`, `messages` |
| Reviews | `reviews`, `review_responses` |
| Notifications | `notification_log`, `notification_template_overrides` (new) |
| Identity | `audit_log_entries` (already has actor; add `tenant_id` for filtered Super Admin views) |

**Migration strategy: three-phase, never single-shot.**

1. **OPS.M.3a** — add column NULL-allowed, add FK to `identity.tenants(id)`, no app code changes. Ship.
2. **OPS.M.3b** — backfill: insert a `tenants` row "VrBook Default" (slug `default`), set `tenant_id = default.id` on every existing row. Wire the existing DevAuth Owner persona's user as the membership. Ship.
3. **OPS.M.3c** — alter to `NOT NULL`, deploy app code that requires the claim, drop the default-tenant fallback. Ship.

This survives staging→prod because steps 1 and 2 are reversible (drop the column) and step 3 is gated on app code already being in place.

**RLS (Postgres Row-Level Security)** is defense-in-depth, not the primary guard. Enable in **OPS.M.3d** (after app-level checks proven): `CREATE POLICY tenant_isolation ON <table> USING (tenant_id = current_setting('app.tenant_id')::uuid);`. The EF interceptor sets `app.tenant_id` per connection from `ICurrentUser.TenantId`. Background workers and Super Admin set `app.bypass_tenancy = on` instead, which the policy `OR`s. Catches "forgot to add `.Where(x => x.TenantId == _)`" bugs at the DB layer.

**Cross-tenant queries** (Super Admin reports, SLA worker, iCal inbound poller) bypass RLS via a service-account connection string with `app.bypass_tenancy = on` set at connect time. The app pulls a separate `IDbConnectionFactory<TenantBypass>` so the bypass is explicit at the call site, not ambient.

---

## 4. Stripe Connect Express

Express is committed per ADR-0003. Reaffirmed: Standard punts our brand to Stripe's dashboard; Custom dumps PCI scope and dispute handling onto us. Express is the right cost/control trade.

**Onboarding flow — at first-property-listed, not at sign-up.** Listing is the moment the tenant has skin in the game and KYC friction is justified; gating sign-up on Stripe loses tenants who want to look around first. Wizard step: create property draft → "Connect payouts" CTA → `Account.create` (type=express) → `AccountLink.create` → Stripe-hosted form → return URL → poll `account.updated` webhook until `charges_enabled && payouts_enabled`. Listings can be saved as drafts pre-KYC; activation requires `charges_enabled = true`.

**Hidden cost the user didn't ask about:** Stripe Express KYC typically takes 24-48h, occasionally 5+ days for identity edge cases. UX must say "Your listing is saved; payouts unlock when Stripe finishes verifying you." Tenants who don't read that flood support. Build an "Onboarding status" tile on the tenant dashboard from day one.

**PaymentIntent change** (today in `StripeGateway.cs:42`):
```csharp
opts.TransferData = new() { Destination = tenant.StripeAccountId };
opts.ApplicationFeeAmount = ToCents(amount * tenant.PlatformFeeBps / 10_000m);
opts.OnBehalfOf = tenant.StripeAccountId; // for VAT/tax determination
```
`platform_fee_bps` lives on `identity.tenants` (default 1500 = 15%, Super Admin can override per tenant). Capture method stays `manual` (Slice 0.3 invariant). `application_fee_amount` is on the auth, not the capture, so manual capture math is correct.

**Refunds**: tenant-initiated through our app; we pass `refund_application_fee=true` for full refunds and partial-proportional for partials. Edge case: a partial refund that exceeds `(charge_total - application_fee)` would push the connected account negative — we hard-block this in our `RefundCommand` validator and surface a clear error.

**Negative balance liability** is on the platform under Express. Acceptable Phase 1.5 risk because (a) we hold funds in manual-capture until owner confirms, (b) the partial-refund guard above, (c) Stripe Radar is on by default. Revisit at >50 tenants.

**Payouts** — tenant sees them via magic-link to the Stripe Express dashboard (`AccountLink.create` with `type=login_link`). We do not rebuild a payouts UI. `1099-K` reporting is handled by Stripe per connected account.

**Webhooks** — keep a single platform endpoint with the platform's webhook signing secret. Route events carrying an `account` field to the right tenant via `account → tenants.stripe_account_id`. Idempotency key becomes `(event_id, account_id)` — necessary because Stripe replays per-account-connected.

---

## 5. iCal Round-Trip per Tenant

**Outbound feeds inherit tenancy through Property.** `ChannelFeed.PropertyId` → `Property.TenantId`. Confirmed against `src/Modules/VrBook.Modules.Sync/Domain/ChannelFeed.cs`. The `OutboundToken` is global-unique by construction; no collision risk across tenants. **However**, we add `tenant_id` to `channel_feeds` denormalized so RLS works without joining Catalog at every request.

**Inbound URLs are tenant-private**. They contain AirBnB calendar secret tokens that leak forward bookings if exposed. Audit: today `ListChannelFeedsQuery` filters by property, which the new pipeline behavior will tenant-gate automatically. Add an explicit `tenant_id` filter to the *poller* (it currently iterates all enabled feeds platform-wide) so a bug in property scoping can't show feed A's URL in feed B's error log.

**Conflict resolution** — `sync_conflicts.property_id` inherits tenancy. Confirmed safe.

**Per-tenant outbound rate limiting** — yes, add. One tenant publishing an AirBnB link that goes viral shouldn't degrade others. Redis-backed token bucket keyed by `tenant_id`, 60 req/min default, raisable per tenant via Super Admin.

**Channel Manager API push** (proposal §22.2) is Phase 2 and doesn't change tenancy: same `tenant_id → stripe_account_id` pattern works for OAuth tokens per channel partner.

---

## 6. Property Management

**Amenity catalog: global with per-property selections.** Per-tenant amenities sounds attractive but explodes the search/filter UX (guest sees inconsistent facets per result). Tenant-specific amenities are 1% of the value; keep them global. House rules and descriptions already absorb the long tail.

**Pricing rules: tenant-configurable templates, property-level overrides.** The Slice 6 rule engine (Seasonal / Last-minute / LOS) gets a `tenant_id`-scoped "rule template" layer. Tenant Admin sets defaults at the tenant level; per-property overrides where needed. Cancellation policy is the same shape — tenant-default with property override.

**Feature toggles: scope='tenant' is needed.** Today `IFeatureToggle.IsEnabledAsync` resolves `user → property → global`. Add a `tenantId` parameter and resolve `user → property → tenant → global → default`. Tenant Admin can disable reviews/messaging across their whole portfolio.

**Image storage: keep flat, add tenant prefix later.** Current `property-images/{propertyId}/...` works; properties are tenant-unique. Switching to `property-images/{tenantId}/{propertyId}/...` requires a blob rename migration we don't need. Revisit if SAS-token scope-by-prefix becomes useful.

---

## 7. Bookings

**Booking belongs to Property.TenantId** at write-time and is denormalized onto the row. Cross-tenant booking view: Super Admin only, via a dedicated `ListAllBookingsForSuperAdminQuery` that goes through the bypass connection.

**Hold keys**: today `vrbook:hold:{propertyId}:{checkin}:{checkout}`. PropertyId is globally unique, so technically tenant scope isn't required. **Recommend adding it anyway**: `vrbook:hold:{tenantId}:{propertyId}:{checkin}:{checkout}`. Reasons: per-tenant Redis eviction policy if one tenant misbehaves; trivial to log/grep per tenant; future per-tenant Redis sharding option without a key migration.

**Stripe webhook idempotency**: `(event_id, account_id)` as in §4. We don't need `tenant_id` because `account_id → tenant_id` is the lookup we already do.

**SLA worker** (Slice 0.4) stays platform-wide for execution but every log line and audit entry carries `tenant_id`. Per-tenant SLA alerts in Phase 2.

**Reports** (Slice 7) — per-tenant occupancy/revenue is the default tenant_admin view; Super Admin gets cross-tenant aggregates via the bypass path.

---

## 8. Communication

**Threads inherit tenancy via Booking → Property → Tenant.** Cross-tenant messaging is never allowed; the guest-A ↔ owner-of-A's-booking invariant is enforced by the tenant authorization behavior.

**Sender domain: one platform domain in Phase 1.5; per-tenant in Phase 2.** Email goes from `bookings@vrbook.example.com` with `Reply-To: <tenant.support_email>` and display name `"<TenantDisplayName> via VrBook"`. Per-tenant subdomains (`bookings@tenant.vrbook.example.com`) require per-tenant DKIM/SPF — material effort and ongoing ops cost. Postpone.

**ACS Email: one platform resource with per-tenant `From` display name** in Phase 1.5. Per-tenant ACS resources cost real money and slow onboarding. Shared resource with strong tenant-id metadata on every send is the right Phase 1.5 trade. Per-tenant resources land in Phase 2 alongside subdomain DKIM.

**Recipient (notifications to tenant_admin)**: dedicated `tenants.support_email` field, separate from the auth principal's `users.email`. Auth user is one human; support inbox is often a shared mailbox. Bounce handling: ACS webhook → `notification_log` row marked bounced → Super Admin alert if a tenant's primary support email bounces 3x.

---

## 9. Super Admin Console

**Routes under `/super-admin/*`, separate from `/admin/*`**. `/admin/*` continues to mean "tenant admin" (current Owner UI). Separation reduces accidental privilege escalation and keeps middleware rules simple.

**Capabilities**: list tenants (status, MRR, properties, last activity), tenant detail view, suspend/reactivate (writes `suspended_reason` and a `TenantSuspended` domain event that disables new bookings + sends notice), force-refund a booking (dispute path), edit global feature toggles, view audit log filtered by tenant.

**Audit log** — `AuditLogBehavior.cs:18` already records every `IAuditable` request. Mark every Super Admin command `IAuditable`; existing pipeline does the rest. No new infrastructure.

**Impersonation** — implement as **a time-boxed claim swap, not a full session takeover**. Super Admin clicks "Act as Tenant X"; API issues a 30-minute token with `app_role=tenant_admin`, `app_tenant_id=X`, plus `impersonated_by=<super_admin_user_id>` claim that every audit row captures. Banner in UI: "Acting as TenantX — exit". Hard expiry, no refresh. The `impersonated_by` claim makes "who actually did this" answerable from audit logs alone.

**Billing**: VrBook bills tenants via **Stripe Billing on the platform account** (separate from Connect destinations). Each tenant becomes a Stripe Customer (platform) AND has a Stripe Account (Connect destination) — different objects. Recommend per-property monthly fee + per-booking fee (proposal §22.4) implemented as Stripe Billing usage-based metered subscriptions. Manual invoicing is fine for the first 5 tenants while pricing stabilizes; switch to Stripe Billing before scaling.

---

## 10. Migration & Cutover

| # | Sub-deliverable | Est. days | Depends on | Notes |
|---|---|---|---|---|
| OPS.M.1 | Tenant aggregate + schema (identity.tenants, memberships) | 2 | — | Empty table; no app code uses it yet. |
| OPS.M.2 | `TenantId` claim wiring + `ICurrentUser` shape update | 1.5 | M.1 | DevAuth populates `TenantId` from a cookie for testing. |
| OPS.M.3 | `tenant_id` column rollout (3a/3b/3c/3d) across all tables | 4 | M.1, M.2 | The work. Parallelisable across modules. |
| OPS.M.4 | `TenantAuthorizationBehavior` + remove per-handler owner checks | 1.5 | M.3 | Net code reduction. |
| OPS.M.5 | Stripe Connect Express integration (Account.create, AccountLink, webhook routing, PaymentIntent transfer_data) | 4 | M.3 | Critical path with M.3. |
| OPS.M.6 | iCal poller tenant-scoping + outbound rate limit | 1 | M.3 | Defensive change. |
| OPS.M.7 | Tenant Admin onboarding wizard (UI) + first-property → Stripe link | 3 | M.5 | Frontend slice. |
| OPS.M.8 | Super Admin console (list/detail/suspend/impersonate/audit filter) | 4 | M.4 | Frontend + small backend. |
| OPS.M.9 | RLS policies + bypass connection factory | 1.5 | M.3c | Defense-in-depth. |
| OPS.M.10 | Cross-tenant isolation test pack | 2 | M.4, M.5 | Two-tenant scenario — see below. |

**Critical path: M.1 → M.2 → M.3 → M.4 → M.5 → M.7 (≈16 days).** M.6, M.8, M.9, M.10 run in parallel against M.5/M.7. **Total realistic OPS multi-tenancy bring-up: 4-5 calendar weeks** with one full-time engineer; 2.5-3 weeks with two engineers split frontend/backend.

**Cutover** uses the OPS.M.3b backfill: "VrBook Default" tenant absorbs all Slice 0/1/2 data, the DevAuth Owner persona becomes its tenant_admin membership, no row is orphaned. Rollback for OPS.M.3a/b is a column drop; rollback for OPS.M.3c is the previous app image (column stays NOT NULL but app ignores it — safe).

**Test plan (the tenancy equivalent of Slice 0's race window):** scripted scenario *"Owner A signs up, Owner B signs up, A creates property P-A, B creates property P-B, B cannot list/read/edit P-A (403), guest G books P-A, A receives Stripe transfer, B's Stripe balance unchanged, B cannot read thread for booking-of-P-A (403), A's iCal outbound feed token does not appear in B's feed list, Super Admin sees both."* Wire as integration test in CI under `tests/Integration/MultiTenancyIsolationTests.cs`.

---

## 11. Explicitly Not in Phase 1.5

- Tenant subdomain routing (`a.vrbook.com`) — Phase 2 (cosmetic; adds cert + DNS ops).
- Per-tenant database schemas / physical isolation — Phase 3 if ever.
- Tenant-aware SignalR groups — pairs with Slice 7.
- Multi-region / data residency — Phase 3.
- Per-tenant Entra tenants — Phase 2+ at earliest; likely never.
- AirBnB Channel Manager API push — Phase 2 (§22.2).
- Self-serve tenant sign-up — Phase 2 (UI exists, gated invite-only initially).
- Per-tenant ACS Email resources / DKIM-per-subdomain — Phase 2.

---

## 12. Pushback Items (where the framing needs adjustment)

1. **"Account admin for each account"** — call it Tenant Admin and align with the existing Owner persona. Avoids a third role class on day one.
2. **"One super admin account"** — should be a *role* assignable to multiple humans, not a single shared login. Shared logins kill the audit trail. Three named super_admin users is the minimum.
3. **Onboarding at sign-up vs at first-property** — the user's framing implied Stripe at sign-up; defer to first-property and you keep the funnel.
4. **Sender domain per tenant** — sounds good, costs real DKIM/SPF/DMARC ops per tenant. Phase 1.5 uses one domain with per-tenant display name + Reply-To.

This plan is the contract. Once approved, OPS.M.1 starts; no code is written before that approval.
