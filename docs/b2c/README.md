# Azure AD B2C — Tenant Configuration

> Status: A0 documentation stub. Owner: A1 (Identity) — replaces with real exports after the dev tenant is created.

## Tenants

| Environment | Tenant | Notes |
|---|---|---|
| dev      | `vrbookb2c-dev.onmicrosoft.com`     | Shared across all developers. |
| staging  | `vrbookb2c-staging.onmicrosoft.com` | Mirrors prod policies; test users only. |
| prod     | `vrbookb2c.onmicrosoft.com`         | MFA enabled for Owner role. |

## Identity providers

- **Local** (email + password) — primary.
- **Google** — for guests; Apple deferred to mobile phase.

## User flows / custom policies

Phase 1 uses **user flows** (no custom policies) per proposal §14.1 + R3 in §21.

| Flow | Policy id | Purpose |
|---|---|---|
| SignUpSignIn_v1 | `B2C_1_SignUpSignIn_v1` | Combined; email verification mandatory. |
| PasswordReset_v1 | `B2C_1_PasswordReset_v1` | Self-service password reset. |
| ProfileEdit_v1 | `B2C_1_ProfileEdit_v1` | Minimal — most profile lives in our DB. |

When A1 exports the flow definitions, commit them as JSON in `flows/`:
- `flows/SignUpSignIn_v1.json`
- `flows/PasswordReset_v1.json`
- `flows/ProfileEdit_v1.json`

## Claims emitted

Per proposal §14.1, every access token carries:

- `oid` — B2C object id (stable across name changes).
- `email` — verified primary email.
- `email_verified` — boolean.
- `name` — display name.
- `extension_isOwner` — managed via Microsoft Graph from the admin screen.
- `extension_isAdmin` — same.

The API maps these to a domain `User` row on first request (proposal §4.3 — Identity context).

## Tokens

- Access token TTL: 60 min.
- Refresh token TTL: 90 days (rolling).
- Frontend stores refresh token in HttpOnly Secure SameSite=Lax cookie.

## App registrations

| App | Purpose | Redirect URIs |
|---|---|---|
| `vrbook-api`       | Resource for the API.                | n/a (it's a resource, not a client) |
| `vrbook-web`       | SPA client for Next.js frontend.     | `http://localhost:3000/auth/callback`, `https://www.vrbook.example.com/auth/callback` |
| `vrbook-admin-mgmt` | Daemon for Graph extension updates. | client credentials flow |

## Extension attributes

- `extension_<app-id>_isOwner` (boolean)
- `extension_<app-id>_isAdmin` (boolean)

Managed via Microsoft Graph from an admin screen owned by A1. A0 ships the placeholder
endpoint; the wiring lands when A1's identity module ships.
