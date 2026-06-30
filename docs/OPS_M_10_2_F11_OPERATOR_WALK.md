# Slice OPS.M.10.2 F11 — Operator Walk-Through (Staging)

**Status:** Live procedure. Run once per fresh staging environment to unblock the OPS.M.9.1 guest-checkout UI verification.
**Audience:** Niroshana (engineering lead). The walk takes about 10 minutes.
**Pre-req:** You are signed in to staging via Entra at `https://ca-vrbook-web-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/`. Your Entra OID is visible in browser DevTools after sign-in (see Step 1 below).

---

## §0 What this walk does

After F11.1–F11.5, the staging API has every contract change required to unblock the guest-checkout flow. What's missing is **data + role bootstrap** — a one-shot setup that promotes your Entra-signed user to `PlatformAdmin`, attaches a `tenant_memberships` row, completes Stripe Connect onboarding for the seed tenant, and seeds a `PricingPlan` per test property.

After this walk, an anonymous browser tab should be able to:

1. Browse `/properties` → 4 properties listed (already works).
2. Open `/properties/beach-villa` → quote widget shows nightly + total (now works).
3. Sign in as a different Entra user (guest persona) and complete `Book` → `/account/bookings` → `Cancel`.
4. Owner persona returns to `/admin/sync`, creates an iCal feed, copies the outbound URL, opens in a fresh tab → returns 200 with VEVENT body.

---

## §1 Promote yourself to PlatformAdmin (one-shot SQL)

The `is_platform_admin` flag is the gate for everything else in this walk.

1. Sign in to staging once so the M.2 `UserProvisioningMiddleware` creates your `identity.users` row.
2. From your local terminal, run:

   ```bash
   # Get the staging Postgres connection string from Key Vault (or use the
   # operator runbook docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md).
   psql "$STAGING_POSTGRES_CONN" -c "
     UPDATE identity.users
        SET is_platform_admin = true
      WHERE email = '<your-entra-email>';
   "
   ```

3. **Sign out and sign back in** so the M.2 middleware reissues your claims with `PlatformAdmin` role.
4. Confirm via DevTools `/api/v1/me` returns `"roles": [..., "PlatformAdmin"]`.

> **Reference:** `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` is the authoritative runbook for the SQL. The F8 prod-guard (commit `e251bf7`) prevents accidental flips in Production.

---

## §2 Seed your tenant_memberships row (F11.3)

You need a `tenant_memberships` row pointing at tenant `…0001` so `currentUser.TenantId` resolves and the per-tenant `/admin/*` routes stop 403'ing.

1. Open DevTools while signed in to staging. Look at the `Authorization: Bearer <token>` header on any API call.
2. Find your Entra OID — it's the `oid` claim in the JWT (paste the token into <https://jwt.ms> to see).
3. From your terminal:

   ```bash
   API=https://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io
   TID=00000000-0000-0000-0000-000000000001
   TOKEN=<paste-the-bearer-token-from-devtools>
   MY_OID=<paste-your-entra-oid>

   curl -sS -X POST \
     -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" \
     "$API/api/v1/admin/platform/tenants/$TID/memberships" \
     -d "{ \"entraOid\": \"$MY_OID\", \"role\": \"tenant_admin\", \"isPrimary\": true }"
   ```

   Expected: `201 { "membershipId": "<uuid>", "created": true }`.

4. **Sign out, sign back in.** Confirm via DevTools:

   ```bash
   curl -sS -H "Authorization: Bearer $TOKEN" "$API/api/v1/me/tenant" | jq '.id, .status'
   ```

   Expected: `"00000000-0000-0000-0000-000000000001"`, `"PendingOnboarding"`.

> **Source:** `src/Modules/VrBook.Modules.Identity/Application/Tenants/Commands/SeedTenantMembershipCommand.cs` (F11.3 `726c6f4`).

---

## §3 Onboard the tenant on Stripe Connect (real sandbox)

Tenant `…0001` needs a real (sandbox) Stripe account before any guest can check out. Three calls; total time about 3 minutes.

```bash
# 3a — create the Connect Express account on Stripe (idempotent).
curl -sS -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  "$API/api/v1/admin/tenants/$TID/stripe/onboard" \
  -d '{ "country": "US" }'
# expect: 200 { "stripeAccountId": "acct_..." }

# 3b — generate the Stripe-hosted onboarding URL (5-min expiring).
curl -sS -X POST \
  -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/admin/tenants/$TID/stripe/account-link"
# expect: 200 { "url": "https://connect.stripe.com/...", "expiresAt": "..." }
```

