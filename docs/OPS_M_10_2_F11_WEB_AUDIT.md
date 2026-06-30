# OPS.M.10.2 — F11 Web App Architecture Audit & Fix Plan

**Status:** PROPOSED (architect-reviewed). Not committed. Author: system-architect, 2026-06-30.
**Trigger:** F11.7 staging walk surfaced 3 web-side breakages in rapid succession
(F11.7.1 anonymous CTA, F11.7.2 Stripe-tolerance, F11.7.3 SSR-auth crash). The
user asked for a systemic audit before further reactive patches.
**Scope:** `web/` only. The .NET API is in a known-good state (25 audit findings
closed; F11.x flows working server-side; user's `VRB-3CVAKC` booking durable in
DB; host email fired).
**Non-goals:** OPS.M.12 (social IdPs + real Stripe sandbox), httpOnly cookie
migration, replacing MSAL / react-query.

---

## Part 1 — Root cause: TWO bugs collapsed under one symptom

Every "Application error" / "Unauthorized" / "Failed to load" we've hit on the
web app in F11.7 traces to one of two distinct anti-patterns. They look
identical from the browser, which is why this kept feeling like a moving target.

### Bug class 1 — **SSR-Auth Crash** (F11.7.3 already fixed two instances)

A Next.js server component (`page.tsx` / `layout.tsx` without `'use client'`)
calls an `[Authorize]` API endpoint during server render. SSR runs in the
Next.js container; the user's MSAL bearer token lives in the browser's
`sessionStorage` and never reaches the server fetch. Result: `apiFetch` sends
`Authorization: <empty>`, the API returns 401, the unhandled `ApiProblemError`
propagates up through React's SSR pipeline, and Next.js renders
**"Application error: a server-side exception has occurred"** with a digest.

**Diagnostic signature:** the entire page is the generic Next error page (no
chrome, no header). Reload doesn't help. Browser network tab shows nothing
because the failing fetch happened server-side.

### Bug class 2 — **Client-side MSAL Race** (the *real* cause of the user's `/account/bookings` "Unauthorized")

A client component fires `useEffect → apiFetch(...)` or
`useQuery({queryFn})` on mount **without** gating on
`useAuth().isAuthenticated && !isBusy`. On a fresh page load, MSAL's
`InteractionStatus` is non-None for ~50–500 ms while it rehydrates the account
from sessionStorage and (sometimes) talks to Entra to refresh a token. During
that window:

1. `useIsAuthenticated()` (from `@azure/msal-react`) flips to `true` as soon
   as an account exists in cache — **before** a token is acquirable.
2. The component's `useEffect` / `useQuery` fires.
3. `apiFetch` calls `tokenProvider()`.
4. `tokenProvider` in `web/src/components/Providers.tsx:66-78` does:
   ```ts
   try { ... acquireTokenSilent ... } catch { return null; }
   ```
   If silent acquisition is mid-flight or fails (cached account but no
   refreshable token yet), it returns `null`. **The error is swallowed.**
5. `apiFetch` proceeds with no `Authorization` header.
6. API returns 401.
7. The component renders its error branch — usually a generic
   "Unauthorized" or "Failed to load" panel.

**Diagnostic signature:** chrome renders normally (header has the right
sign-in state), but the inner component shows an error pill. A page reload
sometimes fixes it (because cache is now warm); sometimes doesn't.

### Bug class 3 — **Guest-persona blind spots** (latent)

The guest persona (`niroshhh@gmail.com`, no `tenant_memberships` row) is a
first-class supported state in the model: `ICurrentUser.TenantId` is null.
The API surfaces it as:

- `GET /api/v1/me` → **200 OK** (the user record exists)
- `GET /api/v1/me/tenant` → **403 Forbidden** (NOT 404 — the controller is
  `[Authorize(Roles="Owner,Admin")]` so the ASP.NET authorization filter
  short-circuits with 403; and the underlying `GetMyTenantHandler` would
  throw `ForbiddenException` → 403 if the role check were ever bypassed —
  see `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/GetMyTenantQuery.cs:30`
  and `src/VrBook.Api/Middleware/ProblemDetailsConfig.cs:38-44`).

