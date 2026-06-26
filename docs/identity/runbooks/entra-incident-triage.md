# Runbook: Entra incident triage

> **Status**: Authoritative for VrBook.
> **Owner**: Identity (A1).
> **Last updated**: 2026-06-26 — drawn from the OPS.M.0 cutover debugging session, every symptom in this doc was hit and resolved live.
> **Use this when**: sign-in is broken, `/admin` is 403ing, the access token looks wrong, or something else identity-related is failing.

Diagnostic flow: pick the symptom from §1, walk the steps to a root cause, apply the fix from the linked section in §2.

---

## 1. Symptom-to-cause lookup

### 1.1 Sign-in itself fails

| Symptom (what the user sees) | Most likely cause | Go to |
|---|---|---|
| Microsoft generic "Sorry, but we're having trouble signing you in." `AADSTS700038: 00000000-0000-0000-0000-000000000000 is not a valid application identifier` | The web bundle has fallback constants — `NEXT_PUBLIC_ENTRA_*` resolved to `undefined` at `next build` time | §2.1 |
| Sign-in URL is `login.microsoftonline.com/common/...` instead of `vrbook.ciamlogin.com/...` | Same root cause as above — `NEXT_PUBLIC_ENTRA_AUTHORITY` is undefined so MSAL falls back to `https://login.microsoftonline.com/common` | §2.1 |
| Sign-in completes on Entra but page returns to `/auth/callback` with 400 `AADSTS9002326: Cross-origin token redemption is permitted only for the 'Single-Page Application' client-type` | Redirect URIs registered on the `web` platform, not the `spa` platform — confidential-client expectations on a public client | §2.2 |
| Sign-in completes, browser returns to `/`, but `useAuth()` reports unauthenticated (Sign in button still showing) | `handleRedirectPromise()` didn't process the redirect — or it did but tokens didn't reach the right session storage key | §2.3 |
| 400 at `/oauth2/v2.0/token` with `invalid_grant` or `invalid_client` | PKCE code_verifier mismatch (rare; usually SPA-platform issue) or expired auth code | §2.2 |

### 1.2 Sign-in works but API calls fail

| Symptom | Most likely cause | Go to |
|---|---|---|
| API returns 401 with valid Bearer token | Audience mismatch — token's `aud` is the SPA appId, not the API appId | §2.4 |
| API returns 401 with `IDX10503 Signature validation failed` | JwtBearer authority misconfigured — `EntraExternalId__Instance` / `__TenantId` in KV don't match the actual tenant | §2.5 |
| API returns 403 on `/admin` routes despite a valid token | Token missing `roles` claim or the value isn't `Owner` / `Admin` | §2.6 |
| API returns 200 but the user can see things they shouldn't | DevAuth fallback is active — even though Entra token validates, the request also matches the DevAuth scheme | §2.7 |

### 1.3 Build / deploy errors before the user sees anything

| Symptom | Most likely cause | Go to |
|---|---|---|
| `cd-staging-web` workflow fails at "Fetch Entra build args from Key Vault" with `(SecretNotFound) A secret with name entra-* was not found` | KV doesn't have the Entra placeholders (seed script wasn't run) | §2.8 |
| Workflow's "Fetch" step succeeds but `next build` log shows `NEXT_PUBLIC_ENTRA_AUTHORITY=undefined` baked into the bundle | Dockerfile doesn't declare matching `ARG NEXT_PUBLIC_ENTRA_*` — Docker silently drops unknown build-args | §2.9 |
| `12-entra-cutover.ps1` fails at parse time with `Missing type name after '['` or `The string is missing the terminator: '` | UTF-8 BOM missing on the file — Windows PowerShell 5.1 reads as ANSI, mangles em-dashes and § signs | §2.10 |
| `12-entra-cutover.ps1` runs but errors with `A positional parameter cannot be found that accepts argument '.state'` | PowerShell 5.1's `Join-Path` only accepts 2 positional args; the 3-arg form requires PS 7+ | §2.11 |
| API Container App restart fails with `ERROR: the following arguments are required: --revision` | `az containerapp revision restart` needs an explicit revision name | §2.12 |

