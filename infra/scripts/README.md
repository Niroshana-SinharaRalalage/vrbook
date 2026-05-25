# Bootstrap scripts — `infra/scripts/`

> One-time provisioning + secret-seeding for each VrBook environment. Idempotent —
> every script is safe to re-run. State persists to `infra/.state/<env>.json` (gitignored).

## Prerequisites

- Azure CLI 2.65+ (`az --version`)
- PowerShell 7+ (`pwsh --version`) — Windows PowerShell 5.1 also works but is noisier
- A logged-in `az` session (`az login`) with **Owner** on the target subscription
- GitHub CLI for the federated-credential outputs to land where you can use them

The defaults in [`_common.ps1`](./_common.ps1) point at your tenant
`369a3c47-33b7-4baa-98b8-6ddf16a51a31` and subscription `ebb8304a-…b5b5`. Override with
`-TenantId` / `-SubscriptionId` if you need to target a different account.

## The runbook — order matters

| # | Script | What it does | When to run |
|---|---|---|---|
| 0 | [`00-foundation.ps1`](./00-foundation.ps1) | RG + User-Assigned Managed Identity + Key Vault + RBAC | Once per env, first |
| 1 | [`10-store-secrets.ps1`](./10-store-secrets.ps1) | Generate random secrets + prompt for Stripe / SendGrid / etc., store in KV | After foundation |
| 2 | [Portal-only step 1](../../docs/b2c/setup.md#1-create-the-b2c-tenant) | Create the B2C tenant in Azure portal (~5 min) | Before script #3 |
| 3 | [`20-b2c-apps.ps1`](./20-b2c-apps.ps1) | Register vrbook-api + vrbook-web apps; extension attrs; admin-consent permissions; write KV secrets | After tenant exists |
| 4 | [Portal-only step 2](../../docs/b2c/setup.md#5-create-the-three-user-flows) | Create `B2C_1_SignUpSignIn_v1`, `B2C_1_PasswordReset_v1`, `B2C_1_ProfileEdit_v1` user flows (~10 min of clicking) | After script #3 |
| 5 | [`30-github-oidc.ps1`](./30-github-oidc.ps1) | Federated credential on the UAMI + Contributor RBAC, outputs the GitHub Secrets values | After foundation; can run before B2C |
| 6 | [`grant-self-admin.ps1`](./grant-self-admin.ps1) | Stamp your own B2C user with isOwner + isAdmin extension claims | After you sign up via SignUpSignIn_v1 |

## Quick start — staging

```powershell
cd C:\Work\BookingApp\infra\scripts

# 1. Foundation
.\00-foundation.ps1 -Env staging

# 2. Seed secrets (interactive — paste Stripe + SendGrid keys when prompted, or Enter to skip)
.\10-store-secrets.ps1 -Env staging

# 3. Go to https://portal.azure.com → "Azure AD B2C" → + Create →
#    Initial domain: vrbookb2cdev  →  Tenant name: VrBook B2C (dev)
#    (Microsoft has no CLI for this, sorry.)

# 4. Register the apps in the freshly-created tenant
.\20-b2c-apps.ps1 -Env staging -B2CDomain vrbookb2cdev.onmicrosoft.com

# 5. Go to the B2C tenant portal → User flows → + New user flow, three times.
#    See docs/b2c/setup.md §5 for screenshots and exact options.

# 6. Federate the GitHub Actions OIDC
.\30-github-oidc.ps1 -Env staging
# → copy the three values it prints into https://github.com/Niroshana-SinharaRalalage/vrbook/settings/secrets/actions

# 7. After signing yourself up via the SignUpSignIn_v1 user flow:
.\grant-self-admin.ps1 -Env staging -UserEmail you@example.com
```

Then push to `develop` and `cd-staging.yml` will deploy.

## State file

Each script writes to `infra/.state/<env>.json`. Keys persisted:

| Key | Source | Used by |
|---|---|---|
| `tenantId`, `subscriptionId`, `location` | `00-foundation.ps1` | every script |
| `resourceGroup`, `keyVaultName`, `keyVaultUri` | `00-foundation.ps1` | secrets + b2c + bicep |
| `uamiName`, `uamiClientId`, `uamiPrincipalId`, `uamiResourceId` | `00-foundation.ps1` | github-oidc + bicep |
| `b2cDomain`, `b2cTenantId`, `b2cApiAppId`, `b2cWebAppId`, `b2cInstance`, `b2cSignUpSignInPolicyId` | `20-b2c-apps.ps1` | grant-self-admin + bicep |

The state file is **gitignored** — it holds non-secret IDs but not secrets, yet still
shouldn't leak which tenant/subscription you're targeting outside the team.

## What the scripts DON'T do

- **Create the B2C tenant itself** — Microsoft hasn't shipped that CLI command. Portal only.
- **Create B2C user flows** — same. Portal only.
- **Deploy any application infrastructure** — that's Bicep's job. These scripts only
  create the prerequisites (KV, UAMI, GitHub OIDC). Bicep `main.bicep` creates Postgres,
  Redis, Container Apps, etc., from the values in `infra/.state/<env>.json` + KV secrets.
- **Issue Stripe / SendGrid API keys** — you create those at the provider; the script
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