The user's report of a 404 on `/me/tenant` from a `page-d82f8c5d0d4d957a.js`
hash is **almost certainly a stale tab** (the `/admin` browser tab from the
operator walk earlier in the session — `AdminSidebar` calls `useMyTenant()`
unconditionally). It's still an architectural problem because:

- `useMyTenant()` (`web/src/hooks/useMyTenant.ts:44`) has **no auth gate** and
  no guest-handling branch.
- `AdminSidebar.tsx:58-66` consumes it on every page under `/admin/*` and
  silently swallows the failure (`const { data: tenant }` — no error check;
  `showContinueSetup = tenant && ...` so the failed-fetch branch just hides
  the banner). This currently looks benign but means the admin shell is
  permanently broken for a user who happens to be a `PlatformAdmin` but has
  no tenant membership — the dashboard renders blank with no diagnostic.

---

## Part 2 — Web app audit (inventory of offenders)

### 2.1 — `app/**/page.tsx` server-component SSR-auth offenders

After F11.7.3 + the recursive sweep below: **zero remaining**.

| Path | `'use client'`? | Async? | Calls authed API at SSR? | Status |
| --- | --- | --- | --- | --- |
| `web/src/app/page.tsx` | no | no | no | safe (marketing) |
| `web/src/app/properties/page.tsx` | no | yes | NO — `searchProperties()` is `anonymous:true` | safe |
| `web/src/app/properties/[slug]/page.tsx` | no | yes | NO — `getPropertyBySlug()` is `anonymous:true` | safe |
| `web/src/app/bookings/[id]/page.tsx` | no | no | NO (thin shell → `BookingDetailClient`) | FIXED F11.7.3 |
| `web/src/app/account/bookings/page.tsx` | no | no | NO (thin shell → `MyBookingsClient`) | FIXED F11.7.3 |
| `web/src/app/account/bookings/[id]/review/page.tsx` | yes | no | n/a | safe |
| `web/src/app/account/loyalty/page.tsx` | yes | no | n/a | safe (but Bug-class-2 — see §2.3) |
| `web/src/app/account/messages/page.tsx` | yes | no | n/a | safe (but Bug-class-2 — see §2.3) |
| `web/src/app/account/profile/page.tsx` | yes | no | n/a | safe (stub) |
| `web/src/app/admin/**/page.tsx` (21 files) | yes (all) | no | n/a | safe (but Bug-class-2 — see §2.3) |
| `web/src/app/auth/callback/page.tsx`, `.../signout/page.tsx` | yes | no | n/a | safe |

### 2.2 — `app/**/layout.tsx`

| Path | `'use client'`? | Calls authed API at SSR? | Status |
| --- | --- | --- | --- |
| `web/src/app/layout.tsx` | no | no | safe |
| `web/src/app/account/layout.tsx` | no | no | safe (just renders `SiteHeader` + nav) |
| `web/src/app/admin/layout.tsx` | no | no | safe (renders `AdminSidebar`, which is a client child) |

### 2.3 — Bug-class-2 offenders (client component fires authed call without auth gate)

These are the ones currently breaking in staging. **Every one will intermittently
401 on cold cache.**

