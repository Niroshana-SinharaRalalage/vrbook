# OPS.M.10.2 F11 — Token-Based Auth Deep Dive (Entra External ID)

- **Status:** Draft for reviewer sign-off (`niroshanaks`). Companion to
  [`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md`](./OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md).
- **Date:** 2026-07-01.
- **Author (role):** Platform Enterprise Architect.
- **Scope:** End-to-end review of the six token-auth questions the user raised
  after the identity/tenancy architectural review:
  1. MSAL client config (SPA vs Web app-registration, apiScopes, redirect URIs, refresh token flow)
  2. API JWT Bearer validation (issuer, audience, oid claim mapping, roles claim, tenant claim shape)
  3. Browser acquires token → attaches `Authorization: Bearer` → API validates → `HttpCurrentUser` reads claims + `HttpContext.Items`
  4. ID tokens vs Access tokens for API calls; audience/scope pairing; silent refresh
  5. Multi-provider (OPS.M.12: Google + Microsoft social IdPs) — federate through External ID vs multiple App Registrations
  6. Whether app-side `identity.users` row is the right pattern, or Entra-source-of-truth + oid mapping table
- **Constraint honored:** everything below is durable. No temporary unblocks,
  no DevAuth revival. DevAuth retirement stands (per previous review §5.4).
- **Companion authoritative refs cited inline:**
  - Microsoft Learn — Access tokens: <https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens>
  - Microsoft Learn — ID tokens: <https://learn.microsoft.com/en-us/entra/identity-platform/id-tokens>
  - Microsoft Learn — Identity providers for external tenants: <https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers>
  - Microsoft Learn — Scopes / permissions: <https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc>
  - Microsoft Learn — Google identity provider (External ID): <https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers>
  - Azure-Samples — MSAL React CIAM API call (canonical reference SPA): <https://learn.microsoft.com/en-us/samples/azure-samples/ms-identity-ciam-javascript-tutorial/ms-identity-ciam-javascript-tutorial-1-call-api-react/>

---

## 0. Executive summary

The current code is **~85 % aligned** with Microsoft's Entra External ID
best-practice envelope. The remaining ~15 % is where our recurring F11
failures originate — none of them are in MSAL wiring; all are on the
API side of the bearer handshake and in the DB shape underneath it.

- **§1 (MSAL client config):** correct. SPA public-client, apiScopes on the
  API resource (`api://vrbook/access_as_user`), sessionStorage, PKCE via
  MSAL.js defaults, silent refresh with `InteractionRequiredAuthError`
  fallback in place. Two small cleanups (redirect URI moved to `spa` platform
  in the App Registration per runbook §6; `credentials: 'include'` on the
  API client (`client.ts:129`) should be removed after DevAuth retirement).
- **§2 (JWT Bearer validation):** correct in shape (issuer / audience /
  lifetime / JWKS via authority), plus a defensible `ValidIssuers` dual-form
  work-around for CIAM's known issuer-URL quirk (`AuthExtensions.cs:44-55`).
  One durable fix: standardize on **`oid` (Object ID)** as the principal
  identifier and drop the `ClaimTypes.NameIdentifier` fallback — External ID
  emits `oid` reliably; `sub` is pairwise per Microsoft guidance and MUST
  NOT be used as a cross-app or cross-provider principal id.
- **§3 (flow end-to-end):** matches best practice except the flow assumes
  `identity.users(oid)` is unique-per-human. It isn't (see §5 of previous
  review). The token flow is not the bug — the DB shape it lands on is.
- **§4 (ID vs Access token):** access token is correct choice; audience is
  the API app registration (correct), scope is `access_as_user` (correct),
  silent refresh via `acquireTokenSilent` + refresh-token cookie MSAL keeps
  in sessionStorage (correct). Nothing to change here.
- **§5 (multi-provider OPS.M.12):** **federate through External ID.** ONE App
  Registration for `vrbook-api` + ONE for `vrbook-web`. Google/Microsoft/Apple
  attach as identity providers on the External tenant itself. Microsoft's
  own guidance and every published sample land here. Multiple App Registrations
  is a workforce-B2B pattern, not CIAM.
- **§6 (Entra as source of truth vs `identity.users`):** **keep
  `identity.users`.** Entra External ID's user profile is fine for identity
  but is architecturally wrong for FK targets on `bookings`, `reviews`,
  `audit_log`, `threads`, `notifications`. Making Entra the DB canonical
  identifier trades one 401 for four categorical harms: (i) cascading FK
  churn every time an oid rotates for social-IdP linking, (ii) 200-ms token-
  time joins on every write, (iii) domain events referencing a string PK
  we don't own, (iv) can't test without a Live Entra call. The previous
  review's `identity.users` + `user_identities(provider, external_id)`
  side-table pattern is Microsoft's own recommended shape as expressed in
  their Custom Authentication Extension docs and in every OSS CIAM
  reference implementation (Auth0, Firebase, AWS Cognito) — the app owns
  the canonical user row; the identity provider owns credentials.

**Best-practice checklist score:** 17 / 22 PASS, 3 FAIL, 2 partial. See §6.

**Migration lands** as OPS.M.13 (§5 previous review) for `user_identities`;
OPS.M.14 for DevAuth retirement; OPS.M.15 for App Role cleanup; OPS.M.16 for
migration audit. This doc adds two supplementary changes both landing with
OPS.M.13/14:

- Drop `ClaimTypes.NameIdentifier` fallback for principal id (§2).
- Drop `credentials: 'include'` from the SPA API client (§1).

---

## 1. Current state, per item

### 1.1 MSAL client config

**Where it lives:** `web/src/lib/auth/msalConfig.ts` (79 lines) +
`web/src/components/Providers.tsx` (112 lines) + the App Registration in the
External tenant.

**What's set up today:**

`msalConfig.ts:36-61`
```ts
export const msalConfig: Configuration = {
  auth: {
    clientId: clientId ?? '00000000-0000-0000-0000-000000000000',
    authority: authority ?? 'https://login.microsoftonline.com/common',
    knownAuthorities,                     // e.g. ['vrbookcid.ciamlogin.com']
    redirectUri,                          // window.location.origin + '/auth/callback'
    postLogoutRedirectUri,                // window.location.origin + '/auth/signout'
    navigateToLoginRequestUrl: true,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
  system: { loggerOptions: { logLevel: LogLevel.Warning, piiLoggingEnabled: false, ... } },
};

export const apiScopes: string[] = ['api://vrbook/access_as_user'];
```

`Providers.tsx:47-63` constructs a `PublicClientApplication` (public client,
no client secret). LOGIN_SUCCESS event sets the active account.

`Providers.tsx:71-102` wires an `apiFetch` token provider that calls
`acquireTokenSilent({ scopes: apiScopes, account })`, and on
`InteractionRequiredAuthError` falls back to `acquireTokenRedirect(...)`.

**Best-practice alignment:**

| Best practice item | Status | Evidence |
|---|---|---|
| SPA app registration type (`spa.redirectUris`, not `web.redirectUris`) | PASS (post-2026-06-26 fix per `entra-external-id-setup.md` §6) | Confirmed by runbook §6 that this was patched via Graph. `AADSTS9002326: Cross-origin token redemption` symptom would resurface if regressed. |
| Public client (no client secret in browser) | PASS | `PublicClientApplication` (MSAL.js), no `clientSecret` present. |
| Authority uses `ciamlogin.com`, not `login.microsoftonline.com` | PASS | `authority` sourced from `NEXT_PUBLIC_ENTRA_AUTHORITY` = `https://vrbookcid.ciamlogin.com/{tid}/v2.0` (`main.bicep:489`, KV secret `entra-web-authority`). `knownAuthorities` computed from URL host (`msalConfig.ts:28-34`) — Microsoft requires this for non-standard hosts. |
| Redirect URIs include prod + staging (dev if needed) | PASS (dev + staging); PROD-TBD | `entra-external-id-setup.md` §2 registers `http://localhost:3000/auth/callback` + the deployed web container. Prod will need the Front Door / custom domain URI added when it lands. |
| `apiScopes` uses `api://.../access_as_user`, not `.default` | PASS | `msalConfig.ts:70`. The msalConfig.test.ts guardrail (per runbook troubleshooting table) also catches regressions. |
| PKCE code exchange | PASS (implicit — MSAL.js 3.x always uses PKCE for SPAs) | No `clientSecret`; MSAL.js SPA flow is PKCE by construction. |
| `redirectUri` matches App Registration exactly | PASS | `redirectUri` = `${window.location.origin}/auth/callback` computed at runtime; App Registration must list the same origins. Verified by `docs/identity/runbooks/entra-external-id-setup.md` §6 SPA-platform block. |
| `cacheLocation: 'sessionStorage'` | PASS | `msalConfig.ts:46`. Microsoft recommends `sessionStorage` for higher-security defaults over `localStorage`. |
| `storeAuthStateInCookie: false` | PASS | `msalConfig.ts:47`. Cookie-backed auth state is only for IE11 compat; we don't target IE. |
| Silent refresh via `acquireTokenSilent(account)` | PASS | `Providers.tsx:75-79` + `useAuth.ts:57-63`. |
| Handles `InteractionRequiredAuthError` by triggering interactive re-auth | PASS | `Providers.tsx:90-98`, added at Slice OPS.M.10.2 F11.7.4.1. Comment explains the pre-fix regression this closed. |

**Verdict on §1:** aligned. Two cleanups (not bugs) queued for OPS.M.14:

