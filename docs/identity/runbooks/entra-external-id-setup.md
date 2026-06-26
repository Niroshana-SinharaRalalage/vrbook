# Runbook: Provision a fresh Entra External ID environment

> **Status**: Authoritative for VrBook (replaces the role-attribute parts of [`setup.md`](../setup.md)).
> **Owner**: Identity (A1).
> **Last updated**: 2026-06-26 — App Roles path adopted, extension-attribute path retired (see [`roles-architecture.md`](../roles-architecture.md)).
> **Use this when**: provisioning a new environment's Entra tenant (today: prod; future: any new env). For the existing staging tenant, use [`entra-key-rotation.md`](./entra-key-rotation.md) for incremental changes.

This runbook is the **end-to-end** path. Skip nothing the first time.

---

## 0. Prerequisites

- Azure CLI 2.65+ (`az --version`).
- Signed in to the workforce Azure AD that owns the target subscription: `az login`.
- Repo checkout with the latest `develop`. The cutover script lives at [`infra/scripts/12-entra-cutover.ps1`](../../../infra/scripts/12-entra-cutover.ps1).
- Owner/Tenant Creator role at the subscription scope.
- ~50 min wall-clock (most of it Microsoft's tenant-provisioning wait).

---

## 1. Provision the External tenant (portal-only, ~30 min)

> Microsoft has not shipped a CLI command for External tenant creation. Use the Entra admin center, not the Azure portal — they are different surfaces.

1. Open https://entra.microsoft.com (signed in as the same account that pays for the parent subscription).
2. Left nav → **Entra ID** → **Overview** → **Manage tenants** tab.
3. **+ Create** → **External** → **Continue**.
4. **Basics**:
   - **Tenant Name**: `VrBook External Prod` (or `VrBook External <Env>`).
   - **Domain Name**: `vrbook` *(yields `vrbook.onmicrosoft.com` + `vrbook.ciamlogin.com`)*. Must be globally unique within `*.ciamlogin.com`. If already taken in your target environment, pick a variant like `vrbookprod`.
   - **Location**: United States. **Can't change later.**
5. **Add a subscription**:
   - Pick the same Azure subscription you're deploying VrBook to.
   - **Resource group**: `rg-vrbook-<env>` (or create new).
6. **Review + create** → **Create**.
7. Watch the bell-icon notifications. Up to ~30 minutes. When done, the tenant appears under **Manage tenants** with type **External**.
8. Switch to the new tenant: top-right Settings icon → **Directories + subscriptions** → **Switch** to the new External tenant.

**Output to capture**: the new tenant's `.onmicrosoft.com` domain (will become `-ExternalTenantDomain` parameter for the cutover script).

---

## 2. Run the cutover script (CLI, ~30 sec to start)

The script registers the two app registrations (`vrbook-api-<env>`, `vrbook-web-<env>`), pauses for the portal-only user-flow creation, then writes secrets to Key Vault and updates `infra/.state/<env>.json`.

```powershell
cd c:\Work\BookingApp

.\infra\scripts\12-entra-cutover.ps1 `
    -Env <env> `
    -ExternalTenantDomain vrbook.onmicrosoft.com
```

The script:
- Switches CLI context to the External tenant.
- Creates `vrbook-api-<env>` with identifier URI `api://vrbook` and exposed scope `access_as_user`.
- Creates `vrbook-web-<env>` as a public-client SPA with redirect URIs (`http://localhost:3000/auth/callback` + the deployed web container).
- Grants `vrbook-web-<env>` admin-consented `access_as_user` on `vrbook-api-<env>`.
- **Pauses** — prints exact portal recipe for the next step.

**Output to capture**: the script prints `vrbook-api-<env>` and `vrbook-web-<env>` app IDs.

---

## 3. Create the SignUpAndSignIn user flow (portal-only, ~7 min)

Portal-only because the Graph beta API for user flows in CIAM has an undocumented schema.

1. Still on https://entra.microsoft.com in the External tenant.
2. Left nav → **External Identities** → **User flows** → **+ New user flow**.
3. **Name**: `SignUpAndSignIn` (or `SignUpAndSignIn_v1` if recreating — staging used `_v1`).
4. **Identity providers**: ✅ **Email signup**. Pick *one-time passcode* or *password*.
5. **User attributes** (collected at sign-up form — KEEP MINIMAL):
   - ✅ Display Name (Required)
   - ✅ Email Address (Required)
   - Do NOT add extension attributes here. Roles come via App Roles (§5 below), not via the user flow.
6. **MFA**: Off in dev/staging. **Required for users with role `Admin`** in prod.
7. **Create**.

---

## 4. Associate the user flow with the web app (portal-only, ~1 min)

1. Open the `SignUpAndSignIn` user flow you just created.
2. Left nav → **Applications** → **+ Add application**.
3. Pick **`vrbook-web-<env>`** (the script printed its appId).

---

## 5. Resume the cutover script

Back in the PowerShell window that paused at the portal step. **Press Enter**.

The script:
- Switches CLI back to the workforce tenant.
- Writes 5 KV secrets: `entra-instance`, `entra-tenant-id`, `entra-api-client-id`, `entra-web-authority`, `entra-web-client-id`.
- Updates `infra/.state/<env>.json` with the Entra fields.
- Restarts the API Container App (`ca-vrbook-api-<env>`) so the new KV values are read.

Verify by signing into the API container's revision list — there should be a fresh revision marked Active.

---

## 6. Move web app's redirect URIs from `web` to `spa` platform

**This is required** — MSAL.js uses PKCE and the app registration must declare the URIs on the SPA platform, not the Web platform. The cutover script previously used `--web-redirect-uris` (incorrect for SPAs). If you ran the script before 2026-06-26 commit `989104d`, this was wrong. Confirm + fix:

```powershell
az login --tenant vrbook.onmicrosoft.com --allow-no-subscriptions | Out-Null

$webAppId = '<the script's vrbook-web appId output>'
$webObjectId = az ad app show --id $webAppId --query id -o tsv

# Confirm current state - if "web" has URIs and "spa" is empty, you need this fix
az ad app show --id $webAppId --query "{web:web.redirectUris, spa:spa.redirectUris}" -o json

# Patch via Graph (PowerShell quoting needs the temp-file trick)
$body = @{
    spa = @{ redirectUris = @('http://localhost:3000/auth/callback', '<staging container /auth/callback>') }
    web = @{ redirectUris = @() }
} | ConvertTo-Json -Depth 5
$bodyFile = New-TemporaryFile
$body | Set-Content -Path $bodyFile.FullName -Encoding utf8 -NoNewline
az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$webObjectId" `
    --headers 'Content-Type=application/json' `
    --body "@$($bodyFile.FullName)"
Remove-Item $bodyFile.FullName
```

Symptom if you skip this: `AADSTS9002326: Cross-origin token redemption is permitted only for the 'Single-Page Application' client-type` → 400 at `/oauth2/v2.0/token`.

---

## 7. Define App Roles + assign the first admin

Roles for VrBook flow as **Entra App Roles** on the API app — not as extension attributes. The access token carries `roles: ["Owner", "Admin"]` natively; JwtBearer maps to ASP.NET `ClaimTypes.Role` automatically; `[Authorize(Roles="Owner,Admin")]` just works. See [`roles-architecture.md`](../roles-architecture.md).

### 7.1 Define the two App Roles (portal)

1. Entra admin center (External tenant) → **App registrations** → **All applications** → **`vrbook-api-<env>`**.
2. Left nav → **App roles** → **+ Create app role**.
3. Add **`Owner`**:
   - Display name: `Owner`
   - Allowed members: ✅ Users/Groups
   - **Value**: `Owner` (**case-sensitive** — must match `[Authorize(Roles="Owner,Admin")]` in code)
   - Description: `Property owner; can manage their listings + bookings.`
   - Enable: ✅
4. **+ Create app role** again, add **`Admin`** with value `Admin`.

### 7.2 Create the service principal + assign the first admin (CLI)

For API apps in CIAM, the Enterprise Application (service principal) entry is **not** auto-created. Without it, the portal's "Users and groups" path is unavailable. The one-shot block:

```powershell
az login --tenant vrbook.onmicrosoft.com --allow-no-subscriptions | Out-Null

$apiAppId  = '<vrbook-api-$env appId from the script>'
$userOid   = '<your Entra oid - sign up via the user flow first, then read it from your token at jwt.ms>'

# Get-or-create the API service principal
$apiSpId = az ad sp list --filter "appId eq '$apiAppId'" --query "[0].id" -o tsv
if (-not $apiSpId) { $apiSpId = az ad sp create --id $apiAppId --query id -o tsv }

# Look up the role IDs
$ownerRoleId = az ad app show --id $apiAppId --query "appRoles[?value=='Owner'].id | [0]" -o tsv
$adminRoleId = az ad app show --id $apiAppId --query "appRoles[?value=='Admin'].id | [0]" -o tsv

# Assign both roles - one Graph call each
foreach ($role in @(@{name='Owner';id=$ownerRoleId}, @{name='Admin';id=$adminRoleId})) {
    $body = @{ principalId = $userOid; resourceId = $apiSpId; appRoleId = $role.id } | ConvertTo-Json
    $bodyFile = New-TemporaryFile
    $body | Set-Content -Path $bodyFile.FullName -Encoding utf8 -NoNewline
    az rest --method POST `
        --uri "https://graph.microsoft.com/v1.0/users/$userOid/appRoleAssignments" `
        --headers 'Content-Type=application/json' `
        --body "@$($bodyFile.FullName)"
    Remove-Item $bodyFile.FullName
}
```

**Sign out + sign back in** in the web app so the new token carries `roles: ["Owner", "Admin"]`.

---

## 8. Verify end-to-end

1. Fresh incognito → staging URL → click **Sign in**.
2. After sign-in, DevTools Console:

   ```javascript
   const akey = Object.keys(sessionStorage).find(k => k.toLowerCase().includes('-accesstoken-'));
   const claims = JSON.parse(atob(JSON.parse(sessionStorage.getItem(akey)).secret.split('.')[1]));
   console.log({aud: claims.aud, scp: claims.scp, roles: claims.roles, oid: claims.oid});
   ```

   Expect: `aud = <vrbook-api appId>`, `scp = access_as_user`, `roles = ["Owner","Admin"]`, `oid = <your oid>`.

3. Visit `/admin` — should load with real data.

If verification passes, **turn off DevAuth** for that environment so the Entra path is provably the only auth path:

```powershell
az containerapp update -n ca-vrbook-api-<env> -g rg-vrbook-<env> `
    --set-env-vars 'DevAuth__AllowAnonymous=false'
```