| File | Line | Symptom | Pattern |
| --- | --- | --- | --- |
| `web/src/components/booking/MyBookingsClient.tsx` | 45-50 | "Unauthorized" panel on `/account/bookings` | gates on `isAuthenticated && !isBusy` BUT `Providers.tsx:75` swallows `acquireTokenSilent` failure as `null`, so the gate clears and the request still goes out un-auth'd |
| `web/src/components/booking/BookingDetailClient.tsx` | 121 | "We couldn't load this booking" | same as above |
| `web/src/components/layout/AdminSidebar.tsx` | 58-59 | sidebar silently empty for a `PlatformAdmin`-but-no-tenant user; `Continue setup` link disappears | `useMyTenant()` + `useMe()` fire ungated |
| `web/src/app/account/loyalty/page.tsx` | 19-34 | "Failed to load loyalty status" | `useEffect` fires `getMyLoyalty()` immediately on mount |
| `web/src/app/account/messages/page.tsx` | 32-41, 43-56 | "Failed to load your account" / "Failed to load conversations" | `useEffect` fires `getCurrentUser()` + `listThreads()` immediately |
| `web/src/app/account/bookings/[id]/review/page.tsx` | 42-54 | "Could not load the booking" | `useEffect` fires `getBooking()` immediately |
| `web/src/app/admin/page.tsx` | 25-37 | empty/broken dashboard on cold load; the `useTentativeBookingPush` SignalR connect ALSO 401s | `useEffect → Promise.all([adminListBookings, adminListMyProperties])` |
| `web/src/app/admin/bookings/page.tsx` | 42-61 | "Failed to load" on `/admin/bookings` | `useEffect → adminListBookings(filter)` |
| `web/src/app/admin/onboarding/page.tsx` | 53 | "We couldn't load your tenant" | `useMyTenant()` ungated |
| `web/src/app/admin/onboarding/complete/page.tsx` | 18 | polls fail until cache warms | `useMyTenant({ pollIntervalMs:1000 })` ungated |
| `web/src/app/admin/onboarding/refresh/page.tsx` | 16 | same | same |
| `web/src/app/admin/platform/tenants/page.tsx` | 26-30 | "Failed to load tenants." | `useQuery({queryFn: listPlatformTenants})` ungated |
| `web/src/app/admin/platform/tenants/[tenantId]/page.tsx` | ~29 | "Failed to load" | same |
| `web/src/app/admin/properties/page.tsx`, `.../[id]/page.tsx`, `.../new/page.tsx` | (various) | same | same |
| `web/src/app/admin/calendar/page.tsx`, `.../pricing/page.tsx`, `.../reviews/page.tsx`, `.../sync/page.tsx`, `.../notifications/page.tsx`, `.../reports/page.tsx`, `.../messages/page.tsx`, `.../amenities/page.tsx`, `.../bookings/[id]/page.tsx` | (various) | same | same |

**Estimated count:** ~25 ungated client-side authed fetches across the codebase.

### 2.4 — Token-provider divergence (the silent-null bug)

Two separate `acquireTokenSilent` implementations exist with **different**
failure semantics:

- `web/src/components/Providers.tsx:66-78` — wired into `apiFetch` via
  `setTokenProvider`. On failure: `catch { return null }`. **Silent.**
- `web/src/lib/auth/useAuth.ts:55-64` — exposed as `getAccessToken()`. On
  failure: `acquireTokenRedirect(...)`. **Bounces to Entra.**

`apiFetch` only sees the silent variant. So when MSAL needs interactive
reconsent (rare but happens after a long idle), every authed call returns
401 forever until the user hits the sign-out button manually.

### 2.5 — Components consuming `/api/v1/me/tenant`

| File | Line | Auth-gated? | Guest-handled? |
| --- | --- | --- | --- |
| `web/src/hooks/useMyTenant.ts` | 44 | no | no |
| `web/src/components/layout/AdminSidebar.tsx` | 58 | no | partially (silent empty) |
| `web/src/app/admin/onboarding/page.tsx` | 53 | no | shows "We couldn't load your tenant" |
| `web/src/app/admin/onboarding/complete/page.tsx` | 18 | no | same |
| `web/src/app/admin/onboarding/refresh/page.tsx` | 16 | no | same |

### 2.6 — Existing wrapper hooks

There is **no** `useAuthedQuery` / `useApiQuery` / `RequireAuth` boundary.
Auth gating is hand-rolled per call site, and inconsistently. `useMe()`
(`web/src/hooks/useMe.ts:9-15`) is the simplest example — bare `useQuery`,
no gate, no guest branch.

---

## Part 3 — Systemic fix (Option A, the wrapper hook)

I weighed the four candidates from the consult brief:

- **A — `useAuthedQuery`**: shallow blast radius, idiomatic react-query,
  composable, doesn't change page routing.
