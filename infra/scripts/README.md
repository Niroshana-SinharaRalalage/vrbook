# Bootstrap scripts — `infra/scripts/`

> One-time provisioning + secret-seeding for each VrBook environment. Idempotent —
> every script is safe to re-run. State persists to `infra/.state/<env>.json` (gitignored).

## Prerequisites

- Azure CLI 2.65+ (`az --version`)
- PowerShell 7+ (`pwsh --version`) — Windows PowerShell 5.1 also works but is noisier
- A logged-in `az` session (`az login`) with **Owner** on the target subscription

The defaults in [`_common.ps1`](./_common.ps1) point at your tenant
`369a3c47-33b7-4baa-98b8-6ddf16a51a31` and subscription `ebb8304a-…b5b5`. Override with
`-TenantId` / `-SubscriptionId` if you need to target a different account.

## The runbook — order matters

| # | Script | What it does | When to run |
|---|---|---|---|
| 0 | [`00-foundation.ps1`](./00-foundation.ps1) | RG + User-Assigned Managed Identity + Key Vault + RBAC | Once per env, first |
| 1 | [`10-store-secrets.ps1`](./10-store-secrets.ps1) | Generate random secrets + prompt for Stripe / ACS / etc., store in KV | After foundation |
| 2 | [Portal-only — create Entra External tenant](../../docs/identity/setup.md#1-create-the-external-tenant-portal-only) | Create the Entra External ID tenant in entra.microsoft.com (~30 min, Microsoft-side) | Before app registrations |
| 3 | [Manual app registration](../../docs/identity/setup.md#3-register-vrbook-api-the-resource) | Register `vrbook-api` + `vrbook-web` apps + extension attrs in the External tenant. Done from your terminal — interactive `az login --tenant ...` then 4 commands. See setup.md §3-4. | After tenant exists |
| 4 | [Portal-only — user flow](../../docs/identity/setup.md#5-create-the-sign-in-user-flow-portal) | Create the `SignUpAndSignIn` user flow + associate with `vrbook-web` (~5 min) | After app registrations |
| 5 | [`30-github-oidc.ps1`](./30-github-oidc.ps1) | Federated credential on the UAMI + Contributor RBAC, outputs the GitHub Secrets values | After foundation; can run before/parallel to identity setup |
| 6 | [`grant-self-admin.ps1`](./grant-self-admin.ps1) | Stamp your own Entra user with isOwner + isAdmin extension claims | After you sign up via the user flow |

> **Note**: a previous version of this runbook had a `20-b2c-apps.ps1` script that
> registered apps in an AD B2C tenant. That step is replaced by the manual
> `az ad app create` commands in [docs/identity/setup.md §3-4](../../docs/identity/setup.md) — see [ADR-0012](../../docs/adr/0012-entra-external-id-over-b2c.md) for the pivot rationale.

## Quick start — staging

```powershell
cd C:\Work\BookingApp\infra\scripts

# 1. Foundation
.\00-foundation.ps1 -Env staging

# 2. Seed secrets (interactive — paste Stripe + ACS keys when prompted, or Enter to skip)
.\10-store-secrets.ps1 -Env staging

# 3. Open https://entra.microsoft.com → Entra ID → Manage tenants → + Create → External.
#    See docs/identity/setup.md §1 for exact field values.
#    Wait ~30 min for provisioning.

# 4. Register vrbook-api + vrbook-web in the new External tenant.
#    See docs/identity/setup.md §3-4 for the az commands (interactive, you run them).

# 5. Create the user flow in the External tenant portal.
#    See docs/identity/setup.md §5.

# 6. Federate the GitHub Actions OIDC (can run anytime after foundation).
.\30-github-oidc.ps1 -Env staging
# → copy the printed values into https://github.com/Niroshana-SinharaRalalage/vrbook/settings/secrets/actions

# 7. After signing yourself up via the user flow:
.\grant-self-admin.ps1 -Env staging -UserEmail you@example.com
```

Then push to `develop` and `cd-staging.yml` will deploy.

## State file

Each script writes to `infra/.state/<env>.json`. Keys persisted:

| Key | Source | Used by |
|---|---|---|
| `tenantId`, `subscriptionId`, `location` | `00-foundation.ps1` | every script |
| `resourceGroup`, `keyVaultName`, `keyVaultUri` | `00-foundation.ps1` | secrets + bicep |
| `uamiName`, `uamiClientId`, `uamiPrincipalId`, `uamiResourceId` | `00-foundation.ps1` | github-oidc + bicep |
| `entraTenantId`, `entraTenantDomain`, `entraInstance`, `entraApiAppId`, `entraWebAppId` | docs/identity/setup.md §7 (manual) | grant-self-admin + bicep |

The state file is **gitignored** — it holds non-secret IDs but not secrets, yet still
shouldn't leak which tenant/subscription you're targeting outside the team.

## What the scripts DON'T do

- **Create the Entra External tenant itself** — Microsoft hasn't shipped that CLI command. Portal only.
- **Create the user flow** — same. Portal only.
- **Deploy any application infrastructure** — that's Bicep's job. These scripts only
  create the prerequisites (KV, UAMI, GitHub OIDC). Bicep `main.bicep` creates Postgres,
  Redis, Container Apps, etc., from the values in `infra/.state/<env>.json` + KV secrets.
- **Issue Stripe / ACS keys** — you create those at the provider; the script
  just stores the value you paste.

## Rotating a secret

```powershell
.\10-store-secrets.ps1 -Env staging
# It detects the existing value, asks you for the new one, and writes a new KV version.
# Container Apps pull `secretref:` values on each replica restart, so deploy a new
# revision (or `az containerapp revision restart`) to pick up the change.
```

## Tearing down (use with care)

```powershell
# Soft-deletes everything in the RG, but the KV itself stays in soft-delete for 90 days
# per the proposal's purge-protection requirement.
az group delete -n rg-vrbook-staging --yes --no-wait
```