(Already true for staging from commit `be897bc`. Bicep change in `infra/main.bicep` persists this across deploys.)

---

## 9. Troubleshooting

For symptoms not listed here, see [`entra-incident-triage.md`](./entra-incident-triage.md) — the full diagnostic playbook drawn from real production-style failures.

| Symptom | Likely cause | Quick fix |
|---|---|---|
| `AADSTS700038: 00000000-... is not a valid application identifier` at sign-in | Web bundle has fallback constants — build args didn't reach `next build` | Verify `web/Dockerfile` declares `ARG NEXT_PUBLIC_ENTRA_AUTHORITY` + `ARG NEXT_PUBLIC_ENTRA_CLIENT_ID`. Then trigger a fresh `cd-staging-web` run with `workflow_dispatch`. |
| `AADSTS9002326: Cross-origin token redemption` 400 at `/token` | Redirect URIs on `web` platform, not `spa` | §6 above. |
| Access token has `aud = web app id` instead of API app id | MSAL scope is `${clientId}/.default` | `web/src/lib/auth/msalConfig.ts` `apiScopes` should be `['api://vrbook/access_as_user']`. Regression test at `msalConfig.test.ts`. |
| Access token `roles` claim missing | App Roles not assigned to the user | §7.2 — confirm `appRoleAssignments` GET returns both roles. |
| `/admin` loads but tokens are empty | DevAuth fallback is active | Set `DevAuth__AllowAnonymous=false` (Bicep handles this for non-dev envs from commit `be897bc`). |

---

## Related

- [`roles-architecture.md`](../roles-architecture.md) — why App Roles, not extension attributes.
- [`entra-key-rotation.md`](./entra-key-rotation.md) — ongoing credential & membership management.
- [`entra-incident-triage.md`](./entra-incident-triage.md) — diagnostic playbook.
- [`../setup.md`](../setup.md) — older reference; superseded for §8 (bootstrap) by this runbook §7.
- [ADR-0012](../../adr/0012-entra-external-id-over-b2c.md) — provider choice.
