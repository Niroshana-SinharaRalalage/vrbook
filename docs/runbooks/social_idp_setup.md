# Social IdP Setup Runbook (OPS.M.12)

Configures the four consumer identity providers Entra External ID
federates against for the **guest** sign-in surface: Google, Microsoft
consumer, Facebook, and Apple. Also documents the two Entra user flows
(`AdminSignUpSignIn` guest-forbidden, `GuestSignUpSignIn` guest + all 4
socials) that back the SPA's per-flow authority.

> Owner policy locked 2026-07-05 (see
> [OPS_M_12_SOCIAL_IDPS_PLAN.md §11](../OPS_M_12_SOCIAL_IDPS_PLAN.md#11-locked-answers-2026-07-05)):
>
> - **Platform Admin + Tenant Admin** — Entra local ONLY (password OR
>   OTP). Never social.
> - **Guests** — Entra local OR any of the 4 socials.
>
> Enforcement is at TWO layers: `ProvisionOrLinkUserHandler` REFUSES at
> Layer 1 (§2.2 REFUSE-AT-PROVISIONING); `AdminSocialIdpRejectionMiddleware`
> blocks at Layer 2 (belt). Both must be in place.

---

## Table of contents

1. [Prerequisites](#1-prerequisites)
2. [Entra tenant baseline](#2-entra-tenant-baseline)
3. [Google Cloud Console setup](#3-google-cloud-console-setup)
4. [Microsoft consumer setup](#4-microsoft-consumer-setup)
5. [Meta for Developers (Facebook) setup](#5-meta-for-developers-facebook-setup)
6. [Apple Developer setup](#6-apple-developer-setup)
7. [Entra user flows — Admin vs Guest](#7-entra-user-flows-admin-vs-guest)
8. [Bicep + Key Vault audit table](#8-bicep-key-vault-audit-table)
9. [Smoke walk (15 min)](#9-smoke-walk-15-min)
10. [Break-glass recovery](#10-break-glass-recovery)

---

## 1. Prerequisites

- Owner access to the VrBook Entra External ID tenant
  (`vrbookexternal.onmicrosoft.com` staging /
  `vrbookexternalprod.onmicrosoft.com` prod). Confirm in Azure Portal →
  External Identities.
- Owner account on each provider portal (see per-provider sections).
- Access to the environment's Key Vault (`kv-vrbook-staging` /
  `kv-vrbook-prod`) with `Secrets Officer` role for writing new secret
  values.
- Reference: [ADR-0012 — Entra External ID over B2C](../adr/0012-entra-external-id-over-b2c.md),
  [ADR-0016 — Admin vs Social IdP surface split](../adr/0016-admin-vs-social-idp-surface-split.md).

## 2. Entra tenant baseline

Verified once per tenant. If any check fails, don't proceed to
per-provider setup.

- [ ] `vrbook-api-<env>` app registration exists with API scope
  `access_as_user` exposed.
- [ ] SPA reply URL matches the environment
  (`https://staging.vrbook.example/auth/callback` in staging).
- [ ] Federation endpoint format known:
  `https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/federation/oauth2`.
  Substitute for every provider's redirect / return URI.

Grab the subdomain + tenant id from Azure Portal → External Identities →
Overview.

## 3. Google Cloud Console setup

**Portal**: [console.cloud.google.com](https://console.cloud.google.com/apis/credentials).
Time cost: ~10 min once the GCP project exists.

1. Create (or reuse) a GCP project named `vrbook-<env>`. The project is
   an org-level container; it is NOT the OAuth client itself.
2. `APIs & Services → OAuth consent screen`:
   - User type: **External**.
   - App name: `VrBook (<env>)`.
   - Logo, homepage, privacy URL: use the marketing URLs from
     `docs/PUBLIC_URLS.md` (or leave homepage placeholder while pre-prod).
   - Scopes: `openid`, `email`, `profile`. Do not add sensitive scopes.
   - Test users: add the OwnerHome + one QA email while status is
     `Testing`. Publish to `In production` before staging cutover.
3. `Credentials → Create Credentials → OAuth client ID`:
   - Application type: **Web application**.
   - Authorized JavaScript origins: none (Entra handles the browser side).
   - Authorized redirect URIs: **exactly one** —
     `https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/federation/oauth2`.
     Copy-paste; a trailing slash is fatal.
4. Copy the generated **Client ID** + **Client secret**.
5. Write to Key Vault:
   - `google-oauth-client-id` → `<client-id>` (not secret; stored for
     symmetry with other IdPs).
   - `google-oauth-client-secret` → `<client-secret>`.
6. In Entra Portal → External Identities → All identity providers →
   `+ Google`:
   - Name: `Google`.
   - Client ID: from Key Vault above.
   - Client secret: from Key Vault above.
7. Attach the provider to the `GuestSignUpSignIn` user flow (§7 below).
   Do NOT attach to `AdminSignUpSignIn`.

## 4. Microsoft consumer setup

**Portal**: none required — Entra's built-in Microsoft IdP registers
against Microsoft's consumer account service (live.com, outlook.com,
hotmail.com) directly.

Time cost: ~2 min.

1. Entra Portal → External Identities → All identity providers →
   `+ Microsoft`.
2. Entra pre-fills the Microsoft-service endpoints. No client ID or
   secret is provisioned by us — the trust is between Entra and
   Microsoft.
3. Attach to `GuestSignUpSignIn` only.

The token's `idp` claim on a Microsoft consumer sign-in is `live.com`
(this is the tenant Microsoft uses for consumer accounts). VrBook's
`IdentityProviderClassifier` maps this to canonical `microsoft` before
persisting in `user_identities.provider`.

## 5. Meta for Developers (Facebook) setup

**Portal**: [developers.facebook.com](https://developers.facebook.com/apps).
Time cost: ~15 min. Meta requires an approved use case which adds review
delay — start days ahead of a launch.

1. Sign in to Meta for Developers. Use the same
   Owner email used for the other IdPs so ownership is discoverable.
2. `My Apps → Create App`:
   - Use case: **Authenticate and request data from users with Facebook
     Login**. (Do NOT pick the marketing or gaming use cases; they
     enable IG Basic Display and other surfaces we don't need.)
3. In the new app:
   - Add product: **Facebook Login for Business** (or Facebook Login).
   - Remove product: **Instagram Basic Display** if it was added.
     Delete unused products to shrink the Data-Use-Checkup surface.
4. Facebook Login → Settings:
   - Client OAuth Login: **On**.
   - Web OAuth Login: **On**.
   - Force Web OAuth Reauthentication: **Off**.
   - Valid OAuth Redirect URIs: **exactly one** — Entra's federation
     endpoint (§2). Meta rejects a trailing slash; copy-paste.
5. Settings → Basic → capture **App ID** + **App Secret**.
6. Data Use Checkup: complete now. Reviewer requires the sign-in flow to
   work end-to-end, so complete this AFTER a staging smoke walk verifies
   the redirect round-trips.
7. Write to Key Vault:
   - `facebook-app-id` → `<app-id>`.
   - `facebook-app-secret` → `<app-secret>`.
8. Entra Portal → External Identities → All identity providers →
   `+ Facebook`:
   - Client ID: App ID (Meta's naming).
   - Client secret: App Secret.
9. Attach to `GuestSignUpSignIn`.

## 6. Apple Developer setup

**Portal**: [developer.apple.com](https://developer.apple.com/account/).
Time cost: ~30 min plus **$99 USD/year Developer Program membership**.
Apple's docs are terse but the sequence is fixed — do NOT reorder.

1. Enroll in the Apple Developer Program if not already
   (`developer.apple.com/programs/enroll`). Individual enrollment is
   fine; org enrollment requires D-U-N-S which adds days.
2. Certificates, Identifiers & Profiles → Identifiers → `+`:
   - Type: **App IDs**. Description: `VrBook API <env>`. Bundle ID:
     `com.vrbook.api.<env>` (reverse-DNS from a domain we control).
     Capabilities → check **Sign in with Apple**. Save.
3. Identifiers → `+`:
   - Type: **Services IDs**. Description: `VrBook Web <env>`.
     Identifier: `com.vrbook.web.<env>`. Save.
4. Open the newly-created Services ID → check **Sign in with Apple** →
   Configure:
   - Primary App ID: the App ID from step 2.
   - Domains and Subdomains: `<tenant-subdomain>.ciamlogin.com`.
   - Return URLs: Entra's federation endpoint (§2).
   - Save.
5. Keys → `+`:
   - Key Name: `VrBook Sign-in with Apple <env>`.
   - Check **Sign in with Apple** → Configure → pick the App ID from
     step 2. Save.
   - **Download the .p8** immediately. Apple only lets you download it
     once. Note the **Key ID**.
6. Membership → capture **Team ID** (top right).
7. Entra requires a per-hour-rotated **client secret** — Apple's flow
   uses a JWT signed with the .p8, `alg=ES256`, `iss=<TeamID>`,
   `aud=https://appleid.apple.com`, `sub=<ServicesID>`, `iat=now`,
   `exp=now + 6 months` (max Apple allows). Use the
   `tools/apple-client-secret-jwt.ps1` helper (or any ES256-capable
   signer) to generate. Store the JWT itself in Key Vault; rotate
   before the 6-month expiry.
8. Write to Key Vault:
   - `apple-team-id` → `<team-id>` (audit trail).
   - `apple-key-id` → `<key-id>` (audit trail).
   - `apple-services-id` → `<services-id>` (audit trail).
   - `apple-private-key-p8` → contents of the .p8 file, base64.
   - `apple-client-secret-jwt` → the signed JWT (rotate every 6 months).
9. Entra Portal → External Identities → All identity providers →
   `+ Apple`:
   - Apple Developer ID: Services ID.
   - Apple Team ID: Team ID.
   - Apple Key ID: Key ID.
   - Client secret (JWT): the JWT from Key Vault.
10. Attach to `GuestSignUpSignIn`.

## 7. Entra user flows — Admin vs Guest

The **flow split** is the load-bearing invariant. Get this wrong and
admins can sign in with Google, which the middleware would then reject
at the API boundary — a bad UX + a leak of the internal admin surface
name to social IdPs.

### 7.1 `AdminSignUpSignIn`

- User flows → `+ New user flow` → **Sign up and sign in (v2)**.
- Name: `AdminSignUpSignIn`.
- Identity providers: **Email signup** only. Uncheck every social IdP.
- MFA / conditional access: leave default; owner policy allows password
  OR OTP so both are permitted (owner may enforce one via portal
  Conditional Access if desired).
- Application claims — **REQUIRED**: `email`, `email_verified`, `name`,
  `oid`, `idp`. The `idp` claim is what Layer 2 middleware inspects; if
  it's missing, admin sign-ins from a social IdP would silently pass
  (§7 risk 1 in the plan).
- Return claim `idp` for local sign-ups too — Entra emits `entra` (or
  `null`, which the classifier maps to `entra`).

### 7.2 `GuestSignUpSignIn`

- User flows → `+ New user flow` → **Sign up and sign in (v2)**.
- Name: `GuestSignUpSignIn`.
- Identity providers: Email signup + **Google + Microsoft + Facebook +
  Apple** (all 4 configured above).
- Application claims — **REQUIRED**: `email`, `email_verified`, `name`,
  `oid`, `idp`. Same as admin flow.

### 7.3 Assignment to the app registration

Both user flows must be assigned to the `vrbook-api-<env>` app
registration under `User flows → <flow> → Applications`. Without this
the SPA's authority URL returns a "user flow not found" error.

### 7.4 Verifying the `idp` claim shape

Sign in with each provider. The raw JWT at
`/api/v1/me/debug/claims` (dev only) MUST include a top-level `idp`
claim with one of the following values:

| Provider | Raw `idp` value | Classifier maps to |
|---|---|---|
| Entra local | `null` or the tenant issuer host | `entra` |
| Google | `google.com` | `google` |
| Microsoft consumer | `live.com` | `microsoft` |
| Facebook | `facebook.com` | `facebook` |
| Apple | `apple.com` | `apple` |

If a provider emits a raw `idp` value the classifier doesn't recognise,
the middleware treats it as a social IdP (fail-closed) — but the
provisioning handler rejects the sign-in at Layer 1 because the
`SocialProviderKeys` set doesn't contain it. Add it explicitly to
`HttpCurrentUser.cs` + widen the DB CHECK in a new migration.

## 8. Bicep + Key Vault audit table

`infra/main.bicep` sets up Key Vault access; secrets are populated
**out-of-band** through this runbook. Bicep does NOT create secret
values.

| Secret name | Purpose | Rotation cadence |
|---|---|---|
| `google-oauth-client-id` | Google OAuth Client ID | On Google rotation (rare) |
| `google-oauth-client-secret` | Google OAuth Client Secret | Every 90 days recommended by Google |
| `facebook-app-id` | Meta App ID | On Meta rotation (rare) |
| `facebook-app-secret` | Meta App Secret | Every 60 days (Meta forces) |
| `apple-team-id` | Apple Team ID (audit) | Never |
| `apple-key-id` | Apple Sign-in Key ID (audit) | On key regeneration |
| `apple-services-id` | Apple Services ID (audit) | Never |
| `apple-private-key-p8` | .p8 base64 (audit) | On key rotation |
| `apple-client-secret-jwt` | Signed JWT client secret | Every 6 months (Apple max exp) |

Microsoft consumer requires no secret — Entra handles trust natively.

Entra reads the client-id / client-secret pair from what you paste into
the portal at IdP registration time; Key Vault is our record of truth
for rotation. When rotating: update Entra portal first, then update
Key Vault to match. Never trust "Key Vault is current" as the source of
truth for what Entra will present to the provider.

## 9. Smoke walk (15 min)

Run per environment after any IdP change.

- [ ] `AdminSignUpSignIn` with Entra local password — expect API sign-in
  succeeds, `GET /api/v1/me` returns `identityProvider: "entra"`.
- [ ] `AdminSignUpSignIn` with Entra local OTP — expect same.
- [ ] `GuestSignUpSignIn` with Google — expect API sign-in succeeds,
  `GET /api/v1/me` returns `identityProvider: "google"`.
- [ ] `GuestSignUpSignIn` with Microsoft consumer — same, `microsoft`.
- [ ] `GuestSignUpSignIn` with Facebook — same, `facebook`.
- [ ] `GuestSignUpSignIn` with Apple — same, `apple`.
- [ ] **Adversarial**: seed an admin user with `is_platform_admin=true`.
  Sign in with Google using the same email. Expect
  `422 admin_social_signin_refused` at
  `POST /api/v1/auth/provision-or-link` (Layer 1) OR
  `403 admin_authority_requires_entra_local` at any subsequent request
  (Layer 2). Confirm no row is added to
  `identity.user_identities` with `provider='google'` for this user.
- [ ] **Adversarial**: `GET /api/v1/me` with a social-IdP admin token —
  the whitelist exempts this path, so it should return 200 with the
  admin's shape so the SPA can render the rejection error page.

If any check fails, capture the raw JWT (Chrome DevTools → Network →
`/token` response) + the `/api/v1/me` response and open a
`social-idp-setup` issue with both.

## 10. Break-glass recovery

**Symptom**: All guest sign-ins fail with `AADSTS500011 (federated
identity not found)` or similar.

- Cause: Entra provider registration was deleted, or the client
  secret in Entra was rotated without pushing to Key Vault.
- Recovery: Re-run §3–§6 for the affected provider. Entra keeps the
  IdP registration decoupled from the user flow — a missing provider
  causes the flow to omit that button but not fail closed.

**Symptom**: All Apple sign-ins fail after ~6 months.

- Cause: Apple client-secret JWT expired.
- Recovery: §6 step 7 — regenerate the JWT, push to
  `apple-client-secret-jwt`, re-paste into Entra Apple IdP config.

**Symptom**: An admin was accidentally created with a social identity
row.

- Cause: A regression in `ProvisionOrLinkUserHandler` (Layer 1
  bypassed).
- Recovery: `DELETE FROM identity.user_identities WHERE user_id=<...>
  AND provider IN ('google','microsoft','facebook','apple');` This
  leaves the entra identity untouched. Admin can continue via
  password/OTP. File an incident — Layer 1 defence was bypassed and
  needs a test to recreate the state.

---

Related:

- [OPS_M_12_SOCIAL_IDPS_PLAN.md](../OPS_M_12_SOCIAL_IDPS_PLAN.md) —
  design doc, §2.2 REFUSE-AT-PROVISIONING, §11 locked answers.
- [ADR-0016 — Admin vs Social IdP surface split](../adr/0016-admin-vs-social-idp-surface-split.md).
- [ADR-0012 — Entra External ID over B2C](../adr/0012-entra-external-id-over-b2c.md).
- [ADR-0014 — App Roles for global, DB for per-tenant](../adr/0014-app-roles-global-db-per-tenant.md).

---

## Appendix A — Post-M.12 walk playbook (added M.12.8)

Once §3–§7 are complete for an environment, run this scripted walk to
confirm the three layers hold. Owner drives it; findings feed the
close-out doc [`OPS_M_12_CLOSE_OUT.md`](../OPS_M_12_CLOSE_OUT.md) §6.

### A.1 Guest flow — happy path per provider

For each of Google, Microsoft consumer, Facebook, Apple:

1. Incognito window → `https://<web-fqdn>` → click Sign in.
2. Expect the Entra `GuestSignUpSignIn` hosted UI with all 4 buttons.
3. Complete provider sign-in with a **fresh email** the tenant has never
   seen.
4. Post-callback lands on `/` (guest → skip picker per §6.4 in the plan).
5. Verify `GET /api/v1/me` returns `identityProvider: "<canonical>"`.
6. Verify `identity.user_identities` shows one row for the user
   with `provider='<canonical>'` and no admin authority.

### A.2 Guest flow — link to an existing entra-only guest

Repeat A.1 but with an email that already has an `entra` identity from
a prior local sign-in and no admin authority (any tenant_admin
membership). Expect a successful link — post-condition
`user_identities` shows both `entra` + the social provider row.

### A.3 REFUSE-AT-PROVISIONING (Layer 1)

Seed an admin user (or promote a guest to `is_platform_admin=true`).
Sign in with that admin's email via Google:

- Expect 422 with `rule=admin_social_signin_refused` at
  `POST /api/v1/auth/provision-or-link` (whichever step the SPA calls
  during callback).
- Expect NO `provider='google'` row in `user_identities` for this user.
- SPA should either surface the 422 (guest flow catch) OR the
  `AdminAuthGuard` client-side detect fires (if the admin was previously
  linked but somehow held both identities).

### A.4 Middleware belt (Layer 2)

Manufacture a social-IdP admin token (typically only possible in a
staging test tenant where you can grant admin authority to a
Google-linked user). Hit `GET /api/v1/admin/bookings`:

- Expect 403 with `type=https://vrbook.example.com/problems/admin-social-idp-rejected`
  and `rule=admin_authority_requires_entra_local` +
  `identityProvider=<idp>` extension.
- Hit `GET /api/v1/me` on the same token — expect 200. Whitelist held.

### A.5 SPA flow-split routing

- Public homepage → header Sign in button → routes through
  `GuestSignUpSignIn` (Google button visible).
- Direct visit to `https://<web-fqdn>/admin/properties` → redirects to
  `/auth/signin?flow=admin&returnTo=/admin/properties`.
- The signin page kicks off `AdminSignUpSignIn` (Entra local only — no
  Google button visible).
- Complete with Entra local password → callback → tenant picker if
  memberships > 1, else `/admin/properties`.

### A.6 SPA rejection UX

Sign in via Google using an email that has admin authority
(post-manufactured — see A.4). Expect the SPA to redirect to
`/auth/admin-social-idp-rejected?provider=google.com` before ANY admin
API call fires. The page names Google and offers a "Sign out and try
again" CTA that triggers MSAL logout.

### A.7 Break-glass verification

- Delete an `entra-web-authority-admin` KV secret → cutover a revision
  → verify the SPA falls through to `login.microsoftonline.com/common`
  and Entra returns a "user flow not found" error. Restore the KV
  value and cutover again to confirm recovery.
- (Do NOT actually delete secrets in prod during the walk — this is a
  staging-only check.)