---

## 2. Fixes

### 2.1 Web bundle has fallback constants (AADSTS700038)

The `NEXT_PUBLIC_ENTRA_*` values are inlined into the **browser** bundle at `next build` time. They must reach the Docker build context as `--build-arg`, not just the Container App runtime env.

Check the chain:
1. **KV** — `az keyvault secret show --vault-name kv-vrbook-<env> --name entra-web-authority --query value -o tsv`. Should return the real authority URL, not `pending-identity-setup`.
2. **Workflow** — `.github/workflows/cd-staging-web.yml` step "Fetch Entra build args from Key Vault" must pull these and pass to `docker/build-push-action@v6`'s `build-args:` block.
3. **Dockerfile** — `web/Dockerfile` must declare `ARG NEXT_PUBLIC_ENTRA_AUTHORITY` and `ARG NEXT_PUBLIC_ENTRA_CLIENT_ID` (4 lines total — 2 ARG + 2 ENV). See [`entra-key-rotation.md`](./entra-key-rotation.md) §1 last paragraph if the Dockerfile is missing these.

After fixing any link in the chain, trigger a fresh build:

```bash
gh workflow run cd-staging-web.yml --ref develop
```

Verification: in a fresh incognito tab, the sign-in URL must start with `https://vrbook.ciamlogin.com/<tenantId>/...`, not `https://login.microsoftonline.com/common/...`.

### 2.2 Redirect URIs on the wrong platform (AADSTS9002326)

The `vrbook-web-<env>` app must declare redirect URIs on the `spa` platform, not `web`. The `az ad app create --web-redirect-uris` CLI flag puts them on the wrong platform. Patch via Graph:

```powershell
az login --tenant vrbook.onmicrosoft.com --allow-no-subscriptions | Out-Null
$webAppId = '<vrbook-web-$env appId>'
$webObjectId = az ad app show --id $webAppId --query id -o tsv

$body = @{
    spa = @{ redirectUris = @(
        'http://localhost:3000/auth/callback',
        'https://ca-vrbook-web-<env>.<cae-domain>/auth/callback'
    )}
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

Verify: `az ad app show --id $webAppId --query "{web:web.redirectUris, spa:spa.redirectUris}" -o json` — URIs under `spa`, empty array under `web`.

### 2.3 `handleRedirectPromise()` didn't process the redirect

In MSAL Browser 3.x, the `PublicClientApplication` constructor doesn't auto-process redirects. `MsalProvider` from `@azure/msal-react` 2.x calls `initialize()` and `handleRedirectPromise()` automatically — but only if the instance is created and passed via `MsalProvider`'s `instance` prop on every mount.

Confirm `web/src/components/Providers.tsx`:
1. Creates the instance once via `useMemo`.
2. Wraps `children` in `<MsalProvider instance={msalInstance}>`.
3. Does NOT call `instance` methods synchronously inside `useMemo` (e.g. `instance.getAllAccounts()` before `initialize()` — borderline supported on 3.27).

DevTools Console diagnostic:

```javascript
Object.keys(sessionStorage).filter(k => k.toLowerCase().includes('msal')).length
```

- `0` → handleRedirectPromise didn't run. Code-side bug. Most likely Providers.tsx setup.
- `>0` but no `account` key → token exchange itself failed. Look at Network tab's `/oauth2/v2.0/token` response body for the OAuth error code.

### 2.4 Token has wrong audience (`aud`)

ASP.NET JwtBearer checks `aud` against `EntraExternalId__ClientId` (the API app's appId, from KV `entra-api-client-id`). If MSAL was minting tokens for the SPA, `aud` would be the web app's appId.

Verify the MSAL scope in `web/src/lib/auth/msalConfig.ts`:

```typescript
export const apiScopes: string[] = ['api://vrbook/access_as_user'];
```

It MUST be the API app's identifier URI + exposed scope. NEVER `${clientId}/.default` (that mints for the SPA).

Also check `web/src/components/Providers.tsx` line ~67: the `setTokenProvider` function must use `apiScopes`, not an inline `${msalConfig.auth.clientId}/.default`. This was a real bug fixed in commit `63cd52a`.

Regression test: `web/src/lib/auth/msalConfig.test.ts` enforces both invariants.

### 2.5 JwtBearer signature validation failure

`IDX10503 Signature validation failed` usually means JwtBearer can't fetch the OpenID Connect discovery document or the JWKS keys at the configured authority.

Verify:

```powershell
# Confirm the authority is reachable
$instance = az keyvault secret show --vault-name kv-vrbook-<env> --name entra-instance --query value -o tsv
$tenantId = az keyvault secret show --vault-name kv-vrbook-<env> --name entra-tenant-id --query value -o tsv