- **B — `<RequireAuth>` boundary**: cleaner separation but forces every
  page to opt in twice (`'use client'` AND the boundary), and breaks the
  "header + auth CTA visible during loading" UX we already have on
  `MyBookingsClient`.
- **C — `'use client'` everywhere**: makes the SSR-auth bug
  unreproducible but loses streaming SSR + SEO for public surfaces (the
  property listing pages are anonymous and benefit from SSR today).
- **D — `useMe()` / `useMyTenant()` provider in `/account` layout**:
  good for de-dup but doesn't address the race; the provider's own
  `useQuery` has the same problem.

**Pick: A.** It's the smallest change that closes all 25 offenders, leaves
the public SSR surfaces (`/`, `/properties`, `/properties/[slug]`)
untouched, and has a natural arch-test surface (grep for raw
`useQuery({queryFn: <authedHelper>})` and assert it goes through the
wrapper).

### 3.1 — The wrapper

Add `web/src/hooks/useAuthedQuery.ts`:

```ts
'use client';

import { useQuery, type UseQueryOptions, type UseQueryResult } from '@tanstack/react-query';
import { useAuth } from '@/lib/auth/useAuth';
import { ApiProblemError } from '@/lib/api/client';

/**
 * Auth-aware react-query wrapper. Standard contract for every component
 * that calls an `[Authorize]` API endpoint.
 *
 *   - Defers the query until MSAL is ready AND a token is acquirable.
 *   - 401 from the API short-circuits to `isError + status === 401`
 *     WITHOUT triggering react-query retry storms.
 *   - 403/404 are returned to the caller as `data === null` so the
 *     "no tenant / no booking" empty state is a render concern, not an
 *     exception concern. (Pass `treatAs404` to override.)
 *
 * Use this instead of bare `useQuery` for ANY authed endpoint. Pages
 * that intentionally want anonymous-or-authed dual mode (e.g. the
 * public property page that swaps to a personalized rate when signed in)
 * should still use this — the `enabled: false` branch will keep their
 * call from firing while anonymous.
 */
export const useAuthedQuery = <T,>(
  options: Omit<UseQueryOptions<T | null>, 'enabled' | 'retry'> & {
    readonly treatAs404?: readonly number[]; // default: [403, 404]
  },
): UseQueryResult<T | null> & {
  readonly tokenReady: boolean;
  readonly needsSignIn: boolean;
} => {
  const { isAuthenticated, isBusy } = useAuth();
  const tokenReady = isAuthenticated && !isBusy;
  const treatAs404 = options.treatAs404 ?? [403, 404];

  const wrappedQueryFn = async (ctx: Parameters<NonNullable<UseQueryOptions<T | null>['queryFn']>>[0]) => {
    try {
      return await options.queryFn!(ctx);
    } catch (err) {
      if (err instanceof ApiProblemError && treatAs404.includes(err.status)) {
        return null;
      }
      throw err;
    }
  };

  const result = useQuery<T | null>({
    ...options,
    queryFn: wrappedQueryFn,
    enabled: tokenReady && (options as { enabled?: boolean }).enabled !== false,
    retry: false,
  });

  return {
    ...result,
    tokenReady,
    needsSignIn: !isAuthenticated && !isBusy,
  };
};
```

### 3.2 — Token provider needs to stop swallowing failures

Replace `Providers.tsx:66-78` so silent-fail bounces to interactive:

```ts
setTokenProvider(async () => {
  const account = msalInstance.getActiveAccount();
  if (!account) return null;
  try {
    const result = await msalInstance.acquireTokenSilent({ scopes: apiScopes, account });
    return result.accessToken;
  } catch (err) {
    // Don't swallow: if silent acquisition genuinely needs interaction,
    // an ungated apiFetch would otherwise 401 forever. The redirect
    // re-enters MSAL's flow and lands back on the current route.
    if (err instanceof InteractionRequiredAuthError) {
      await msalInstance.acquireTokenRedirect({ scopes: apiScopes, account });
    }
    return null;
  }
});
```

