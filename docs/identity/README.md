# Identity — Microsoft Entra External ID

> **Provider:** Microsoft Entra External ID (External tenant configuration).
> See [**ADR-0012**](../adr/0012-entra-external-id-over-b2c.md) for why we switched from Azure AD B2C.
> Setup runbook: [`setup.md`](./setup.md).

## Quick links

- [**setup.md**](./setup.md) — end-to-end provisioning runbook
- [API auth wiring](../../src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs)
- [Dev-only synthetic principal](../../src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs) (`DevAuth:AllowAnonymous=true`)
- [Proposal §14 — Identity & Security (now superseded by ADR-0012)](../../BookingApp_Proposal.md)

## Tenants

| Environment | Tenant | Notes |
|---|---|---|
| dev / staging | `vrbookcid.onmicrosoft.com` *(or whatever was provisioned)* | Provisioning runbook step §1 |
| prod | `vrbookcid-prod.onmicrosoft.com` *(reserved; created at prod cutover)* | Separate tenant — MFA required for Owners |

External tenants are independent from the work AAD (`niroshanaksgmail.onmicrosoft.com`).
Sign in to an External tenant with `az login --tenant <tenant>.onmicrosoft.com --allow-no-subscriptions`.

## Identity providers

- **Local** (email + password or email + one-time-passcode) — primary.
- **Google** — for guests; Apple deferred to Phase 2 (mobile).
- **Microsoft work/school accounts** — via Entra ID federation, available for Owner accounts.

## User flow

Phase 1 uses a single **sign-up and sign-in** user flow per tenant.
*(Entra External ID does not have the `B2C_1_` policy prefix that B2C used.)*

Attributes collected at sign-up: Display Name, Email.
Claims emitted in tokens:

- `oid` — user object id (stable across name changes)
- `email`, `email_verified`
- `name`
- `extension_<api-app-id-no-dashes>_isOwner` — boolean, managed via Microsoft Graph
- `extension_<api-app-id-no-dashes>_isAdmin` — boolean, same

The API's [`HttpCurrentUser`](../../src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs) reads these claim names and stamps the app-side user id onto `HttpContext.Items`.

## Tokens

- Access token TTL: 60 min (Microsoft default for External tenants)
- Refresh token TTL: 90 days (rolling)
- Frontend stores refresh token in HttpOnly Secure SameSite=Lax cookie

## App registrations

| App | Purpose | Redirect URIs |
|---|---|---|
| `vrbook-api` | Resource (audience for access tokens). Exposes scope `access_as_user`. | n/a (it's a resource) |
| `vrbook-web` | SPA client for Next.js frontend. | `http://localhost:3000/auth/callback`, `https://www.vrbook.example.com/auth/callback` |

Both apps live in the External tenant. The `vrbook-api` app exposes the
`access_as_user` scope; `vrbook-web` requests it and admin-consent is granted
once (CIAM tenants don't show consent UI to end users).

## Extension attributes

Per-app extension properties created via Microsoft Graph and surfaced as
`extension_<api-app-id-without-dashes>_isOwner` / `_isAdmin` claims in the token.
The Identity module reads these in `HttpCurrentUser.IsOwner` / `IsAdmin`.

## Bootstrapping yourself as Owner+Admin

After signing up via the sign-in user flow once, run:

```powershell
.\infra\scripts\grant-self-admin.ps1 -Env staging -UserEmail you@example.com
```

It uses Microsoft Graph to set the two extension attributes to `true` on your
user object. Sign out and back in to pick up the new claims.