curl "$instance/$tenantId/v2.0/.well-known/openid-configuration"
```

Expect a JSON document with `jwks_uri`, `issuer`, etc. If 404 → tenant ID wrong. If timeout → API container can't reach the public internet (check egress NSG rules).

### 2.6 Token has no `roles` claim

This is the "App Roles not assigned" case.

```javascript
// Browser DevTools Console
const akey = Object.keys(sessionStorage).find(k => k.toLowerCase().includes('-accesstoken-'));
const claims = JSON.parse(atob(JSON.parse(sessionStorage.getItem(akey)).secret.split('.')[1]));
console.log('roles:', claims.roles, 'all keys:', Object.keys(claims));
```

- `roles: ["Owner", "Admin"]` → token is correct; API-side issue.
- `roles: undefined` and no `roles` in keys → user has no App Role assignments. See [`entra-key-rotation.md`](./entra-key-rotation.md) §2.

If `roles` is present but `[Authorize(Roles="Owner,Admin")]` still rejects: case mismatch. App Role values are case-sensitive. `Owner` ≠ `owner`. Re-check [`entra-key-rotation.md`](./entra-key-rotation.md) §6 step 3.

### 2.7 DevAuth fallback masking Entra failures

In dev `DevAuth__AllowAnonymous=true` is fine. In staging or prod it's a footgun — DevAuth's "Dev Owner" persona silently grants Owner+Admin to any unauthenticated request, making it impossible to verify Entra is actually working.

```powershell
# Confirm current value on the active revision
az containerapp show -n ca-vrbook-api-<env> -g rg-vrbook-<env> `
    --query "properties.template.containers[0].env[?contains(name, 'DevAuth__AllowAnonymous')]" -o json

# Flip it off (creates a new revision)
az containerapp update -n ca-vrbook-api-<env> -g rg-vrbook-<env> `
    --set-env-vars 'DevAuth__AllowAnonymous=false'
