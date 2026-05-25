# AD B2C — Step-by-Step Setup

> Status: Authoritative. Run these once per environment (dev, staging, prod).
> Owner: A1 (Identity). Last updated: 2026-05-25.

This file walks you through creating a new AD B2C tenant, two app registrations, and the
three user flows the API expects. Most steps are `az` CLI; the few that require the Azure
portal are flagged inline. Total time: ~25 minutes for a clean tenant.

---

## 0. Prereqs (one-time)

```powershell
# Azure CLI 2.65+
az --version

# Sign in to your work / personal Azure AD (the directory that pays for the B2C tenant).
az login

# Confirm you can see your subscription.
az account show --query "{subscription:name,id:id,tenantId:tenantId}"
```

You'll need **Global Administrator** in the directory you sign in with, because B2C tenant
creation requires it.

---

## 1. Create the B2C tenant

> ⚠️ This step is **portal-only** at the time of writing — there's no `az b2c tenant create`
> command. Open the Azure portal as the directory's Global Administrator:

1. Search **"Azure AD B2C"** → **"+ Create"** → **"Create a new Azure AD B2C tenant"**.
2. Fill in:
   - **Organization name:** `VrBook B2C (dev)` (or staging / prod)
   - **Initial domain name:** `vrbookb2cdev` → becomes `vrbookb2cdev.onmicrosoft.com`
   - **Country/Region:** United States
   - **Subscription:** your subscription
   - **Resource group:** `rg-vrbook-dev` (create new)
3. Wait ~2 minutes for provisioning.
4. The portal will show a banner — click **"link your B2C tenant"** so it appears in your
   subscription's resource list.

From here on we run `az` commands against the **B2C tenant**, not your work tenant. Sign in
again:

```powershell
$b2cDomain = "vrbookb2cdev.onmicrosoft.com"
az login --tenant $b2cDomain --allow-no-subscriptions
```

`--allow-no-subscriptions` is required because B2C tenants don't have an Azure subscription
attached — that's normal.

---

## 2. Register the API application (`vrbook-api`)

```powershell
$apiApp = az ad app create `
  --display-name "vrbook-api" `
  --sign-in-audience "AzureADandPersonalMicrosoftAccount" `
  --identifier-uris "https://${b2cDomain}/api" `
  | ConvertFrom-Json

$apiAppId = $apiApp.appId
"vrbook-api app id = $apiAppId"
```

Expose a scope the SPA can request:

```powershell
$exposeBody = @"
{
  "api": {
    "oauth2PermissionScopes": [
      {
        "id": "$([guid]::NewGuid())",
        "adminConsentDescription": "Allow the application to call the VrBook API on the user's behalf",
        "adminConsentDisplayName": "Access VrBook API",
        "userConsentDescription": "Allow this app to call VrBook on your behalf",
        "userConsentDisplayName": "Access VrBook on your behalf",
        "value": "access_as_user",
        "type": "User",
        "isEnabled": true
      }
    ]
  }
}
"@

az rest --method PATCH `
  --uri "https://graph.microsoft.com/v1.0/applications/$($apiApp.id)" `
  --body $exposeBody --headers "Content-Type=application/json"
```

---

## 3. Register the SPA application (`vrbook-web`)

```powershell
$webApp = az ad app create `
  --display-name "vrbook-web" `
  --sign-in-audience "AzureADandPersonalMicrosoftAccount" `
  --is-fallback-public-client true `
  --web-redirect-uris "http://localhost:3000/auth/callback" "https://www.vrbook.example.com/auth/callback" `
  --enable-id-token-issuance true `
  --enable-access-token-issuance true `
  | ConvertFrom-Json

$webAppId = $webApp.appId
"vrbook-web app id = $webAppId"
```

Grant `vrbook-web` the `access_as_user` scope on `vrbook-api`:

```powershell
$scopeId = az ad app show --id $apiAppId --query "api.oauth2PermissionScopes[0].id" -o tsv

az ad app permission add `
  --id $webAppId `
  --api $apiAppId `
  --api-permissions "$scopeId=Scope"

# Pre-consent so users don't see a permissions dialog
az ad app permission admin-consent --id $webAppId
```

---

## 4. Extension attributes for `isOwner` / `isAdmin`

B2C extension attributes are scoped to an app registration. We pick `vrbook-api` as the
owner so the claim names are stable: `extension_<api-app-id-no-dashes>_isOwner`.

```powershell
$apiAppIdNoDashes = $apiAppId.Replace("-", "")

# Add boolean extension properties via Microsoft Graph
$createExt = @"
{
  "name": "isOwner",
  "dataType": "Boolean",
  "targetObjects": ["User"]
}
"@
az rest --method POST `
  --uri "https://graph.microsoft.com/v1.0/applications/$($apiApp.id)/extensionProperties" `
  --body $createExt --headers "Content-Type=application/json"

$createExt = $createExt.Replace("isOwner", "isAdmin")
az rest --method POST `
  --uri "https://graph.microsoft.com/v1.0/applications/$($apiApp.id)/extensionProperties" `
  --body $createExt --headers "Content-Type=application/json"
```