### 3.3 — The `<SignInGate>` empty state

A small client component every offender drops in for the
`needsSignIn` branch:

```tsx
// web/src/components/auth/SignInGate.tsx
'use client';
import { LogIn } from 'lucide-react';
import { useAuth } from '@/lib/auth/useAuth';

export const SignInGate = ({ title, description }: { title: string; description?: string }) => {
  const { signIn } = useAuth();
  return (
    <div className="mx-auto max-w-md py-12 text-center">
      <h1 className="text-xl font-semibold">{title}</h1>
      {description && <p className="mt-2 text-sm text-muted-foreground">{description}</p>}
      <button
        type="button"
        onClick={signIn}
        className="mt-4 inline-flex items-center gap-1.5 rounded-md bg-brand-orange-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-orange-700"
      >
        <LogIn className="h-4 w-4" aria-hidden /> Sign in
      </button>
    </div>
  );
};
```

### 3.4 — The arch test

Add `web/src/lib/auth/auth-arch.test.ts`:

```ts
/**
 * Architecture guardrail for OPS.M.10.2 F11.7.4.
 * Every client component that calls an authenticated API helper MUST
 * go through `useAuthedQuery`. Bare `useQuery({ queryFn: <helper> })`
 * is forbidden for helpers in the authedHelpers list.
 *
 * Catches BOTH the SSR-auth bug AND the MSAL race in CI before merge.
 */
import { readFileSync } from 'node:fs';
import fg from 'fast-glob';
import { describe, expect, it } from 'vitest';

// Maintained list. New authed helpers added to web/src/lib/api/*.ts
// MUST be added here OR explicitly marked `anonymous: true`.
const authedHelpers = [
  'getCurrentUser','getMyTenant','getMyLoyalty','myBookings','getBooking',
  'adminListBookings','adminGetBooking','adminListMyProperties','adminGetPropertyById',
  'createProperty','updateProperty','listThreads','getThread','listMessages','sendMessage',
  'markThreadRead','listPlatformTenants','getPlatformTenant','suspendTenant','reactivateTenant',
  'setPlatformFee','submitReview','respondToReview','adminListReviews','adminHideReview',
  'adminRestoreReview','adminRejectReview','listChannelFeeds','createChannelFeed',
  'updateChannelFeed','deleteChannelFeed','listSyncConflicts','resolveSyncConflict',
  'adminListAmenities','adminCreateAmenity','adminUpdateAmenity','adminDisableAmenity',
  'adminEnableAmenity','adminDeleteAmenity','adminListNotifications','adminRetryNotification',
  'getOccupancyReport','getRevenueReport','getAdrReport','getSourceReport',
  'createHold','releaseHold','placeBooking','cancelBooking','confirmBooking','rejectBooking',
  'checkInBooking','checkOutBooking','getPaymentIntentForBooking','getPropertyCalendar',
  'listAvailabilityBlocks','createAvailabilityBlock','deleteAvailabilityBlock',
];

describe('auth arch', () => {
  it('no client component uses raw useQuery() with an authed helper', async () => {
    const files = await fg(['src/**/*.{ts,tsx}'], {
      cwd: 'web', ignore: ['**/node_modules/**','**/*.test.*','**/useAuthedQuery.ts'],
    });
    const offenders: string[] = [];
    for (const f of files) {
      const text = readFileSync(`web/${f}`, 'utf8');
      if (!text.includes("from '@tanstack/react-query'")) continue;
      if (!/\buseQuery\s*\(/.test(text)) continue;
      for (const helper of authedHelpers) {
        // queryFn: helper OR queryFn: () => helper(...) OR queryFn: () => helper
        const re = new RegExp(`queryFn[^,}]*\\b${helper}\\b`);
        if (re.test(text)) offenders.push(`${f} uses ${helper}() with bare useQuery — switch to useAuthedQuery`);
      }
    }
    expect(offenders).toEqual([]);
  });

  it('no server-component page imports an authed helper', async () => {
    const files = await fg(['src/app/**/{page,layout}.tsx'], { cwd: 'web' });
    const offenders: string[] = [];
    for (const f of files) {
      const text = readFileSync(`web/${f}`, 'utf8');
      const isClient = /^\s*['"]use client['"]/.test(text);
      if (isClient) continue;
      for (const helper of authedHelpers) {
        if (new RegExp(`\\b${helper}\\b`).test(text)) {
          offenders.push(`${f} (server component) imports ${helper}`);
        }
      }
    }
    expect(offenders).toEqual([]);
  });
});
```