```

For staging + prod, `infra/main.bicep` line 272 already defaults to `false` (commit `be897bc`). If you see it as `true`, someone manually overrode and the override is the bug.

### 2.8 KV missing Entra secrets at deploy time

`infra/scripts/10-store-secrets.ps1` seeds 5 placeholder `entra-*` secrets so first-time deploys don't fail. If the placeholders are missing (KV bypass, manual delete, fresh subscription), re-run:

```powershell
.\infra\scripts\10-store-secrets.ps1 -Env <env>
```

Then run `12-entra-cutover.ps1 -SkipRegister` to write real values (or re-run from scratch if the apps don't exist yet — see [`entra-external-id-setup.md`](./entra-external-id-setup.md)).

### 2.9 Dockerfile doesn't declare ARG for build args

`web/Dockerfile` must have these four lines before `RUN npm run build`:

```dockerfile
ARG NEXT_PUBLIC_ENTRA_AUTHORITY=""
ARG NEXT_PUBLIC_ENTRA_CLIENT_ID=""
ENV NEXT_PUBLIC_ENTRA_AUTHORITY=${NEXT_PUBLIC_ENTRA_AUTHORITY}
ENV NEXT_PUBLIC_ENTRA_CLIENT_ID=${NEXT_PUBLIC_ENTRA_CLIENT_ID}
```

If any are missing, Docker silently drops the matching `--build-arg`, `next build` sees the value as undefined, MSAL falls back to defaults. Fix: add the missing lines, push, the next workflow run bakes the real values in. Commit `989104d` is the canonical fix.

### 2.10 PowerShell 5.1 parse errors

If `12-entra-cutover.ps1` fails to parse (e.g. "Missing type name after '['"), the file was saved without UTF-8 BOM. PS 5.1 interprets BOM-less files as ANSI, mangling em-dashes (`—`) and section signs (`§`).

Re-save with BOM:

```powershell
$path = 'infra/scripts/12-entra-cutover.ps1'
$content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($path, $content, $utf8WithBom)
```

Verify with parse check:

```powershell
$tokens = $null; $errors = $null
[System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $path), [ref]$tokens, [ref]$errors)
$errors.Count  # should be 0
```

### 2.11 PowerShell 5.1 `Join-Path` 3-arg form

PS 5.1's `Join-Path` accepts only `-Path` + `-ChildPath`. Code like:

```powershell
Join-Path $PSScriptRoot '..' '.state'
```

is a PS 7+ form. PS 5.1 errors with `A positional parameter cannot be found that accepts argument '.state'`. Fix: nest the calls.

```powershell
Join-Path (Join-Path $PSScriptRoot '..') '.state'
```

`infra/scripts/_common.ps1` `Get-StateFilePath` was patched in commit `fac9279`.

### 2.12 Container App revision restart needs `--revision`

`az containerapp revision restart -n <app> -g <rg>` errors with `the following arguments are required: --revision`. The Az CLI requires explicit revision name. Fix in scripts:

```powershell
$latestRevision = az containerapp show -n $apiContainerApp -g $resourceGroup `
    --query 'properties.latestRevisionName' -o tsv
az containerapp revision restart -n $apiContainerApp -g $resourceGroup `
    --revision $latestRevision
```

Also check exit code via `$LASTEXITCODE` afterwards — the old `| Out-Null` pattern hid failures. Patched in commit `b21447c`.

---

## 3. Quick diagnostic commands (cheat sheet)

| What to know | One-liner |
|---|---|
| Current External tenant context | `az account show --query "{tenant:tenantId, user:user.name}" -o table` |
| KV `entra-*` values | `az keyvault secret list --vault-name kv-vrbook-<env> --query "[?starts_with(name, 'entra-')].{name:name, updated:attributes.updated}" -o table` |
| API container's active revision + image | `az containerapp show -n ca-vrbook-api-<env> -g rg-vrbook-<env> --query "{rev:properties.latestRevisionName, image:properties.template.containers[0].image}" -o table` |
| API container's DevAuth env | `az containerapp show -n ca-vrbook-api-<env> -g rg-vrbook-<env> --query "properties.template.containers[0].env[?contains(name,'DevAuth')]" -o table` |
| App Role definitions on the API app | `az ad app show --id <apiAppId> --query "appRoles[].{value:value, enabled:isEnabled}" -o table` |
| Current App Role assignments | `az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/<apiSpId>/appRoleAssignedTo" --query "value[].{user:principalDisplayName, role:appRoleId}" -o table` |
| Sign-in error correlation id (from "Troubleshooting details" panel on Entra error page) | Search Entra admin center → Sign-in logs → filter by correlationId |

---

## Related

- [`entra-external-id-setup.md`](./entra-external-id-setup.md) — first-time provisioning.
- [`entra-key-rotation.md`](./entra-key-rotation.md) — routine ops.
- [`roles-architecture.md`](../roles-architecture.md) — why App Roles.
- [ADR-0012](../../adr/0012-entra-external-id-over-b2c.md) — provider choice.