3c — **open the `url` in a browser**, fill the Stripe sandbox form. The test-mode form auto-approves with any placeholder data (use the Stripe test SSN `000-00-0000`, address, etc.). You'll be redirected back to `https://…/admin/onboarding/complete`.

```bash
# 3d — Stripe fires the account.updated webhook on completion. If you saw
#       the redirect AND the tenant didn't flip to Active within ~30s,
#       use the F11.4 manual reconcile.
curl -sS -X POST \
  -H "Authorization: Bearer $TOKEN" \
  "$API/api/v1/admin/tenants/$TID/stripe/refresh-readiness"
# expect: 200 { stripeAccountId, chargesEnabled: true, payoutsEnabled: true, status: "Active" }
```

3e — confirm the tenant is payment-ready:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" "$API/api/v1/me/tenant" \
  | jq '.id, .status, .stripeAccountId, .chargesEnabled, .payoutsEnabled'
# expect: …0001, "Active", "acct_...", true, true
```

> **Stripe sandbox flake fallback:** if the hosted form is broken in Stripe sandbox (rare but happens), `DevAuth__AllowStripeStub=true` + `DevAuth__AllowAnonymous=true` on staging unlocks the F11.2 dev-bridge:
>
> ```bash
> az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging \
>   --set-env-vars DevAuth__AllowAnonymous=true DevAuth__AllowStripeStub=true
>
> # WARNING: this temporarily disables Entra-only auth. After the stub
> # call below, IMMEDIATELY flip both back to false.
>
> curl -sS -X POST -H "Content-Type: application/json" \
>   "$API/api/v1/dev-auth/stub-stripe-readiness" \
>   -d "{ \"tenantId\": \"$TID\" }"
>
> az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging \
>   --set-env-vars DevAuth__AllowAnonymous=false DevAuth__AllowStripeStub=false
> ```
>
> This bypasses the real Stripe onboarding entirely. Document the fallback usage in the slice close-out — `e251bf7` (F8) requires Production guards to stay on.

---

## §4 Seed a PricingPlan per property

You can do this in the browser (`/admin/properties/<slug>/pricing`) or via curl. Curl is faster for all 4 seed properties.

```bash
# 4a — list the seed properties.
curl -sS "$API/api/v1/properties" | jq -r '.items[] | "\(.id)\t\(.slug)"'

# 4b — for each one (or just beach-villa for the walk):
PROP=$(curl -sS "$API/api/v1/properties" | jq -r '.items[] | select(.slug=="beach-villa") | .id')

curl -sS -X PUT \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  "$API/api/v1/properties/$PROP/pricing" \
  -d '{
    "baseNightlyRate": 200,
    "weekendRate": 250,
    "currency": "USD",
    "minStayNights": 1,
    "maxStayNights": 30,
    "dynamicEnabled": false,
    "fees": []
  }'
# expect: 200 with the plan DTO.
```

4c — confirm anonymous quote works:

```bash
curl -sS -X POST -H "Content-Type: application/json" \
  "$API/api/v1/properties/$PROP/quotes" \
  -d '{ "checkin":"2026-07-01", "checkout":"2026-07-04", "guests":2, "applyLoyaltyDiscount":false }'
# expect: 200 { totalAmount, lineItems, ... }
```

---

## §5 Browser walk — anonymous tab

Now the OPS.M.9.1 work is fully demonstrable. Open a **fresh anonymous browser tab** (Cmd/Ctrl+Shift+N — no Entra session):

1. `https://ca-vrbook-web-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/properties` — 4 seeded properties listed.
2. Click `beach-villa` → detail page renders, **quote widget now shows nightly + total** (was the `payment.connect_account_missing` error pre-F11).
3. Click **Sign in as Guest** (separate Entra account from your owner persona).
4. Pick dates → **Book** → expect a 201 with `status: "Tentative"`.
5. Go to `/account/bookings` → the booking row appears.
6. Open the booking → **Cancel** → status flips to `Cancelled`.

If steps 1–6 all work, **OPS.M.9.1 is verified end-to-end in staging**.

---

