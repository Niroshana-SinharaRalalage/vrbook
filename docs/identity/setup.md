# Microsoft Entra External ID — Step-by-Step Setup

> Status: Authoritative for VrBook. Replaces the original B2C runbook per [ADR-0012](../adr/0012-entra-external-id-over-b2c.md).
> Owner: A1 (Identity). Last updated: 2026-05-26.

Most steps are `az` CLI; portal-only steps are flagged. Total time: ~50 minutes per environment (tenant provisioning alone is ~30 min Microsoft-side).

---

## 0. Prereqs

```powershell
# Azure CLI 2.65+
az --version

# Sign in to your work/personal Azure AD (the directory that pays for the External tenant).
az login

# Confirm the right subscription is active.
az account show --query "{subscription:name, id:id, tenant:tenantId}"
```

You need **Tenant Creator** at the subscription scope (Owner has this implicitly).

---

## 1. Create the External tenant (portal-only)

> ⚠️ Microsoft has not shipped a CLI command for External tenant creation. Use the
> Entra admin center, not the Azure portal — they're different surfaces.

1. Open [https://entra.microsoft.com](https://entra.microsoft.com) (signed in as the same account)
2. Left nav: **Entra ID** → **Overview** → **Manage tenants** tab
3. Click **+ Create**
4. Select **External** → **Continue**
5. **Basics tab:**
   - **Tenant Name:** `VrBook External` *(or `VrBook External Staging`)*
   - **Domain Name:** `vrbookcid` *(becomes `vrbookcid.onmicrosoft.com` + `vrbookcid.ciamlogin.com`)*
   - **Location:** United States *(can't change later)*
6. **Add a subscription tab:**
   - **Subscription:** Azure subscription 1
   - **Resource group:** `rg-vrbook-staging`
7. **Review + create** → **Create**

⏱ **Up to 30 minutes.** Monitor in the bell-icon Notifications. When done the
tenant appears in **Manage tenants** with type "External".

After creation, switch to it: top-right **Settings** icon → **Directories + subscriptions**
→ **Switch** to the new External tenant.

---

## 2. Sign into the External tenant from CLI

```powershell
$externalTenant = "vrbookcid.onmicrosoft.com"     # the tenant you just created
az login --tenant $externalTenant --allow-no-subscriptions
```

`--allow-no-subscriptions` is required — External tenants don't have subscriptions
attached directly (the billing parent is the workforce AAD's subscription).

Verify you're in the right tenant:

```powershell
az account show --query "{tenant:tenantId, user:user.name}"
```

The `tenant` value should be a GUID different from your workforce tenant. Save
it — we'll use it in §7.

---

## 3. Register `vrbook-api` (the resource)

```powershell
# 1. Create the API app registration. Single-tenant — only this External tenant.
$apiApp = az ad app create `
    --display-name "vrbook-api" `
    --sign-in-audience AzureADMyOrg `
    --identifier-uris "api://vrbook" `
    -o json | ConvertFrom-Json

$apiAppId = $apiApp.appId
$apiAppObjectId = $apiApp.id
"vrbook-api  appId = $apiAppId"

# 2. Expose 'access_as_user' scope.
$scopeId = [guid]::NewGuid().ToString()
$exposeBody = @"
{
  "api": {
    "oauth2PermissionScopes": [{
      "id": "$scopeId",
      "adminConsentDescription": "Allow the app to call the VrBook API on the user's behalf",
      "adminConsentDisplayName": "Access VrBook API",
      "userConsentDescription": "Allow this app to call VrBook on your behalf",
      "userConsentDisplayName": "Access VrBook on your behalf",
      "value": "access_as_user",
      "type": "User",
      "isEnabled": true
    }]
  }
}
"@
az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId" `
    --body $exposeBody --headers 'Content-Type=application/json'

# 3. Add extension attributes for isOwner / isAdmin.
foreach ($ext in 'isOwner','isAdmin') {
    $extBody = @{
        name = $ext
        dataType = 'Boolean'
        targetObjects = @('User')
    } | ConvertTo-Json -Compress
    az rest --method POST `
        --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId/extensionProperties" `
        --body $extBody --headers 'Content-Type=application/json' | Out-Null
}

"Extension claims will be:"
"  extension_$($apiAppId.Replace('-', ''))_isOwner"
"  extension_$($apiAppId.Replace('-', ''))_isAdmin"
"(External ID surfaces these in tokens stripped to extension_isOwner / extension_isAdmin)"
```

Save **`vrbook-api appId`** — this becomes `EntraExternalId__ClientId` in API config.

---

## 4. Register `vrbook-web` (the SPA client)

```powershell
$webApp = az ad app create `
    --display-name "vrbook-web" `
    --sign-in-audience AzureADMyOrg `
    --is-fallback-public-client true `
    --web-redirect-uris "http://localhost:3000/auth/callback" "https://www.vrbook.example.com/auth/callback" `
    --enable-id-token-issuance true `
    --enable-access-token-issuance true `
    -o json | ConvertFrom-Json

$webAppId = $webApp.appId
"vrbook-web  appId = $webAppId"

# Grant 'access_as_user' on vrbook-api to vrbook-web.
$scopeId = az ad app show --id $apiAppId --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv
az ad app permission add --id $webAppId --api $apiAppId --api-permissions "$scopeId=Scope"
az ad app permission admin-consent --id $webAppId
```

Save **`vrbook-web appId`** — this becomes `NEXT_PUBLIC_ENTRA_CLIENT_ID` in the web app config.

---

## 5. Create the sign-in user flow (portal)

> Portal-only. External tenants offer two methods of sign-up: email + password,
> or email + one-time passcode.

1. In Entra admin center (still in the External tenant): **External Identities → User flows → + New user flow**
2. **Name:** `SignUpAndSignIn`
3. **Identity providers:** ✅ Email signup (one-time passcode or password — pick one)
4. **User attributes** (collected at sign-up):
   - ✅ Display Name (Required)
   - ✅ Email Address (Required)
5. **Application claims** (emitted in token):
   - ✅ User's Object ID
   - ✅ Display Name
   - ✅ Email Addresses
   - ✅ Email Verified
   - ✅ `extension_<api-app-id>_isOwner`  ← (will appear once you've granted yourself, §8)
   - ✅ `extension_<api-app-id>_isAdmin`
6. **MFA:** Off for dev; **Required** for Owners in prod.
7. **Create.**

(Optionally: add Google as IdP via **External Identities → All identity providers → Google**.
Configure a Google OAuth client and paste the client id/secret. Then edit the
user flow to allow Google.)

---

## 6. Associate the user flow with `vrbook-web`

In the user flow's blade → **Applications** → **+ Add application** → select `vrbook-web`.

This tells Entra "when a token request comes from `vrbook-web`, run this flow."

---

## 7. Wire the values into VrBook

Get the External tenant id:

```powershell
$externalTenantId = (az account show --query tenantId -o tsv)
"EntraExternalId__TenantId = $externalTenantId"
"EntraExternalId__Instance = https://vrbookcid.ciamlogin.com"
"EntraExternalId__ClientId = $apiAppId"
"NEXT_PUBLIC_ENTRA_AUTHORITY = https://vrbookcid.ciamlogin.com/$externalTenantId/v2.0"
"NEXT_PUBLIC_ENTRA_CLIENT_ID = $webAppId"
```

Switch back to your **workforce** tenant to write the values to staging KV:

```powershell
az login --tenant 369a3c47-33b7-4baa-98b8-6ddf16a51a31
az account set --subscription ebb8304a-6374-4db0-8de5-e8678afbb5b5

az keyvault secret set --vault-name kv-vrbook-staging --name entra-instance      --value 'https://vrbookcid.ciamlogin.com' | Out-Null
az keyvault secret set --vault-name kv-vrbook-staging --name entra-tenant-id     --value $externalTenantId                | Out-Null
az keyvault secret set --vault-name kv-vrbook-staging --name entra-api-client-id --value $apiAppId                        | Out-Null
az keyvault secret set --vault-name kv-vrbook-staging --name entra-web-client-id --value $webAppId                        | Out-Null

# OPS.M.0 — full v2.0 authority URL the SPA's MSAL config consumes. Distinct
# from entra-instance which is only the hostname prefix.
az keyvault secret set --vault-name kv-vrbook-staging --name entra-web-authority --value "https://vrbookcid.ciamlogin.com/$externalTenantId/v2.0" | Out-Null
```

Then **disable DevAuth** when an environment has real Entra wiring:

```ini
DevAuth__AllowAnonymous = false
```

---

## 8. Bootstrap yourself as Owner + Admin (one-off)

> ⚠️ **Superseded as of 2026-06-26.** This section originally PATCHed `extension_*_isOwner` / `_isAdmin` attributes on the Entra user. Roles now ship as Entra App Roles, not extension attributes — empirically the extension-attribute approach doesn't propagate into CIAM access tokens. See [`roles-architecture.md`](./roles-architecture.md) for the design rationale.
>
> **Use this procedure instead**: [`runbooks/entra-external-id-setup.md`](./runbooks/entra-external-id-setup.md) §7 — define `Owner` + `Admin` as App Roles on `vrbook-api-<env>`, then assign your user via Graph (`POST /users/{id}/appRoleAssignments`).
>
> The old extension-attribute procedure that was here previously is preserved in git history (pre-commit `<this commit>`) for archaeological reference only — do not run it. `grant-self-admin.ps1` is similarly obsolete.

---

## 9. Verify end-to-end

```powershell
# Get an access token. Easiest path for manual testing:
# 1. Open https://entra.microsoft.com -> External Identities -> User flows -> Run user flow
# 2. Choose vrbook-web app, redirect to https://jwt.ms, sign in
# 3. Copy the access_token from the URL fragment
$token = "<paste here>"

curl -H "Authorization: Bearer $token" https://api.vrbook.example.com/api/v1/me
```

Should return 200 with your provisioned profile.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `401 Unauthorized` from `/me` even with a valid token | Audience mismatch — token issued for `vrbook-web`, API expects `vrbook-api` | Ensure the SPA requests scope `api://vrbook/access_as_user`, not the SPA's clientId/.default. |
| `403 Forbidden` with valid token | Missing extension claim | Run §8 — set `extension_*_isOwner` to true. |
| User created in Entra but no row in `identity.users` | Provisioning middleware failed silently | Check API logs for `User provisioning failed for oid {Oid}` warning. |
| `IDX10503 Signature validation failed` | Authority misconfigured | Check `EntraExternalId__Instance` matches `https://<tenant>.ciamlogin.com` and `__TenantId` matches the External tenant GUID. |
| `AADSTS500011: The resource principal named api://vrbook was not found` | API app's identifier URI not set | Step 3 should have set it; verify with `az ad app show --id $apiAppId --query identifierUris`. |