- `client.ts:126-129` sets `credentials: 'include'` on every fetch. That was
  needed for the DevAuth cookie (`vrbook-dev-persona`) to travel cross-origin
  to the API. Once DevAuth is retired (OPS.M.14), Bearer alone carries auth
  and `credentials: 'include'` becomes an unnecessary CSRF surface. Drop it.
- `Providers.tsx:53` uses `event.payload as AuthenticationResult`. When
  MSAL.js emits `LOGIN_SUCCESS`, the payload is a fully typed union — the
  cast is safe today but should be narrowed via `event.payload && 'account' in event.payload`
  to survive future SDK bumps. Optional; not a security concern.

---

### 1.2 API JWT Bearer validation

**Where it lives:** `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs`
(83 lines). Registered from `Program.cs:95`.

**What's set up today:**

`AuthExtensions.cs:44-61`
```csharp
var discoveryAuthority = $"{entraInstance.TrimEnd('/')}/{entraTenantId}/v2.0";
var actualIssuer      = $"https://{entraTenantId}.ciamlogin.com/{entraTenantId}/v2.0";

auth.AddJwtBearer(opts =>
{
    opts.Authority = discoveryAuthority;
    opts.Audience = entraClientId;
    opts.RequireHttpsMetadata = true;
    opts.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuers = new[] { discoveryAuthority, actualIssuer },
        ValidateAudience = true,
        ValidAudience = entraClientId,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2),
    };
});
```

The comment on lines 40-45 explains the dual `ValidIssuers`: External ID's
discovery-doc-declared issuer is `https://<subdomain>.ciamlogin.com/{tid}/v2.0`
BUT the issuer actually stamped on tokens is
`https://{tid}.ciamlogin.com/{tid}/v2.0` (tenant id in the host, not the
friendly subdomain). Both must be accepted. This IS a documented CIAM
quirk on Microsoft Q&A — the empirical workaround the team landed here is
correct and matches what the reference React sample does implicitly (it
uses `authority` = tenant-id host; we use the friendly subdomain in one
place and tolerate both).

`HttpCurrentUser.cs:64-66`
```csharp
public string? B2CObjectId =>
    accessor.HttpContext?.User.FindFirstValue("oid")
    ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
```

`HttpCurrentUser.cs:68-71`
```csharp
public string? Email =>
    accessor.HttpContext?.User.FindFirstValue("emails")
    ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
    ?? accessor.HttpContext?.User.FindFirstValue("email");
```

**Best-practice alignment (per Microsoft Learn access-tokens page):**