This gives us teeth: a regressor PR that converts a client component back
to bare `useQuery({queryFn: myBookings})` fails CI with a precise message.

---

## Part 4 — Special cases the wrapper must accept

### 4.1 — Guest persona is a first-class state

The wrapper's default `treatAs404: [403, 404]` collapses both
"no tenant membership" (`/me/tenant` → 403) and "row not found" (`/bookings/{id}`
→ 404) to `data === null`. Render `data === null` as the appropriate empty
state. `AdminSidebar` calls `useAuthedQuery(getMyTenant)` and renders:

- `needsSignIn` → nothing (don't push sign-in from a sidebar)
- `tokenReady && !data` → render "You don't manage a tenant yet"
- `data` → normal nav

### 4.2 — `useEffect`-based fetchers

`/account/loyalty`, `/account/messages`, `/account/bookings/[id]/review` use
`useEffect + useState` instead of react-query. Migrate them to react-query
via the wrapper. Avoid `useEffect → fetch` going forward — it's the bug
class that gives us no consistent place to add the auth gate.

### 4.3 — Polling consumers

`useMyTenant` has bespoke poll/cap logic. Keep the hook, but rewrite its
internals to call `useAuthedQuery` and surface the polling controls as a
thin layer on top:

```ts
export const useMyTenant = (opts: UseMyTenantOptions = {}): UseMyTenantResult => {
  const q = useAuthedQuery({
    queryKey: ['me', 'tenant'],
    queryFn: getMyTenant,
    refetchInterval: /* same logic as today */,
  });
  // ... existing poll counter / exhausted logic ...
};
```

### 4.4 — Mutations (`useMutation`) are intentionally out of scope

Mutations don't fire on mount; they fire on user gesture, by which time
MSAL is reliably ready. They use the same `apiFetch` so the
`tokenProvider` fix in §3.2 still benefits them. No wrapper needed.

### 4.5 — Anonymous endpoints stay un-wrapped

`searchProperties`, `getPropertyBySlug`, `listAmenities`,
`getAvailability`, `listReviewsForProperty` all pass `anonymous: true`. They
keep using bare `useQuery`. The arch test's `authedHelpers` list is
maintained by hand; new helpers added to `lib/api/*.ts` go through PR
review where the omission is visible.

---

## Part 5 — F11.7.4 commit sequence

Eight commits. Each is independently buildable + CI-greenable. Push
after each; `cd-staging-web` will fire and you can verify the staging
container picks up the change before stacking the next commit.

### F11.7.4.1 — Land the wrapper + token-provider fix + arch test (no consumer changes)

**Files:**
- new `web/src/hooks/useAuthedQuery.ts` (§3.1)
- new `web/src/components/auth/SignInGate.tsx` (§3.3)
- new `web/src/lib/auth/auth-arch.test.ts` (§3.4)
- edit `web/src/components/Providers.tsx` (§3.2 — interactive bounce on
  `InteractionRequiredAuthError`)

**Validate:** `cd web && npm run build && npm test`. Arch test should pass
because the existing offenders use `useEffect`, not `useQuery({queryFn: ...})`
— they'll fail later when we migrate them in 7.4.5 (correct order;
the arch test catches each migration's correctness one at a time).

**CI:** `cd-staging-web.yml`.

### F11.7.4.2 — Migrate `MyBookingsClient` + `BookingDetailClient` (highest-traffic + currently broken)

**Files:**
- edit `web/src/components/booking/MyBookingsClient.tsx` — replace
  `useQuery` with `useAuthedQuery(myBookings)`; replace the
  unauth'd branch with `<SignInGate title="Sign in to see your bookings" />`.
- edit `web/src/components/booking/BookingDetailClient.tsx` — same;
  `useAuthedQuery({ queryKey: ['booking', id], queryFn: () => getBooking(id) })`;
  `data === null` is the "not found / forbidden" empty state we already render.

**Validate:** `npm run build`. User can reload `/account/bookings` on
staging and the "Unauthorized" panel disappears even on cold MSAL state.

### F11.7.4.3 — Migrate `AdminSidebar` (silent-empty regression risk)

**File:** `web/src/components/layout/AdminSidebar.tsx`. Replace `useMyTenant()`
+ `useMe()` with `useAuthedQuery` wrappers. **Critical:** the `showPlatform`
branch must render even when `tenant === null` (PlatformAdmin without a tenant
membership is a real persona).

### F11.7.4.4 — Migrate the 4 `/admin/onboarding` + `/admin/platform/tenants` pages

**Files:**
- `web/src/hooks/useMyTenant.ts` — rebuild on top of `useAuthedQuery` (§4.3).
- `web/src/app/admin/platform/tenants/page.tsx` + `[tenantId]/page.tsx` —
  swap to `useAuthedQuery`.
- The 3 onboarding pages automatically benefit from the `useMyTenant` rewrite.

### F11.7.4.5 — Migrate the 3 `useEffect`-based account pages

**Files:**
- `web/src/app/account/loyalty/page.tsx`
- `web/src/app/account/messages/page.tsx` — keep the `Suspense` boundary
  for `useSearchParams`; rewrite the body's two `useEffect` blocks.
- `web/src/app/account/bookings/[id]/review/page.tsx`

Each: drop `useState + useEffect + apiFetch`, use `useAuthedQuery` +
`useMutation` (for `submitReview`).

### F11.7.4.6 — Migrate the admin dashboard + bookings list (high frequency)

**Files:**
- `web/src/app/admin/page.tsx` — the `Promise.all([adminListBookings, adminListMyProperties])`
  becomes two `useAuthedQuery` calls (or one `queries` array via `useQueries`,
  reviewer's call).
- `web/src/app/admin/bookings/page.tsx`

### F11.7.4.7 — Migrate the remaining ~12 admin pages

**Files:** `web/src/app/admin/{calendar,pricing,reviews,sync,notifications,reports,messages,amenities,properties}/**.tsx` plus `bookings/[id]/page.tsx`.

This commit is large but mechanical. Run the arch test locally first
(`npm test --run auth-arch`); the test enumerates remaining offenders
so the diff is driven by its failure list.

### F11.7.4.8 — Re-consult §11 + close-out

Update `docs/OPS_M_10_2_F11_OPERATOR_WALK.md` with the new client-auth
checklist. Note in `CLAUDE.md` (or the equivalent house-rules doc) that
new authed endpoints get listed in `authedHelpers`. Doc-only commit, no
CI (per the established rule).

---

## Part 6 — Verification protocol after F11.7.4.7

### API-side (I run these)

```bash
# Smoke the affected endpoints under both personas.
# Owner persona — should 200:
curl -sS -b 'vrbook-dev-persona=Owner' \
  https://ca-vrbook-api-staging.../api/v1/me/tenant | jq .
curl -sS -b 'vrbook-dev-persona=Owner' \
  https://ca-vrbook-api-staging.../api/v1/admin/bookings | jq '.items | length'

# Guest persona — should 403 (tenant) + 200 (bookings list, empty) + 200 (me):
curl -sS -b 'vrbook-dev-persona=Guest' \
  -o /dev/null -w 'tenant=%{http_code}\n' \
  https://ca-vrbook-api-staging.../api/v1/me/tenant
curl -sS -b 'vrbook-dev-persona=Guest' \
  https://ca-vrbook-api-staging.../api/v1/bookings | jq '.items | length'
curl -sS -b 'vrbook-dev-persona=Guest' \
  https://ca-vrbook-api-staging.../api/v1/me | jq '.email'
```

If any of these don't match the expected status, **stop** — the API has
regressed and the web fix can't paper over it.

### UI-side (user runs these in the browser)

For each row in the table, the expected state is what should render with
**MSAL cold** (close all browser tabs, reopen the URL fresh, immediately
read the screen — don't wait for a second render). Then reload once and
the state should be **identical**.

| URL (Entra-signed-in guest `niroshhh@gmail.com`) | Expected first paint | Expected after reload |
| --- | --- | --- |
| `/` | marketing landing, no errors in console | same |
| `/properties` | property cards | same |
| `/properties/<slug>` | detail + "Sign in to book" CTA | same |
| `/account/bookings` | "You don't have any bookings yet" (since guest doesn't own `VRB-3CVAKC`) OR the list if they do | same |
| `/account/bookings/<id>` for own booking | full booking detail | same |
| `/account/bookings/<id>` for someone else's | "Booking not found" empty state, NOT an error | same |
| `/account/loyalty` | "Your loyalty account opens automatically…" empty | same |
| `/account/messages` | empty inbox, NOT "Failed to load" | same |
| `/account/profile` | placeholder | same |
| `/admin` (as `Owner` persona — switch via DevAuth) | dashboard with cards | same |
| `/admin/bookings` | list | same |
| `/admin/onboarding` | wizard | same |

If any first-paint cell renders an error pill or generic "Application
error", that's a regression — the wrapper or arch test missed a path.

---

## Part 7 — What we are explicitly NOT doing

(Per the consult brief's "What NOT to do" section, restated here for the
record so a future maintainer doesn't try to relitigate.)

- **Not** ripping out MSAL. The token flow is correct in principle; the
  bug is the silent-null fallback + the missing gate on consumers.
- **Not** replacing react-query.
- **Not** moving to httpOnly cookies. That'd require API-side changes
  (cookie auth scheme, antiforgery rework) which are out of scope for
  F11.
- **Not** adding per-page `error.tsx` boundaries everywhere. The
  wrapper makes 401/403/404 a first-class data state instead of an
  exception — error boundaries become a true last-resort safety net,
  not the primary error UX.

---

## Part 8 — Open questions for the user (none blocking; flagging for visibility)

1. The arch test's `authedHelpers` list is hand-maintained. Acceptable
   maintenance burden? Alternative: parse `lib/api/*.ts` AST at test
   time and infer which helpers omit `anonymous: true`. AST parsing is
   more robust but adds a dev dependency (`@typescript-eslint/parser`)
   and a non-trivial test. The hand list is fine for ~50 endpoints; if
   we cross 100, revisit.
2. `useMutation` is intentionally not wrapped (§4.4). If a future
   regressor shows a mutation firing before MSAL is ready (e.g. a
   programmatic "complete booking" that doesn't wait for a click), we
   add a `useAuthedMutation` then. Don't pre-build it.
3. The user's stale `/me/tenant` 404 in the console (§Part 1, bug
   class 3) — confirm with the user it's from a different tab; if it's
   actually from `/account/bookings` we have a *third* bug to chase.
   My read is that it's stale-tab; the listing component on
   `/account/bookings` doesn't import `useMyTenant`.

---

## Appendix A — Why this won't recur

The combination of (wrapper + token-provider fix + arch test) closes
every known path:

- Wrapper makes the gate the default. Forgetting it is now a typo, not
  an architectural decision.
- Token provider no longer swallows interactive-required failures.
- Arch test fails in CI on the FIRST PR that adds a bare
  `useQuery({queryFn: authedHelper})`.
- Server-component arch test (`§3.4`, second `it` block) fails on the
  FIRST PR that adds `import { myBookings } from '@/lib/api/booking'`
  to a `page.tsx` without `'use client'`.

The remaining failure mode is "developer adds a new authed helper to
`lib/api/foo.ts` and forgets to add it to `authedHelpers`." That's a
review-time check (the diff to `lib/api/*.ts` is small and visible),
not a runtime trap.