Resulting claim names (the API expects exactly these, per `HttpCurrentUser.OwnerClaim` /
`AdminClaim`):

```
extension_isOwner
extension_isAdmin
```

(B2C surfaces them stripped — the `extension_<appIdNoDashes>_` prefix only appears in the
Graph property, not in the JWT. Verify on first login by inspecting the token at jwt.ms.)

---

## 5. Create the three user flows

> Portal-only. Azure CLI does not expose user-flow create commands as of 2026-01.

In the B2C tenant portal:

### 5a. `B2C_1_SignUpSignIn_v1`

1. **User flows → + New user flow → Sign up and sign in (Recommended) → v2.0**
2. Name: `SignUpSignIn_v1` (the portal prefixes with `B2C_1_` automatically)
3. Identity providers:
   - ✅ Email signup
   - ✅ Google (after configuring it under **Identity providers**)
4. User attributes (collected on sign-up): Display Name, Email Address
5. Application claims emitted in token:
   - User's Object ID
   - Display Name
   - Email Addresses
   - Email Verified
   - `extension_isOwner`
   - `extension_isAdmin`
   - Identity provider
6. MFA: **Off** for dev, **Conditional (phone)** for prod owners
7. Create.

### 5b. `B2C_1_PasswordReset_v1`

**User flows → + New → Password reset → v2.0 → name `PasswordReset_v1`.** Defaults are fine.

### 5c. `B2C_1_ProfileEdit_v1`

**User flows → + New → Profile editing → v2.0 → name `ProfileEdit_v1`.** Collect / emit
the same attributes as 5a.

### Verify

For each flow, click **Run user flow**, select `vrbook-web`, set redirect to
`http://localhost:3000/auth/callback`, hit **Run**. You should be able to sign up and get a
JWT back.

---

## 6. Configure Google identity provider (optional, recommended)

1. Create an OAuth 2.0 client at https://console.cloud.google.com/apis/credentials
2. Authorized redirect URI: `https://${b2cDomain}/${b2cDomain}/oauth2/authresp`
3. In B2C portal: **Identity providers → Google → fill Client ID + secret → Save**
4. Edit `SignUpSignIn_v1` to add Google as an identity provider.

---

## 7. Wire the values into VrBook

Drop into your `.env` (local) or Key Vault (staging / prod):

```ini
AzureAdB2C__Instance              = https://vrbookb2cdev.b2clogin.com
AzureAdB2C__Domain                = vrbookb2cdev.onmicrosoft.com
AzureAdB2C__TenantId              = (from `az account show --query tenantId -o tsv` while signed into B2C)
AzureAdB2C__ClientId              = ($apiAppId from §2)
AzureAdB2C__SignUpSignInPolicyId  = B2C_1_SignUpSignIn_v1

NEXT_PUBLIC_B2C_AUTHORITY         = https://vrbookb2cdev.b2clogin.com/vrbookb2cdev.onmicrosoft.com/B2C_1_SignUpSignIn_v1
NEXT_PUBLIC_B2C_CLIENT_ID         = ($webAppId from §3)
```

Then **disable DevAuth** for any environment that has real B2C wired up:

```ini
DevAuth__AllowAnonymous = false
```

---

## 8. Grant your own user the owner + admin extension claims (one-off bootstrap)

Once you've signed up via the user flow, you'll have a `User` object in the B2C tenant. To
mark yourself as Owner and Admin so [Authorize(Roles = "Owner,Admin")] passes:

```powershell
# Find your user object id
$myOid = az ad user show --id "<your-email>" --query id -o tsv

$apiAppIdNoDashes = $apiAppId.Replace("-", "")

$patch = @"
{
  "extension_${apiAppIdNoDashes}_isOwner": true,
  "extension_${apiAppIdNoDashes}_isAdmin": true
}
"@

az rest --method PATCH `
  --uri "https://graph.microsoft.com/v1.0/users/$myOid" `
  --body $patch --headers "Content-Type=application/json"
```

Sign out of `vrbook-web` and sign back in to pick up the new claims.

---

## 9. Verify end-to-end

```powershell
# Get a token via the test runner (one of many options)
$token = az account get-access-token --resource api://$apiAppId --query accessToken -o tsv

curl -H "Authorization: Bearer $token" https://api.vrbook.example.com/api/v1/me
```

You should see a `200` with your provisioned profile, `isOwner: true`, `isAdmin: true`,
and a row in `identity.users`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `401 Unauthorized` from `/me` even with a valid token | Audience mismatch — token issued for SPA's `clientId`, but API expects `vrbook-api`'s `appId` | Ensure SPA requests scope `api://<api-app-id>/access_as_user`, not `<spa-client-id>/.default` |
| `403 Forbidden` with valid token | Missing role / extension claim | §8 — grant the user's `extension_isOwner` |
| User created in B2C but no row in `identity.users` | Provisioning middleware failed silently | Check API logs for `User provisioning failed for oid {Oid}` warning; usually a DB connectivity issue |
| `IDX10503: Signature validation failed` | Authority misconfigured | Confirm `AzureAdB2C__Instance` + `Domain` + `SignUpSignInPolicyId` match the B2C user-flow URL |