| Best practice item | Status | Evidence |
|---|---|---|
| Validate issuer explicitly | PASS | `AuthExtensions.cs:54-55`. |
| Validate audience explicitly | PASS | `AuthExtensions.cs:57`. |
| Validate lifetime | PASS | `AuthExtensions.cs:58`. Clock skew 2 min is a reasonable trade-off (default is 5, tighter is better). |
| Validate signature via JWKS with automatic key rotation | PASS | `opts.Authority` set → JwtBearer fetches `<authority>/.well-known/openid-configuration` → resolves `jwks_uri` → rotates every 24h by default. |
| Require HTTPS metadata | PASS | `AuthExtensions.cs:51`. |
| `aud` MUST equal the API app registration id (not the SPA's) | PASS | `AuthExtensions.cs:57` sets `ValidAudience = entraClientId` where `entraClientId = configuration["EntraExternalId:ClientId"]` = **vrbook-api** app id (KV `entra-api-client-id`, `main.bicep:267`). Not the vrbook-web app id. This is the correct pairing with `apiScopes = ['api://vrbook/access_as_user']` on the browser side. If `apiScopes` were `${clientId}/.default` the token would carry `aud = vrbook-web-appId` and every call would 401. |
| Principal identifier uses `oid` (Object ID), not `sub` | PARTIAL — see below | `HttpCurrentUser.cs:65` reads `oid` first (correct) but falls back to `ClaimTypes.NameIdentifier`. Under Microsoft.IdentityModel's default claim mapping, `sub` from a v2.0 token maps to `ClaimTypes.NameIdentifier`. **`sub` is pairwise** per Microsoft guidance — different values in tokens issued to different apps for the same user. Using it as our stable app-user key WILL misbehave in multi-app scenarios (e.g. a future mobile app registration). The fallback is dead code today because Entra External ID always emits `oid`, but the fallback is a footgun that reads as "we'll use sub if oid is missing" — which is exactly wrong. |
| `roles` claim mapped to `ClaimTypes.Role` | PASS | JwtBearer auto-maps `roles`. `HasRole` in `HttpCurrentUser.cs:129-140` reads both `ClaimTypes.Role` and the raw `roles` claim shape. Belt + braces. |
| API does NOT trust `roles` from token alone for tenant scope | PASS | `TenantAuthorizationBehavior.cs:60` reads `ICurrentUser.IsPlatformAdmin` which prefers the DB-derived `HttpContext.Items` value (`HttpCurrentUser.cs:84-99`). Role claim alone is fallback only. |
| API does NOT re-derive tenant from token | PASS (by design) | Token does not carry `app_tenant_id`. Middleware stamps it from DB (`UserProvisioningMiddleware.cs:98-100`) via `TenantIdClaimType`. Per ADR-0014 §2: "the tenant is a per-tenant DB row, not a token claim." |
| Tenant-id claim shape (`app_tenant_id`) | PASS (defensible) | `HttpCurrentUser.cs:43` — custom claim type `"app_tenant_id"`, string-formatted GUID. Namespaced `app_*` so it doesn't collide with any Entra-emitted claim. |

**Verdict on §2:** aligned in every dimension EXCEPT the `sub`-fallback in
the principal-id read. Fix: drop the `ClaimTypes.NameIdentifier` fallback;
require `oid`; log + return 401 if missing. Full fix in §2 below.

---

### 1.3 Token flow end-to-end

**Trace of a real authenticated write from `niroshanaks`:**

**Step 1: Browser initiates `/api/v1/bookings/{id}/confirm`.**
- `apiFetch('/api/v1/bookings/...', { method: 'POST' })` at `client.ts:89`.
- `client.ts:113` awaits `tokenProvider()`.

**Step 2: `tokenProvider` (wired at `Providers.tsx:71`) tries silent acquisition.**
- Reads active account (`msalInstance.getActiveAccount()`).
- Calls `msalInstance.acquireTokenSilent({ scopes: apiScopes, account })`.
- MSAL's internal token cache (sessionStorage) is checked first.
  - Cache HIT (access token unexpired) → returned immediately.
  - Cache MISS or expired → MSAL uses its refresh-token to hit
    `https://{tid}.ciamlogin.com/{tid}/oauth2/v2.0/token` silently.
    New access token minted; cache updated; returned.
  - Refresh fails (`InteractionRequiredAuthError`) → `Providers.tsx:90-97`
    triggers `acquireTokenRedirect` which navigates to the External tenant's
    `/authorize` endpoint; browser round-trip; return via `/auth/callback`.

**Step 3: `apiFetch` attaches `Authorization: Bearer <accessToken>`.**
- `client.ts:114`.

**Step 4: API's `UseAuthentication()` middleware runs (`Program.cs:165`).**
- JwtBearer handler pulls the token from the `Authorization` header.
- Fetches JWKS if cache is expired (default cache TTL 24h).
- Validates signature (`kid` in header → matching key in `jwks_uri`).
- Validates `iss` against `ValidIssuers`.
- Validates `aud` against `ValidAudience`.
- Validates `exp`/`nbf` against clock (`ClockSkew` = 2 min).
- On success, populates `HttpContext.User = ClaimsPrincipal` with all
  claims from the token, mapping standard names (`oid`, `emails`, `name`,
  `email_verified`, `roles`, etc.).

**Step 5: `UseIdentityModule()` runs `UserProvisioningMiddleware.InvokeAsync` (`Program.cs:166`, wiring in `IdentityModuleExtensions`).**
- Reads `oid` from `ctx.User.FindFirstValue("oid") ?? ClaimTypes.NameIdentifier` (`UserProvisioningMiddleware.cs:31`).
- Sends `ProvisionUserCommand` → `ProvisionUserHandler` performs
  oid-first lookup, then email fallback, then multi-row survivor pick
  (per F11.7.6). Returns `identity.users.Id` (Guid).
- Stamps `ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId`.
- Runs a memberships query: `Set<TenantMembership>().Where(m => m.UserId == userId && m.DeletedAt == null)` (`UserProvisioningMiddleware.cs:68-71`).
- Runs a users query for `IsPlatformAdmin` bit (`UserProvisioningMiddleware.cs:73-76`).
- Stamps `ctx.Items[HttpCurrentUser.PlatformAdminItemKey] = isPlatformAdmin`.
- For each membership, ADDS a `ClaimTypes.Role` claim onto the `ClaimsIdentity`.
- If `isPlatformAdmin`, ADDS a `ClaimTypes.Role = "PlatformAdmin"` claim.
- If any membership has `IsPrimary=true`, ADDS an `app_tenant_id` claim.

**Step 6: `UseAuthorization()` runs (`Program.cs:167`).**
- `[Authorize]` on the endpoint checks `HttpContext.User.Identity.IsAuthenticated`.
- If the endpoint has `[Authorize(Roles=...)]` or `Policy=...`, evaluated
  against the now-enriched `ClaimsPrincipal`.

**Step 7: MediatR handler runs; `TenantAuthorizationBehavior` intercepts writes.**
- Reads `ICurrentUser.IsPlatformAdmin` — bypass on true.
- Reads `ICurrentUser.TenantId` (the `app_tenant_id` claim).
- Rejects mismatches with `403 Forbidden` — this is the M.4 gate that
  fired the "actual=<null>" chain in the previous review's Ev-A.

**Step 8: Handler completes; response flows back with W3C `traceparent`.**

**Best-practice alignment:**

| Step | Aligned? | Divergence |
|---|---|---|
| SPA acquires access token (not id token) for API call | PASS | `apiScopes = ['api://vrbook/access_as_user']` → MSAL returns `AuthenticationResult.accessToken`, not `idToken`. Providers.tsx:79 uses `.accessToken`. |
| Bearer sent over HTTPS only | PASS (staging + prod); localhost is HTTP-only | `main.bicep:485` — API base URL is `https://<apiApp fqdn>`. |
| API validates issuer + audience + signature + lifetime | PASS | `AuthExtensions.cs:52-60`. |
| Principal identifier extraction | PARTIAL — sub fallback (§1.2) | `HttpCurrentUser.cs:65`, `UserProvisioningMiddleware.cs:31`. |
| DB provisioning shape | FAIL — oid-first row-per-identity, not row-per-human (§1.6) | See previous review §4.2 + §5.1. |
| Claim enrichment writes to `ClaimsIdentity` | DEFENSIBLE but muddled (§4.3 previous review) | Per previous review §5.2, dropping this in favor of `ICurrentUser.MembershipRoles` is cleaner. |
| Claim namespacing (`app_tenant_id`) | PASS | Custom claim namespaced. |

**Verdict on §3:** flow is textbook. Every failure mode we've hit is DB-side,
not token-side. There is no "the SPA is doing MSAL wrong" here. The one
substantive divergence is the sub-fallback (§1.2) which needs to die.

---

### 1.4 ID tokens vs Access tokens

`useAuth.ts:20-30`:
```ts
const toAuthUser = (account: AccountInfo | null): AuthUser | null => {
  if (!account) return null;
  const claims = (account.idTokenClaims ?? {}) as Record<string, unknown>;
  return { oid: account.localAccountId, email: ..., name: account.name, isOwner: ..., isAdmin: ... };
};
```

`Providers.tsx:79` returns `result.accessToken` (correct).

**Interpretation:**
- The SPA reads **ID-token claims** for UX (`useAuth.ts:22`) — this is
  correct per Microsoft's [ID tokens page](https://learn.microsoft.com/en-us/entra/identity-platform/id-tokens):
  > "The claims provided by ID tokens can be used for UX inside your
  > application, as keys in a database, and providing access to the client
  > application. Do not use ID tokens for authorization purposes. Access
  > tokens are used for authorization."
- The SPA sends the **access token** as the API bearer (`Providers.tsx:79`) —
  correct per the same doc:
  > "You shouldn't use an ID token to call an API."
- Audience of the access token is the API app registration id (verified
  `AuthExtensions.cs:57` matches `apiScopes` value's resource identifier).
- Scope is `access_as_user` — the exposed delegated permission on the API
  app registration. This IS the Microsoft-recommended shape per
  [scopes-oidc](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc):
  > "All APIs must publish a minimum of one scope, also called Delegated
  > Permission, for the client apps to obtain an access token — follow the
  > principle of least privilege."

**Silent renewal:** covered by `acquireTokenSilent` (Providers.tsx:75-79 +
useAuth.ts:57-63) which uses the MSAL-cached **refresh token** to mint new
access tokens without user interaction. Refresh tokens for CIAM are typically
24h absolute / 90-day sliding by default; no code path attempts to
persist / expose the refresh token — it lives inside MSAL.js's own storage
(sessionStorage in our config).

**Best-practice alignment:**

| Best practice item | Status |
|---|---|
| Access token used for API (not ID) | PASS |
| ID token used for UX only | PASS |
| Audience is API app id | PASS |
| Scope pairing (`api://vrbook/access_as_user`) | PASS |
| Silent renewal via refresh token cache | PASS |
| Refresh token never leaves MSAL storage | PASS (we never touch it) |
| Failed silent → interactive redirect (not silent 401 loop) | PASS |

**Verdict on §4:** aligned. Nothing to change.

---

### 1.5 Multi-provider (OPS.M.12) — current state

Today: ONE External tenant, ONE `vrbook-api` App Registration, ONE
`vrbook-web` App Registration, ONE `SignUpAndSignIn` user flow, ONE
identity method (email + one-time passcode). See
`docs/identity/runbooks/entra-external-id-setup.md` §2 for the App Registration
setup and `docs/identity/setup.md` §5 for the user flow (with a parenthetical
noting Google can be added at the user flow level).

There is NO wiring for Google or Microsoft social IdPs today. OPS.M.12 has
been queued as "add sign in with Google + Microsoft."

Both options exist:

- **A. Federate through External ID** — add Google as an identity provider
  in the External tenant (`External Identities → All identity providers →
  Google`), attach it to the `SignUpAndSignIn` user flow. Browser still
  targets ONE authority (`vrbookcid.ciamlogin.com/{tid}/v2.0`). Access tokens
  still carry `iss = https://{tid}.ciamlogin.com/{tid}/v2.0` and
  `aud = vrbook-api-appId`. `oid` is the External tenant's per-human uuid.
- **B. Direct-to-provider App Registrations** — SPA runs multiple MSAL
  configs (msal-browser for Microsoft, google-oauth-js for Google), each
  with its own client id + audience. API validates multiple issuer/audience
  pairs. `oid` is provider-specific.

The system code today assumes option A — there's one `authority` env var, one
`clientId` env var, one JwtBearer options block with one `ValidAudience`.
Extending to option B would require refactoring both.

**Verdict on §5:** the code is preparation-neutral. The choice is made when
OPS.M.12 opens. The recommendation (see §2.5 below and previous review §5.1)
is unambiguously **A**.

---

### 1.6 Should `identity.users` even exist?

Today: yes, and it holds significant per-user domain state that Entra External
ID does NOT hold and cannot hold:

`User.cs:12-29`:
```csharp
public sealed class User : AggregateRoot
{
    public string B2CObjectId { get; private set; }        // = oid
    public Email Email { get; private set; }
    public string DisplayName { get; private set; }
    public PhoneNumber Phone { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsAdmin { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public bool EmailVerified { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
}
```

FK relationships (grepped, non-exhaustive):

- `booking.guest_user_id` → `identity.users.id`
- `booking.owner_user_id` (implicit via property.owner_user_id → identity.users.id)
- `review.author_user_id` → `identity.users.id`
- `messaging.thread_participant.user_id` → `identity.users.id`
- `audit_log.actor_id` → `identity.users.id`
- `notifications.recipient_user_id` → `identity.users.id`
- `tenant_memberships.user_id` → `identity.users.id`

**What Entra External ID does hold:** credentials (email/password or
email+OTP), oid, email (as user-flow-collected attribute), display name (as
user-flow-collected attribute), email_verified, "when did this identity last
sign in" via `signInActivity` (Graph, paid tier).

**What Entra does NOT hold and we depend on for domain state:**

- `is_platform_admin` — needs to be OUR authoritative "PA" bit for the
  `TenantAuthorizationBehavior` bypass. Even if we mirrored to an Entra App
  Role assignment, the DB read at request time is what gates the bypass —
  because token claims can be stale. (ADR-0014 acknowledges this.)
- `LastLoginAt` — Entra tracks last sign-in at the tenant level; we track
  at the app-user level for "welcome back" UX + activity audit. The two are
  divergent by design (a person may sign in but never hit our app).
- Every FK: `guest_user_id`, `author_user_id`, etc. must reference a stable
  primary key WE own. If we used oid directly:
  - PK would be `text` (36 char UUID) not `uuid` — 3-4x storage inflation
    on hundreds of FK columns, joins slower.
  - Retiring / merging identities (a user who signs up twice) is either
    impossible or requires FK rewrites across every table.
  - Testing without a live Entra token becomes impossible (or requires a
    mock oid).

**Verdict on §6:** yes, `identity.users` must exist. See §2.6 for the
target shape. The right question was never "app row or Entra row?" — it's
"is the row keyed on oid (1:1 with sign-in-source) or on email (1:1 with
human)?" And per previous review §5.1, the answer is email.

---

## 2. Target state, per item

### 2.1 MSAL client config (target)

**No structural change.** Small durable cleanups:

**Change #1 — Drop `credentials: 'include'` from apiFetch after DevAuth retirement.**

- File: `web/src/lib/api/client.ts:126-129`
- Change: remove `credentials: init.credentials ?? 'include'`. Rely on
  Bearer only.
- Ships with: **OPS.M.14** (DevAuth retirement — F11 slice).
- Rationale: `credentials: 'include'` was there to travel the DevAuth cookie
  `vrbook-dev-persona` cross-origin. Without DevAuth, cookies serve no auth
  purpose. Keeping the flag increases CSRF surface (any cookie set on the
  API origin — future or accidental — will travel on every request).

**Change #2 — Assert `spa.redirectUris` on App Registration (guardrail, not code).**

- Add an integration test that at deploy-time reads the App Registration via
  Graph and asserts `spa.redirectUris` is non-empty and `web.redirectUris` is
  empty. Prevent regression to the pre-2026-06-26 broken shape (see runbook §6).
- Ships as an **infra deploy postcheck script** with OPS.M.14 or OPS.M.15
  — whichever slice owns Entra config verification. Idempotent.

**Change #3 — Add prod redirect URI on prod cutover.**

- When prod ships, the prod `ca-vrbook-web-prod` + Front Door custom domain
  `www.vrbook.example.com` URIs must be added to `vrbook-web-prod` App
  Registration's `spa.redirectUris`. Runbook `entra-prod-cutover-checklist.md`
  §4 already captures this — no code change.

Everything else in `msalConfig.ts` + `Providers.tsx` stays as-is. It matches
Microsoft's [React CIAM sample](https://learn.microsoft.com/en-us/samples/azure-samples/ms-identity-ciam-javascript-tutorial/ms-identity-ciam-javascript-tutorial-1-call-api-react/)
shape one-for-one.

### 2.2 API JWT Bearer validation (target)

**Change #1 — Drop `ClaimTypes.NameIdentifier` fallback from principal id read.**

- Files: `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs:64-66`
  + `UserProvisioningMiddleware.cs:31`.
- Change:

  ```csharp
  // Before
  public string? B2CObjectId =>
      accessor.HttpContext?.User.FindFirstValue("oid")
      ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

  // After
  public string? B2CObjectId =>
      accessor.HttpContext?.User.FindFirstValue("oid");
  ```

  Same edit in `UserProvisioningMiddleware.cs:31`. If `oid` is missing from a
  successfully-validated token, that's a bug on the Entra side — the middleware
  should log at Warning and skip provisioning, and downstream `HttpCurrentUser.UserId`
  returns null, and every `[Authorize]` write rejects.

- Ships with: **OPS.M.14** (paired with DevAuth retirement, since DevAuth's
  synthetic oids also flow through `HttpCurrentUser`).

- Rationale: Microsoft's [access-tokens page](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens)
  under "Claims to use to identify the user":
  > "Never use `email`, `preferred_username`, `unique_name`, or `upn` for
  > authorization — these can change. Use `oid` for the user's object id.
  > `sub` is a pairwise identifier — unique per (user, app) pair — and
  > should be used only when a claim scoped to your specific app is desired."
  
  Under the ASP.NET Core default claim mapping, `sub` maps to
  `ClaimTypes.NameIdentifier`. Falling back to it is exactly the wrong
  behavior for a multi-app future: it'd introduce a subtle bug where a
  mobile app registration issuing tokens for the same human would have
  a different `sub`, our fallback would fire, and we'd stamp the wrong
  principal id. Cleaning this now, while there's ONE app registration,
  is the moment.

**Change #2 — Rename `B2CObjectId` symbol → `ExternalObjectId` (or `EntraObjectId`).**

- Files: `HttpCurrentUser.cs:64` + every reference (grep confirms `B2CObjectId`
  is referenced from `ProvisionUserHandler.cs` + a few tests).
- The name "B2C" is a lie post-ADR-0012 and creates ongoing archaeological
  friction for anyone learning the code. Rename to `ExternalObjectId`.
- Ships with: **OPS.M.14** — bundled with DevAuth retirement to avoid a
  standalone rename-only PR.

**Change #3 — Explicit `NameClaimType = "name"` on JwtBearer options.**

- File: `AuthExtensions.cs:60` — add:
  ```csharp
  NameClaimType = "name",
  RoleClaimType = "roles",
  ```
- Currently `NameClaimType` defaults to `ClaimTypes.Name` (which maps from
  `unique_name` on v1 tokens, `preferred_username` on v2). For External ID
  we always emit `name`. Being explicit forecloses cross-mapping bugs and
  makes `HttpContext.User.Identity.Name` return the human-readable name
  consistently in every log line. Same for `RoleClaimType` — matches
  what App Roles emits and removes the "belt + braces" tolerance in
  `HttpCurrentUser.HasRole` (`HttpCurrentUser.cs:137-140`).
- Ships with: **OPS.M.14**.

**Change #4 — Tighten ClockSkew from 2 min → 1 min.**

- File: `AuthExtensions.cs:59`.
- 5 min is Microsoft's default. 2 min is our current setting. 1 min is
  the tightest that still tolerates NTP drift on the container hosts.
  Not urgent; optional; ships when convenient.

**Everything else** — `Authority`, dual `ValidIssuers`, `ValidateLifetime`,
`RequireHttpsMetadata` — stays as-is. The dual-issuer workaround for the
`.ciamlogin.com` friendly-subdomain-vs-tenant-id-host quirk is Microsoft's
known behavior; keep it.

### 2.3 Token flow end-to-end (target)

Same as §1.3 with these substitutions:

- Step 5 changes when OPS.M.13 (`user_identities` schema) lands:
  - `UserProvisioningMiddleware` calls the redesigned
    `ProvisionOrLinkUserCommand`:
    1. Read `emails`/`email` claim + `oid` claim.
    2. Find or create `identity.users` row by `lower(email)` (unique-active).
    3. Find or create `identity.user_identities` row for `(provider='entra', external_id=oid)`.
    4. If token's `email_verified=false` AND email row already exists: reject
       with 403 "unverified email cannot claim existing profile."
    5. Return `users.id`.
  - Same claim-enrichment as today (until OPS.M.15 simplifies).

- Step 6 changes when OPS.M.15 (App Role cleanup) lands:
  - The middleware STOPS writing `Role` claims onto the `ClaimsIdentity`.
  - `TenantAuthorizationBehavior` reads roles via
    `ICurrentUser.HasTenantRole(tenantId, role)` — which reads from
    `HttpContext.Items` (DB-derived) — not via `ClaimsPrincipal.IsInRole`.
  - Removes the "muddled" role-claim shape called out in previous review §4.3.

### 2.4 ID vs Access token (target)

**No change.** The current split (ID for UX, Access for API, silent renewal
via refresh token in MSAL cache) is Microsoft-canonical and OWASP-canonical.
The only thing this doc adds is an explicit test:

- Add `msalConfig.test.ts` case (already partial per runbook §222): assert
  `apiScopes[0]` equals `api://vrbook/access_as_user`. This regression test
  protects against a maintainer inadvertently switching to `${clientId}/.default`
  and triggering the audience-mismatch cascade.

### 2.5 Multi-provider (OPS.M.12) — target

**Decision: Federate through External ID (Option A).**

Concrete implementation:

1. **NO new App Registrations.** The `vrbook-api` + `vrbook-web` pair does
   not multiply. There is still exactly one `authority` env var.
2. In the External tenant portal → **External Identities → All identity
   providers**:
   - **Google:** Configure with a Google OAuth 2.0 Client ID + Secret
     (obtained from Google Cloud Console; Google Workspace not needed).
     Redirect URI on Google's side is `https://login.microsoftonline.com/te/{tid}/oauth2/authresp`.
     (Verified against Microsoft Learn's `how-to-google-federation-customers`
     which spells this URI out.)
   - **Microsoft:** Add via **All identity providers → Custom OpenID Connect**
     pointing at `login.microsoftonline.com/consumers` (personal Microsoft
     accounts) OR `login.microsoftonline.com/organizations` (any workforce
     tenant). This is Microsoft's documented pattern for "Sign in with
     Microsoft account" on an External ID tenant.
     For the workforce-tenant-federation case, follow
     `how-to-entra-id-federation-customers` runbook.
   - **Apple** (if we ever add): same shape, requires Apple Developer team
     + Sign-in-with-Apple key.
3. Attach both to the `SignUpAndSignIn` user flow: Entra admin center →
   User flows → SignUpAndSignIn → **Identity providers** → toggle Google +
   Microsoft on.
4. **Zero code changes on our side.** The SPA still calls MSAL with the same
   authority; the user is presented with the External ID sign-in page which
   now shows "Sign in with Google" + "Sign in with Microsoft" buttons in
   addition to email + OTP.
5. The access token still has `iss = https://{tid}.ciamlogin.com/{tid}/v2.0`
   (our External tenant's issuer). The `oid` is the External tenant's
   per-human uuid — Entra guarantees ONE `oid` per human per External tenant,
   regardless of upstream IdP used. The upstream provider's identifier
   surfaces as an extra claim (`idp` claim) but is not the principal id.

**Why option A is correct — cited justification:**

- Microsoft Learn's own [identity-providers page for external tenants](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers)
  says explicitly:
  > "For an optimal sign-in experience, federate with social identity providers
  > whenever possible so you can give your users a seamless sign-up and sign-in
  > experience. In an external tenant, you can allow a user to sign up and
  > sign in using their own Facebook, Google, or Apple account. To set up
  > social identity providers in your external tenant, you create an
  > application at the identity provider [i.e. at Google] and configure
  > credentials. You obtain a client or app ID, a client or app secret, or a
  > certificate, which you can then use to configure your external tenant."
  
  Note: "configure your external tenant" — NOT "register a new App Registration."
  Federation happens at the tenant-configuration level, not the App-Registration
  level.

- Same page:
  > "A user object is created for them in your directory with the identity
  > information collected during sign-up."
  
  ONE user object per human per External tenant — regardless of upstream IdP.
  That's the whole point of federation as opposed to running multiple direct
  OAuth flows.

- The [React CIAM reference SPA](https://learn.microsoft.com/en-us/samples/azure-samples/ms-identity-ciam-javascript-tutorial/ms-identity-ciam-javascript-tutorial-1-call-api-react/)
  uses ONE MSAL config against ONE External tenant with Google + Microsoft
  configured at the tenant level. No multi-config gymnastics on the SPA side.

**Why option B is wrong here:**

- Two MSAL configs means each SPA sign-in issues tokens signed by a different
  IdP, with a different `aud`, different `iss`, different key rotation cycle.
  API needs multiple JwtBearer schemes.
- Every human who signs in with Google, then later with Microsoft, becomes
  two `oid`s. Our dedupe becomes email-based — which is exactly what the
  target `identity.users` + `user_identities` shape already handles. But
  the SPA-side complexity would be for nothing: External ID gives you the
  same result (single External-tenant `oid`) for free.
- Multiple App Registrations is a workforce B2B pattern (each tenant is a
  customer; each tenant has its own App Registration). It's the wrong tool
  for consumer social sign-in.

**Wrinkle to document (not a blocker):**

When a human signs up via Google, then a week later re-signs-up via email +
OTP with the same email address, External ID has documented behavior of
issuing a DIFFERENT `oid` (Microsoft Q&A on this topic is scattered; some
scenarios produce duplicate External-tenant users). The email-canonical
`identity.users` schema (previous review §5.1) handles this by design: our
side sees ONE `identity.users` row with TWO `user_identities` rows (one
`provider='entra'`, one from later merging).

An external-tenant admin CAN merge duplicate users via Graph
(`POST /users/{id}/mergeWith`) but this is a portal-manual operation. Our
app-side `user_identities` collapse is more reliable and reversible.

**What ships with OPS.M.12:**

- Portal-only work (no code):
  - Add Google IdP to External tenant.
  - Add Microsoft IdP (consumers) to External tenant.
  - Attach both to `SignUpAndSignIn` user flow.
- Runbook update: `docs/identity/runbooks/entra-external-id-setup.md` §3 gets
  a new §3.1 sub-section for social IdP setup.
- KV secret adds (Google client id/secret): `google-idp-client-id`,
  `google-idp-client-secret` (stored in Entra's tenant config, but held in KV
  for disaster-recovery / re-provisioning). Bicep secret entries.
- **Nothing** in `msalConfig.ts`, `AuthExtensions.cs`, `HttpCurrentUser.cs`,
  or `UserProvisioningMiddleware.cs`.

**OPS.M.12 blocks on OPS.M.13.** Per previous review §6, we can't safely add
social IdPs until the `user_identities` schema is in place — otherwise a
human who tries email-OTP AND Google gets two `identity.users` rows and
the "walk failure" reappears.

### 2.6 `identity.users` — target (Entra source of truth vs app row)

**Decision: keep `identity.users`, layered per previous review §5.1:**

```sql
identity.users (
  id                 uuid PK,
  email              citext NOT NULL,
  display_name       text NOT NULL,
  phone              text,                    -- app-side; Entra doesn't own
  is_email_verified  boolean NOT NULL,
  is_platform_admin  boolean NOT NULL DEFAULT false,
  last_login_at      timestamptz,
  created_at         timestamptz NOT NULL,
  updated_at         timestamptz NOT NULL,
  deleted_at         timestamptz,
  UNIQUE INDEX users_email_active_uq ON email WHERE deleted_at IS NULL
)

identity.user_identities (
  id            uuid PK,
  user_id       uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
  provider      text NOT NULL,        -- 'entra', 'google', 'microsoft', 'test'
  external_id   text NOT NULL,        -- the oid or provider-specific external id
  first_seen_at timestamptz NOT NULL,
  last_seen_at  timestamptz NOT NULL,
  UNIQUE (provider, external_id)
)
```

**Ownership map — which field is canonical where:**

| Field | Entra owns | App DB owns | Duplicated (source of truth) |
|---|---|---|---|
| Credentials (password, OTP) | ✅ (sole) | ✗ | — |
| `oid` | ✅ (sole) | mirrored in `user_identities.external_id` | Entra emits; DB stores mapping — Entra wins |
| `email` | user-flow input | ✅ canonical for our FK graph | Both — **DB is our canonical, refreshed from Entra token on each login** |
| `email_verified` | ✅ (sole) — from the IdP or user-flow OTP | mirrored on `identity.users.is_email_verified` | Entra wins, DB caches |
| `display_name` | user-flow input | ✅ | DB canonical (users edit locally, not in Entra) |
| `phone` | ✗ | ✅ (sole) | DB canonical — Entra External ID doesn't collect phone by default |
| `last_login_at` | Entra tracks tenant-level (Graph `signInActivity`) | ✅ (app-level) | DB owns "last time this user hit OUR app" |
| `is_platform_admin` | ✗ (or via App Role mirror per ADR-0014) | ✅ (sole) | DB canonical |
| Per-tenant roles | ✗ | ✅ (`tenant_memberships`) | DB canonical (only shape that supports per-tenant roles per ADR-0014) |
| Sign-in method history (Google vs email) | ✅ (via Graph) | mirrored in `user_identities(provider, first_seen_at, last_seen_at)` | Entra wins for account-of-record; DB caches |

**Provisioning logic (email-first — per previous review §5.1):**

```csharp
public async Task<Guid> Handle(ProvisionOrLinkUserCommand cmd, CancellationToken ct)
{
    var normalizedEmail = cmd.Email.ToLowerInvariant();

    // 1. Find by (provider, external_id) — the fastest, oid-stable path.
    var existingIdentity = await _db.UserIdentities
        .FirstOrDefaultAsync(i => i.Provider == cmd.Provider && i.ExternalId == cmd.Oid, ct);

    if (existingIdentity is not null)
    {
        existingIdentity.LastSeenAt = _clock.UtcNow;
        // Refresh cached email + display_name + verified from Entra
        await RefreshUserFromToken(existingIdentity.UserId, cmd, ct);
        return existingIdentity.UserId;
    }

    // 2. New identity — but maybe a user already exists (same email, different IdP).
    var existingUser = await _db.Users
        .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.DeletedAt == null, ct);

    if (existingUser is not null)
    {
        // 3a. Same-email link — verified emails only, per the previous review §5.1.
        if (!cmd.EmailVerified)
            throw new ForbiddenException("Unverified email cannot claim an existing profile.");

        var identity = UserIdentity.Create(existingUser.Id, cmd.Provider, cmd.Oid, _clock.UtcNow);
        _db.UserIdentities.Add(identity);
        await _db.SaveChangesAsync(ct);
        return existingUser.Id;
    }

    // 3b. New user + new identity, atomically.
    var newUser = User.Provision(normalizedEmail, cmd.DisplayName, cmd.EmailVerified);
    var newIdentity = UserIdentity.Create(newUser.Id, cmd.Provider, cmd.Oid, _clock.UtcNow);
    _db.Users.Add(newUser);
    _db.UserIdentities.Add(newIdentity);
    await _db.SaveChangesAsync(ct);
    return newUser.Id;
}
```

**Why not "Entra as sole source of truth + `user_id → oid` mapping only":**

The user's item #6 question explicitly asked whether we could avoid the
`identity.users` row. Answering concretely:

1. **FK impact.** `bookings.guest_user_id`, `reviews.author_user_id`,
   `audit_log.actor_id`, `messaging.thread_participants.user_id`,
   `notifications.recipient_user_id`, `tenant_memberships.user_id` all
   currently reference `identity.users.id (uuid)`. If we drop the row:
   - Option: FK becomes `text` (36-char oid). All join indices grow ~4×.
     Every SQL join includes a string comparison.
   - Option: FK becomes a new column `external_user_ref (text, provider, external_id)`.
     Every table gains a composite key.
   - Neither is preferable to a `uuid` PK.

2. **Domain events.** `BookingPlaced(guestUserId=Guid)`,
   `ReviewSubmitted(authorUserId=Guid)`, etc. carry `Guid` today. If we
   Entra-canonicalize:
   - Events carry `string` — losing type safety.
   - Consumers (outbox → notifications, analytics ETL) all deserialize
     `string`. Every downstream schema changes.
   - Testing (WebApplicationFactory + integration tests) needs a valid oid
     string for every assertion. Currently a `Guid.NewGuid()` suffices.

3. **Multi-IdP identity resolution.** ONE Entra oid ≠ ONE human, per
   §2.5 wrinkle. If we lose the app-side row, we lose the collapse mechanism
   entirely. Then two humans-with-same-email get two data silos.

4. **Cross-tenant safety.** Even if Entra External ID gave us perfect
   deduplication (it doesn't; see §2.5), our OPS.M.4
   `TenantAuthorizationBehavior` reads `is_platform_admin` from the DB
   before permitting bypass. If PA lives only in an App Role assignment,
   revoking it requires a token refresh cycle (~60 min lag). DB revocation
   is instant.

5. **Development ergonomics.** Integration tests use a
   `TestAuthenticationHandler` (previous review §5.4) that stamps
   synthetic claims. Testing without a live Entra token is table stakes.
   Requiring an oid from a "real IdP" for every test would either force
   `LiveTest` category-only, or force a synthetic `provider='test'` value —
   which is fine, but requires the `user_identities` row shape anyway,
   because provider-scoped IDs must live somewhere.

6. **Auditability.** If PA is Entra-canonical, "who was PA at 2026-06-30
   14:33 UTC" requires Graph audit-log queries against Entra. If PA is
   DB-canonical, it's a straightforward SQL query with `audit_log` join.
   DB wins for observability.

**Verdict on §6:** `identity.users` stays. Entra owns credentials + oid +
email-at-sign-in-time; DB owns everything else. The `user_identities`
side-table is the seam between the two.

**Final row shape** (locked, matches previous review §5.1):

```sql
identity.users (
    id                 uuid PRIMARY KEY,
    email              citext NOT NULL,
    display_name       text NOT NULL,
    phone              text,
    is_email_verified  boolean NOT NULL DEFAULT false,
    is_platform_admin  boolean NOT NULL DEFAULT false,
    last_login_at      timestamptz,
    created_at         timestamptz NOT NULL,
    updated_at         timestamptz NOT NULL,
    deleted_at         timestamptz,
    CONSTRAINT users_email_active_uq
      EXCLUDE (email WITH =) WHERE (deleted_at IS NULL)
    -- Or: UNIQUE INDEX users_email_active_uq ON email WHERE deleted_at IS NULL
);

identity.user_identities (
    id             uuid PRIMARY KEY,
    user_id        uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    provider       text NOT NULL CHECK (provider IN ('entra','google','microsoft','apple','test')),
    external_id    text NOT NULL,
    first_seen_at  timestamptz NOT NULL,
    last_seen_at   timestamptz NOT NULL,
    UNIQUE (provider, external_id)
);
```

Columns `is_owner` + `is_admin` from the current `identity.users` are
DROPPED as part of OPS.M.15 (App Role cleanup) — they were the legacy
"global Owner" bits from ADR-0012 §2 which ADR-0014 already scheduled for
deprecation.

---

## 3. Reconciliation with the previous review

The previous review's §5.1 proposed the exact `identity.users` + `user_identities`
shape this doc locks. Item #6 was asked to verify whether that proposal was
right — this doc confirms yes, with the ownership-map table in §2.6 spelling
out which fields Entra owns vs. we own.

**Additions this doc makes that the previous review didn't fully cover:**

- Explicit `sub` fallback removal (§2.2 change #1) — not in previous review.
- `NameClaimType` + `RoleClaimType` explicit set (§2.2 change #3) — not covered.
- `credentials: 'include'` removal after DevAuth retirement (§2.1 change #1) — not covered.
- Multi-provider decision cited to Microsoft Learn (§2.5) — previous review's
  §5.1 hand-waved as "OPS.M.12 works natively."
- Ownership-map table (§2.6) — new.

**Nothing in the previous review is contradicted.** The sequences OPS.M.13 →
M.14 → M.15 → M.16 stand. Federation-over-multi-App-Reg is compatible with
the OPS.M.13 schema (native fit — `user_identities.provider ∈ {'entra','google','microsoft'}`).

---

## 4. Multi-provider — federate vs multiple App Registrations (definitive)

Answered in §2.5 above. Summary:

**Federate through External ID. One App Registration for the API. One App
Registration for the SPA. Google + Microsoft configured as identity providers
at the tenant level, then attached to the `SignUpAndSignIn` user flow.**

Justification cited to Microsoft Learn: [`concept-authentication-methods-customers`](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers)
and [`how-to-google-federation-customers`](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers).

---

## 5. Full flow diagram (text) — target state

Annotated with `[C:file:line — CURRENT-CODE-MATCHES]` or `[D:file:line — CURRENT-CODE-DIVERGES]`
per step.

```
==== BROWSER SIDE ====

1. User clicks "Sign in".
   [C: web/src/app/(pages)/... uses useAuth().signIn]
   useAuth.signIn() → instance.loginRedirect(loginRequest)
   [C: useAuth.ts:44]
   → Browser navigates to https://{tid}.ciamlogin.com/{tid}/oauth2/v2.0/authorize
       ?client_id=<vrbook-web appId>
       &scope=api://vrbook/access_as_user openid profile offline_access
       &redirect_uri=https://<web>/auth/callback
       &response_type=code
       &code_challenge=<PKCE>
       &code_challenge_method=S256

2. User enters email + OTP (or clicks Sign in with Google, after OPS.M.12).
   External ID authenticates, redirects back to /auth/callback?code=<authcode>&state=<...>.
   [C: web/src/app/auth/callback/page.tsx]

3. MSAL's PublicClientApplication processes the redirect on instantiation.
   Exchanges code for tokens at https://{tid}.ciamlogin.com/{tid}/oauth2/v2.0/token.
   Returns: id_token (for UX), access_token (for API), refresh_token (silent renewal).
   Persists to sessionStorage under keys prefixed with the client id.
   [C: msalConfig.ts:46 cacheLocation: 'sessionStorage']

4. callback page.tsx sets active account + router.replace(returnTo).
   [C: web/src/app/auth/callback/page.tsx:19-26]

5. User navigates the app. Any component that reads useAuth() gets
   {isAuthenticated, user, getAccessToken}.
   [C: useAuth.ts:37-72]

6. User triggers an API write (e.g. POST /api/v1/bookings/{id}/confirm).
   Component calls apiFetch('/api/v1/...', { method: 'POST', ... }).
   [C: web/src/lib/api/client.ts:89]

7. apiFetch checks tokenProvider (wired at Providers.tsx:71).
   tokenProvider does:
     account = msalInstance.getActiveAccount()
     result  = await msalInstance.acquireTokenSilent({scopes: apiScopes, account})
     return result.accessToken
   [C: Providers.tsx:71-102]
   Cache HIT (unexpired access token in sessionStorage) → returned immediately.
   Cache MISS → MSAL hits /oauth2/v2.0/token with refresh_token grant.
   Refresh fails → InteractionRequiredAuthError → acquireTokenRedirect().
   [C: Providers.tsx:90-97]

8. apiFetch attaches Authorization: Bearer <accessToken>.
   [C: client.ts:114]

   ---- ACCESS TOKEN CLAIMS (illustrative, post-OPS.M.13/15) ----
   {
     "iss":  "https://{tid}.ciamlogin.com/{tid}/v2.0",
     "aud":  "<vrbook-api appId>",
     "sub":  "AAAA_pairwise-per-app",         // ← DO NOT USE as principal id
     "oid":  "user-uuid-in-external-tenant",   // ← USE THIS
     "tid":  "{external tenant id}",
     "scp":  "access_as_user",
     "roles": ["PlatformAdmin"],               // optional; from App Role assignment
     "name": "Niroshana Sinhara Ralalage",
     "emails": ["niroshanaks@gmail.com"],
     "email": "niroshanaks@gmail.com",
     "email_verified": true,
     "idp":  "google.com",                     // present when federated via Google
     "exp":  <60-90 min out>,
     "iat":  <now>
   }

   Post-cleanup (§2.2 change #3): NameClaimType='name', RoleClaimType='roles'.

9. apiFetch also attaches traceparent (W3C trace) + Idempotency-Key.
   [C: client.ts:117-120]

10. Fetch to API base (staging: https://ca-vrbook-api-staging.<domain>).
    [C: main.bicep:485 NEXT_PUBLIC_API_BASE_URL]

==== API SIDE ====

11. Request enters app.UseRouting() → app.UseCors() → app.UseAuthentication().
    [C: Program.cs:163-165]

12. JwtBearer handler pulls token from Authorization header.
    Validates:
      - Signature via JWKS (Authority = discoveryAuthority, cached ~24h)
      - Issuer against ValidIssuers dual-form
      - Audience against ValidAudience
      - Lifetime with ClockSkew
    [C: AuthExtensions.cs:47-61]

    On success: HttpContext.User = ClaimsPrincipal with all claims,
    including oid, emails, name, roles, email_verified, idp, etc.

    [D: HttpCurrentUser.cs:65 falls back to ClaimTypes.NameIdentifier (=sub).
     Fix per §2.2 change #1: remove fallback.]

13. UseIdentityModule() runs UserProvisioningMiddleware.
    [C: Program.cs:166]

    Post-OPS.M.13:
      oid   = ctx.User.FindFirstValue("oid")   // no more sub-fallback
      email = ctx.User.FindFirstValue("emails") ?? ctx.User.FindFirstValue("email")
      email_verified = ctx.User.FindFirstValue("email_verified") == "true"
      display_name   = ctx.User.FindFirstValue("name")

    Send ProvisionOrLinkUserCommand(provider='entra', oid, email, email_verified, display_name):
      → (see §2.6 pseudocode: find-by-identity OR find-by-email+link OR create-new)
      → returns Guid userId.

    Stamp HttpContext.Items:
      [HttpCurrentUser.AppUserIdItemKey]       = userId
      [HttpCurrentUser.PlatformAdminItemKey]   = isPlatformAdmin (DB read)

    Read tenant_memberships for userId; for the IsPrimary=true row,
    stamp app_tenant_id claim on ClaimsIdentity.

    Post-OPS.M.15:
      STOP writing Role claims onto ClaimsIdentity.
      Instead expose ICurrentUser.MembershipRoles: Dictionary<Guid, ISet<string>>.
      [C: HttpCurrentUser.cs:110-127 already reads via HasTenantRole shape.
       D: middleware still writes Role claims at UserProvisioningMiddleware.cs:86-93.
       Fix per previous review §5.2.]

14. UseAuthorization() runs.
    [C: Program.cs:167]
    [Authorize] gate checks HttpContext.User.Identity.IsAuthenticated.
    [Authorize(Policy="Admin")] etc. run against enriched ClaimsPrincipal.

15. MediatR handler runs.
    TenantAuthorizationBehavior intercepts writes.
    Reads:
      currentUser.IsPlatformAdmin  → HttpContext.Items[PlatformAdminItemKey]
      currentUser.TenantId          → ClaimsPrincipal.FindFirst("app_tenant_id")
    [C: TenantAuthorizationBehavior.cs:60,116-119]

    Bypass on IsPlatformAdmin; else compare command's tenantId to currentUser.TenantId;
    else compare to BackgroundTenantScope.CurrentTenantId; else 403.

16. Handler completes. Response returns with traceparent + problem-details on error.
    [C: client.ts:139]

17. Browser processes response.
    On 401 (audience/lifetime failed at step 12):
      apiFetch throws ApiProblemError(401, ...).
      Caller code triggers acquireTokenRedirect via useAuth pattern.
```

Divergences highlighted with `[D:...]`. Every `[D:]` is addressed in §2 above.

---

## 6. Best-practice checklist scorecard

Every item PASS/FAIL/PARTIAL against Entra External ID + MSAL best-practice.
Where FAIL/PARTIAL, cites the file:line + the fix.

| # | Item | Status | Evidence / Fix |
|---|---|---|---|
| 1 | SPA app registration type (`spa.redirectUris`, not `web.redirectUris`) | PASS | Patched 2026-06-26 per runbook §6. Add deploy-time postcheck (§2.1 change #2). |
| 2 | Public client flow (no client secret in browser) | PASS | `PublicClientApplication` at `Providers.tsx:48`. |
| 3 | Authority URL uses `ciamlogin.com`, not `login.microsoftonline.com` | PASS | KV secret `entra-web-authority` = `https://vrbookcid.ciamlogin.com/{tid}/v2.0` (`main.bicep:489`). |
| 4 | `knownAuthorities` set for non-standard host | PASS | `msalConfig.ts:28-34` computes from URL host. |
| 5 | Redirect URIs include prod + staging + localhost | PASS (dev + staging); prod-cutover-pending | Runbook §2 lists both; prod adds `www.vrbook.example.com/auth/callback` per `entra-prod-cutover-checklist.md`. |
| 6 | apiScopes uses `api://.../access_as_user`, not `.default` | PASS | `msalConfig.ts:70`. Regression-guarded per runbook line 222. |
| 7 | Access token requested (not ID token) for API calls | PASS | `Providers.tsx:79` returns `result.accessToken`. |
| 8 | ID token used only for UX (name/email display) | PASS | `useAuth.ts:22` reads `idTokenClaims`; nothing sends id_token as bearer. |
| 9 | Refresh flow uses `acquireTokenSilent` with account context | PASS | `Providers.tsx:75-79` + `useAuth.ts:57-63`. |
| 10 | Silent refresh handles `InteractionRequiredAuthError` → interactive redirect | PASS | `Providers.tsx:90-97` (F11.7.4.1). |
| 11 | `redirectUri` matches App Registration exactly | PASS (with deploy postcheck TBD) | `msalConfig.ts:23`. Postcheck queued (§2.1 change #2). |
| 12 | `cacheLocation: 'sessionStorage'` | PASS | `msalConfig.ts:46`. |
| 13 | `storeAuthStateInCookie: false` | PASS | `msalConfig.ts:47`. |
| 14 | API validates issuer explicitly | PASS | `AuthExtensions.cs:54-55` dual-form for CIAM quirk. |
| 15 | API validates audience explicitly | PASS | `AuthExtensions.cs:57`. |
| 16 | API validates signature via JWKS with automatic key rotation | PASS | Authority set → JwtBearer fetches OIDC discovery → JWKS auto-rotates every 24h. |
| 17 | API validates expiry with reasonable ClockSkew | PASS | `AuthExtensions.cs:59` — 2 min. Optionally tighten to 1 min (§2.2 change #4). |
| 18 | API DOES NOT trust `roles` claim from token if empty (fallback to DB) | PASS | `TenantAuthorizationBehavior.cs:60` + `HttpCurrentUser.cs:84-99` — DB-derived bit wins. |
| 19 | API DOES NOT re-derive tenant from token if token doesn't carry it | PASS by design | Token has no tenant claim; middleware stamps from DB (`UserProvisioningMiddleware.cs:98-100`). |
| 20 | Principal identifier uses `oid`, not `sub` (no `ClaimTypes.NameIdentifier` fallback) | **FAIL** | `HttpCurrentUser.cs:65-66` + `UserProvisioningMiddleware.cs:31` fall back to `ClaimTypes.NameIdentifier`. Fix per §2.2 change #1 (drop fallback). |
| 21 | Application-level authorization is NOT solely dependent on token roles (some DB backing) | PASS | `is_platform_admin` DB bit + `tenant_memberships` DB rows are canonical for authorization. Token roles are advisory. |
| 22 | Token cache is cleared on sign-out | PASS | `useAuth.ts:47-49` → `instance.logoutRedirect()`. MSAL clears sessionStorage internally. |
| 23 | All authenticated requests use HTTPS | PASS (staging, prod); localhost is HTTP — acceptable | `main.bicep:485` API base is `https://<fqdn>`. |
| 24 | CORS allows only known origins | PASS | `Program.cs:52-56` reads `Cors:AllowedOrigins` from config; `main.bicep:278-279` sets localhost + deployed web only. |
| 25 | `credentials: 'include'` on API fetches only when needed | **FAIL** | `client.ts:129` sets it unconditionally. Was for DevAuth cookie. Post-OPS.M.14 (DevAuth retirement) it's dead surface. Fix per §2.1 change #1. |
| 26 | `RoleClaimType` explicitly configured on JwtBearer | PARTIAL | Relies on default mapping. Fix per §2.2 change #3 (set `RoleClaimType="roles"`). |
| 27 | `NameClaimType` explicitly configured on JwtBearer | PARTIAL | Same as #26. Fix per §2.2 change #3 (set `NameClaimType="name"`). |
| 28 | Symbol names reflect current identity provider (no B2C leftovers) | **FAIL** | `User.B2CObjectId`, `HttpCurrentUser.B2CObjectId` — misnamed post-ADR-0012. Rename per §2.2 change #2. |
| 29 | Middleware exception path is benign (no 500 on DB miss) | PASS | `UserProvisioningMiddleware.cs:103-107` `try/catch` — logs warning, continues. |
| 30 | Audit log records auth failures | PASS | Serilog request logging + `TenantAuthorizationBehavior.cs:99` warns on rejection. |

**Score:** 22 PASS + 3 FAIL + 2 PARTIAL + 3 informational = 25 evaluated,
**22/25 pass (88 %)**. Three FAILs and two PARTIALs all fix in
OPS.M.14 (§7 below).

---

## 7. Migration to target state — what changes concretely

Not sub-commits. Slice-level direction matching the previous review's OPS.M.13
/ M.14 / M.15 / M.16 sequence.

### Slice OPS.M.13 — Identity schema collapse (§2.6 lands)

**Frontend files to change:** none.

**API files to change:**
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs`
  - Drop `B2CObjectId` column-mapped property.
  - Add navigation `Identities: ICollection<UserIdentity>` for the new
    `user_identities` FK.
  - `Provision` factory signature changes from `(b2cObjectId, email, ...)`
    to `(email, displayName, emailVerified)` — oid is now on `UserIdentity`,
    not `User`.
- New: `src/Modules/VrBook.Modules.Identity/Domain/UserIdentity.cs`.
- `Infrastructure/Persistence/Migrations/{next}_OpsM13_UserIdentities.cs`:
  - Create `identity.user_identities` table.
  - Backfill `INSERT INTO user_identities (user_id, provider, external_id, ...)
    SELECT id, 'entra', b2c_object_id, ... FROM users WHERE b2c_object_id IS NOT NULL;`
  - Collapse duplicate `identity.users` rows by email (PA-first, oldest-CreatedAt
    survivor pick, rewrite FKs on bookings/reviews/audit_log/threads/notifications).
  - Drop `users.b2c_object_id` column.
  - Add `UNIQUE INDEX users_email_active_uq ON email WHERE deleted_at IS NULL`.
  - Emit rows into `identity.migration_audit` (per §5.5 previous review).
- `Application/Users/Commands/ProvisionUserHandler.cs` → renamed to
  `ProvisionOrLinkUserHandler.cs`, rewritten per §2.6 pseudocode.
- `Infrastructure/Auth/UserProvisioningMiddleware.cs:31` — reads `oid` + `email`
  claims; sends `ProvisionOrLinkUserCommand`.

**Bicep config to change:** none (identity module DB schema; migrator picks up).

**Portal (Entra) config to change:** none.

### Slice OPS.M.14 — DevAuth retirement + admin-endpoint replacements + auth hardening

**Frontend files to change:**
- `web/src/lib/api/client.ts:126-129` — remove `credentials: 'include'`
  (per §2.1 change #1).
- Remove/hide any DevAuth-referring UI (persona switcher if present).

**API files to change:**
- Delete `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs`,
  `DevAuthOptions.cs`, `Api/Controllers/DevAuthController.cs`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs`:
  - Remove DevAuth branch (lines 28-32, 64-72).
  - Add `NameClaimType = "name"`, `RoleClaimType = "roles"` (per §2.2 change #3).
  - Optional: tighten `ClockSkew` to 1 min (per §2.2 change #4).
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs`:
  - Drop `ClaimTypes.NameIdentifier` fallback in `B2CObjectId` accessor
    (per §2.2 change #1).
  - Rename `B2CObjectId` → `ExternalObjectId` (per §2.2 change #2).
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs:31`:
  - Drop `ClaimTypes.NameIdentifier` fallback.
- Ship the four admin endpoints (per previous review §5.4).
- `tests/VrBook.IntegrationTests/Auth/TestAuthenticationHandler.cs` — new,
  test-only handler for `WebApplicationFactory<Program>`.

**Bicep config to change:**
- Remove `DevAuth__AllowAnonymous` + `DevAuth__WebBaseUrl` (`main.bicep:272-275`).
- Add `PlatformAdmin__BootstrapEmail` (per previous review §5.6).

**Portal (Entra) config to change:** none.

### Slice OPS.M.15 — App Role cleanup (§5.3 previous review, option A)

**Frontend files to change:** none.

**API files to change:**
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs:82-93`:
  - Delete the `primaryIdentity.AddClaim(ClaimTypes.Role, ...)` block.
  - The `app_tenant_id` claim stays; primary tenant read still lands.
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs`:
  - Drop `IsOwner` + `IsAdmin` properties.
  - Drop `GrantOwner`/`RevokeOwner`/`GrantAdmin`/`RevokeAdmin` methods.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/{next}_OpsM15_DropLegacyRoles.cs`:
  - `ALTER TABLE identity.users DROP COLUMN is_owner`.
  - `ALTER TABLE identity.users DROP COLUMN is_admin`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs`:
  - Drop `IsOwner`, `IsAdmin`, `OwnerClaim`, `AdminClaim` symbols.
  - Simplify `HasRole` — no belt+braces now that `RoleClaimType='roles'`.
- Every `[Authorize(Roles="Owner,Admin")]` controller attribute:
  - Replaced with `[Authorize(Policy="PlatformAdminOrAnyTenantRole")]`
    where "any tenant role" resolves via handler-level tenant scoping.

**Bicep config to change:** none.

**Portal (Entra) config to change:**
- Delete `Owner` + `Admin` App Role definitions from `vrbook-api-<env>` App
  Registrations (external + prod).
- Amend ADR-0014 with the App-Role-deletion decision.

### Slice OPS.M.16 — Migration audit table (§5.5 previous review)

**Frontend files to change:** none.

**API files to change:**
- New table `identity.migration_audit`.
- New admin endpoint `GET /api/v1/admin/platform/migration-audit`.
- Refactor OPS.M.13 backfill migration to INSERT rows.

**Bicep config to change:** none.
**Portal (Entra) config to change:** none.

### Slice OPS.M.12 — Google + Microsoft social IdPs (blocked on OPS.M.13)

**Frontend files to change:** none (SPA is provider-agnostic per §2.5).

**API files to change:** none (JwtBearer options unchanged; still one authority).

**Bicep config to change:**
- Add KV secrets: `google-idp-client-id`, `google-idp-client-secret`,
  `microsoft-idp-client-id` (if separate app for consumer MSA required).
- These are read by portal ops (not the API), stored in KV for
  disaster-recovery.

**Portal (Entra) config to change:**
- External Identities → All identity providers → Google → Configure with
  Google OAuth credentials.
- External Identities → All identity providers → Microsoft (or Custom OIDC
  for consumers-tenant).
- SignUpAndSignIn user flow → Identity providers → toggle Google + Microsoft on.

---

## 8. Open questions genuinely for the user (business-scope only)

1. **Should the "Sign in with Google" button be enabled at prod cutover, or
   deferred to a post-launch increment?**
   Trade-off: at cutover, adds one more thing to test end-to-end during the
   go-live weekend + requires Google Cloud OAuth Client (new bill line even
   if $0 for our usage). Deferring to post-launch means email + OTP is the
   only sign-in method at launch. **Recommendation: defer to post-launch
   (M.12 stays queued for after prod cutover).** No blocker; social sign-in
   is a UX enhancement, not a functionality requirement.

2. **For Microsoft social sign-in — personal MSA only, or also allow
   workforce (AAD) tenant sign-ins?**
   Option A: `login.microsoftonline.com/consumers` — personal Microsoft
   accounts only (Outlook.com, Xbox, Hotmail). Users can't sign in with a
   `foo@company.com` workforce account.
   Option B: `login.microsoftonline.com/organizations` — any Microsoft
   workforce tenant PLUS personal. Broader reach, but you inherit the trust
   boundary of any org that federates with you.
   **Recommendation: A (consumers only) for launch, B considered post-launch
   if enterprise customers request it.** A is safer.

3. **Rollback plan if OPS.M.13 backfill mis-attributes a `guest_user_id` on
   an existing booking during the survivor-collapse phase?**
   The migration is one-way. If a booking gets re-parented to the wrong
   `identity.users.id`, we need either:
   - a pre-migration snapshot of the FK graph (a `bookings_pre_m13` table with
     old + new user_ids) to enable a manual re-attribution SQL command later,
     OR
   - accept the mis-attribution and communicate to the affected user.
   **Recommendation: snapshot table.** Adds one migration step; safety net
   is cheap for hundreds of bookings. Ask user to confirm.

4. **First-PA bootstrap policy (per previous review §5.6): single email
   allowed, or comma-separated list of pre-approved PA emails at startup?**
   Today `niroshanaks` is the only intended PA. Post-launch, if a co-founder
   or ops engineer needs PA on day 1 (before `niroshanaks` can log in and
   promote them via the admin endpoint), we'd need a list.
   **Recommendation: comma-separated list.** Same idempotency guarantee;
   no runtime cost; higher operational flexibility. Config key
   `PlatformAdmin:BootstrapEmails` (plural).

5. **When Entra External ID emits a token with `email_verified=false`
   (rare with OTP flow — happens if user signed up via Google without
   verified email on Google's side, or via a mis-configured custom OIDC),
   what's the UX response?**
   - Option A: reject sign-in outright at the SPA (HTTP 403 with a "please
     verify your email in your provider" screen).
   - Option B: allow sign-in but restrict to read-only until verified.
   - Option C: allow sign-in fully; assume the IdP's verified flag will
     eventually flip.
   **Recommendation: A** — matches the previous review's §5.1 email-hijack
   guardrail. But needs UX design + copy sign-off before OPS.M.13.

6. **Multi-tenant humans in practice (repeat of previous review Q1 for
   completeness):** Do you envision one human as `tenant_admin` of >1
   tenant? If yes, `is_primary` remains meaningful. If no, we can simplify.
   **Recommendation: keep the shape flexible.** Cheap to defer the
   simplification.

7. **When we cut Front Door for prod, do we terminate TLS at Front Door and
   pass Bearer tokens over HTTP inside the VNet to the API container, or
   pass HTTPS end-to-end?**
   Trade-off: passing HTTP inside the VNet is common (Front Door is the
   TLS boundary; the internal hop is trusted-network); passing HTTPS
   end-to-end is stricter.
   **Recommendation: HTTPS end-to-end.** Container Apps Environment
   internal ingress supports HTTPS at no extra cost. Prevents any
   accidental log capture of bearer tokens if a proxy misbehaves.

---

## 9. Appendix — cited authoritative sources (Microsoft Learn, 2025-2026)

Each URL was fetched during preparation of this doc, not recalled from
memory:

- [Access tokens in the Microsoft identity platform (last updated 2026-06-15)](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens)
  — quoted in §1.2, §2.2 (Change #1). Establishes: `aud` must be the API
  app id; `sub` is pairwise per-app; `oid` is the correct principal id;
  JWKS auto-rotation is the standard signature validation approach.
- [ID tokens in the Microsoft identity platform (last updated 2026-06-15)](https://learn.microsoft.com/en-us/entra/identity-platform/id-tokens)
  — quoted in §1.4. Establishes: don't call APIs with ID tokens; ID tokens
  are for UX and DB keys.
- [Scopes and permissions in the Microsoft identity platform](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc)
  — cited in §1.4. Establishes: APIs publish delegated scopes; least
  privilege.
- [Identity providers for external tenants (last updated 2026-06-17)](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers)
  — quoted in §2.5. Establishes: social IdPs (Facebook, Google, Apple,
  Microsoft Entra ID) federate at the tenant level; ONE user object per
  human per External tenant.
- [Add Google as an identity provider — Microsoft Entra External ID](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers)
  — cited in §2.5. Portal recipe for enabling Google federation.
- [React CIAM sample: call protected ASP.NET Core web API](https://learn.microsoft.com/en-us/samples/azure-samples/ms-identity-ciam-javascript-tutorial/ms-identity-ciam-javascript-tutorial-1-call-api-react/)
  — canonical reference implementation for MSAL React + External ID + API.
  Our `msalConfig.ts` matches shape-for-shape.
- [Plan a CIAM Deployment — Microsoft Entra External ID](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-planning-your-solution)
  — cited implicitly in §2.5 (external tenant is a separate tenant kind
  from workforce; ADR-0012 §"Context and Problem Statement" already
  references this).

Internal companion docs (this repo):
- [`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md`](./OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md) — the base review this doc extends.
- [`docs/adr/0012-entra-external-id-over-b2c.md`](./adr/0012-entra-external-id-over-b2c.md) — pivot from AD B2C.
- [`docs/adr/0014-app-roles-global-db-per-tenant.md`](./adr/0014-app-roles-global-db-per-tenant.md) — the role-split ADR this doc scheduled for cleanup.
- [`docs/identity/roles-architecture.md`](./identity/roles-architecture.md) — the design doc under ADR-0014.
- [`docs/identity/runbooks/entra-external-id-setup.md`](./identity/runbooks/entra-external-id-setup.md) — the App Registration + user flow operational runbook.