## §6 Outbound iCal walk

7. Sign back in as your **Owner persona** (the one you bootstrapped in §§1–2).
8. `https://…/admin/sync` — click **Add feed**, fill `channel=AirBnB`, `inboundUrl=https://example.com/cal.ics` (any URL — the poll will 404 but the create succeeds), `pollInterval=30`. Click create.
9. The row appears with a **Copy outbound URL** button. Copy it.
10. Open the copied URL in a fresh anonymous tab. Expected: 200, `Content-Type: text/calendar`, body starts `BEGIN:VCALENDAR`.
11. If you completed §5 (placed a booking that's `Tentative` or `Confirmed`), the ICS body should contain a `VEVENT` for it.

---

## §7 Report back

After the walk, tell me which steps passed and which failed. If a step fails:

- **Step 1–2 (PlatformAdmin / membership):** I'll patch the bootstrap path (likely `Slice5b_DevAuth_Default_Tenant_Membership` needs to cover Entra OIDs too).
- **Step 3 (Stripe onboarding):** I'll triage the Stripe sandbox state OR run the F11.2 stub fallback.
- **Step 4 (PricingPlan):** likely a validation error in the body — I'll fix the example.
- **Step 5 (Guest checkout):** triage via the API response codes you see; F6d resolver + F11.1 publish gate are the candidates.
- **Step 6 (iCal):** likely a feed-create UX gap I'll patch.

When all 11 steps pass, F11.8 closes the slice.

---

## §8 F11.7.4 web-side auth refactor (post-fix)

The first staging walk surfaced a class of symptoms ("Unauthorized" on `/account/bookings`, "Application error" page crashes, silent empty admin lists) that all traced back to TWO distinct bugs collapsed under one shape — diagnosed in [OPS_M_10_2_F11_WEB_AUDIT.md](OPS_M_10_2_F11_WEB_AUDIT.md):

1. **SSR-auth crash** — server components calling `[Authorize]` endpoints with no bearer token. Closed by F11.7.3 (server-shell + client component split).
2. **Client-side MSAL race** — client components firing `useEffect → apiFetch` (or `useQuery({queryFn: helper})`) on mount before MSAL was ready. The Providers.tsx token provider silently returned `null` on failure, so `apiFetch` proceeded without `Authorization` and the API 401'd permanently. Closed by F11.7.4.

The fix is a new wrapper hook `useAuthedQuery` (web/src/hooks/useAuthedQuery.ts) every authed call now goes through, plus a `<SignInGate>` empty state and an `auth-arch.test.ts` CI guardrail. The token provider in `Providers.tsx` now bounces to `acquireTokenRedirect` on `InteractionRequiredAuthError` instead of returning null silently.

### Walk addendum — what each step proves after F11.7.4

- **Step 5 (Guest checkout)**: signing in as a guest with no `tenant_memberships` row used to surface a 403 from `/me/tenant` that propagated as a fatal error. The wrapper's `treatAs404: [403, 404]` default now collapses both to "no tenant" empty state. Walk verification: opening `/account/bookings` should render either the booking list or the "no bookings yet" empty state, never "Unauthorized".
- **Step 6 (owner persona)**: `/admin`, `/admin/bookings`, `/admin/bookings/{id}`, `/admin/sync`, `/admin/calendar`, `/admin/pricing`, `/admin/reviews`, `/admin/notifications`, `/admin/amenities`, `/admin/messages`, `/admin/reports`, `/admin/properties` (list + edit), `/admin/platform/tenants` all migrated onto `useAuthedQuery`. On cold-cache load, each shows a skeleton until MSAL is ready, then renders the data (or a SignInGate if you're not signed in).

### Maintenance rule

When you add a new authed API helper in `web/src/lib/api/*.ts`:

1. Add the function name to the `authedHelpers` list in `web/src/lib/auth/auth-arch.test.ts`.
2. Call it from a client component **only via `useAuthedQuery`**, not bare `useQuery({queryFn: helper})`. The arch test fails the build if you forget.
3. Never import an authed helper into a server component (no `'use client'` directive). The second arch test fails the build if you do.

If a new page needs a server-side render with auth, use the split pattern from `web/src/app/bookings/[id]/page.tsx` + `web/src/components/booking/BookingDetailClient.tsx`: a thin server shell that mounts a `'use client'` child.
