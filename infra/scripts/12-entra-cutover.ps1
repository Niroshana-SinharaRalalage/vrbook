<#
.SYNOPSIS
    OPS.M.0 Entra External ID cutover automation. Wraps the steps from
    docs/identity/setup.md §2-§4 + §7 + state-file update + container restart.

.DESCRIPTION
    Two-phase script with a portal pause in the middle:

      Phase A (register apps):
        1. Switch CLI to the External tenant.
        2. Register vrbook-api with identifier URI api://vrbook + exposed scope
           access_as_user + extension attributes isOwner / isAdmin.
        3. Register vrbook-web (SPA, public client) with redirect URIs.
        4. Grant vrbook-web admin-consented access_as_user permission on vrbook-api.
        5. Print the values you need for the portal steps + pause.

      [YOU do steps 5 + 6 of docs/identity/setup.md in the portal:
        - Create the SignUpAndSignIn user flow with the printed application
          claims (incl. extension_*_isOwner / isAdmin from this run).
        - Associate the user flow with vrbook-web.
        Then press Enter to continue.]

      Phase B (wire to VrBook):
        6. Switch CLI back to the workforce tenant.
        7. Write the 5 entra-* secrets to Key Vault.
        8. Update infra/.state/<env>.json.
        9. Restart the API Container App so JwtBearer picks up the new values.

.PARAMETER Env
    staging or prod.

.PARAMETER ExternalTenantDomain
    The .onmicrosoft.com domain of the External tenant you provisioned in
    the Entra admin center, e.g. 'vrbookcid.onmicrosoft.com'.

.PARAMETER ExternalTenantInstance
    The CIAM login hostname (without protocol), e.g. 'vrbookcid.ciamlogin.com'.
    Defaults to the prefix of ExternalTenantDomain + '.ciamlogin.com'.

.PARAMETER WebRedirectUri
    The deployed web container's /auth/callback URL. Defaults to the
    documented staging Container App.

.PARAMETER SkipRegister
    Skip Phase A (apps already exist). Reads previously-saved values from
    the state file and goes straight to the pause + Phase B.

.PARAMETER SkipRestart
    Skip the API container restart (useful for dry-runs).

.EXAMPLE
    .\12-entra-cutover.ps1 -Env staging `
        -ExternalTenantDomain vrbookcid.onmicrosoft.com

.NOTES
    Run AFTER docs/identity/setup.md §1 (portal tenant creation) is complete.
    Before this script: you should have provisioned the External tenant in the
    portal and noted its .onmicrosoft.com domain.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('staging','prod')][string]$Env,
    [Parameter(Mandatory=$true)][string]$ExternalTenantDomain,
    [string]$ExternalTenantInstance,
    [string]$WebRedirectUri = 'https://ca-vrbook-web-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/auth/callback',
    [switch]$SkipRegister,
    [switch]$SkipRestart
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $ExternalTenantInstance) {
    $prefix = $ExternalTenantDomain -replace '\.onmicrosoft\.com$',''
    $ExternalTenantInstance = "$prefix.ciamlogin.com"
}
$entraInstanceUrl = "https://$ExternalTenantInstance"

$names = Get-ResourceNames -Env $Env
$kv = $names.KeyVault
$apiContainerApp = "ca-vrbook-api-$Env"
$resourceGroup = $names.ResourceGroup
$workforceTenantId = $Script:DefaultTenantId
$workforceSubscriptionId = $Script:DefaultSubscriptionId

Write-Step "OPS.M.0 Entra cutover — $Env"
Write-Host "  External tenant domain:  $ExternalTenantDomain"
Write-Host "  External tenant instance: $entraInstanceUrl"
Write-Host "  Web redirect URI:        $WebRedirectUri"
Write-Host "  KV vault:                $kv"
Write-Host "  API container app:       $apiContainerApp ($resourceGroup)"

# ---------------------------------------------------------------------------
# Phase A — register apps in the External tenant
# ---------------------------------------------------------------------------
if (-not $SkipRegister) {
    Write-Step "Phase A.1 — switch CLI context to External tenant"
    az login --tenant $ExternalTenantDomain --allow-no-subscriptions | Out-Null
    $externalTenantId = az account show --query tenantId -o tsv
    Write-Ok "External tenant id: $externalTenantId"

    $apiDisplayName = "vrbook-api-$Env"
    $webDisplayName = "vrbook-web-$Env"
    Write-Step "Phase A.2 — register $apiDisplayName"
    $apiAppRaw = az ad app create `
        --display-name $apiDisplayName `
        --sign-in-audience AzureADMyOrg `
        --identifier-uris 'api://vrbook' `
        -o json
    $apiApp = $apiAppRaw | ConvertFrom-Json
    $apiAppId = $apiApp.appId
    $apiAppObjectId = $apiApp.id
    Write-Ok "vrbook-api appId = $apiAppId"

    Write-Host "  Adding exposed scope 'access_as_user'..." -ForegroundColor Gray
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
        --body $exposeBody --headers 'Content-Type=application/json' | Out-Null
    Write-Ok "Scope 'access_as_user' exposed (id $scopeId)"

    Write-Host "  Adding extension attributes isOwner / isAdmin..." -ForegroundColor Gray
    foreach ($ext in 'isOwner','isAdmin') {
        $extBody = @{
            name = $ext
            dataType = 'Boolean'
            targetObjects = @('User')
        } | ConvertTo-Json -Compress
        # If the extension already exists this returns 409; ignore.
        try {
            az rest --method POST `
                --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId/extensionProperties" `
                --body $extBody --headers 'Content-Type=application/json' 2>$null | Out-Null
            Write-Ok "extension: $ext"
        } catch {
            Write-Skip "extension $ext (already exists)"
        }
    }

    Write-Step "Phase A.3 — register $webDisplayName"
    $webAppRaw = az ad app create `
        --display-name $webDisplayName `
        --sign-in-audience AzureADMyOrg `
        --is-fallback-public-client true `
        --web-redirect-uris 'http://localhost:3000/auth/callback' $WebRedirectUri `
        --enable-id-token-issuance true `
        --enable-access-token-issuance true `
        -o json
    $webApp = $webAppRaw | ConvertFrom-Json
    $webAppId = $webApp.appId
    Write-Ok "vrbook-web appId = $webAppId"

    Write-Host "  Granting access_as_user permission..." -ForegroundColor Gray
    # Avoid JMESPath pipe + [0] inside --query - Windows PowerShell 5.1's parser
    # mis-tokenises the bracket as a type cast. Pull all scopes as JSON, filter
    # in PowerShell where the syntax is unambiguous.
    $apiScopesJson = az ad app show --id $apiAppId --query "api.oauth2PermissionScopes" -o json
    $apiScopesArr = $apiScopesJson | ConvertFrom-Json
    $resolvedScopeId = ($apiScopesArr | Where-Object { $_.value -eq 'access_as_user' } | Select-Object -First 1).id
    if (-not $resolvedScopeId) {
        throw "No 'access_as_user' scope found on $apiDisplayName ($apiAppId)."
    }
    az ad app permission add --id $webAppId --api $apiAppId --api-permissions "$resolvedScopeId=Scope" | Out-Null
    az ad app permission admin-consent --id $webAppId | Out-Null
    Write-Ok "$webDisplayName granted access_as_user on $apiDisplayName"

    # Cache values now so a resumption after the portal pause survives a fresh shell.
    Update-State -Env $Env -Updates @{
        entraTenantDomain = $ExternalTenantDomain
        entraTenantInstance = $ExternalTenantInstance
        entraTenantId = $externalTenantId
        entraInstance = $entraInstanceUrl
        entraApiAppId = $apiAppId
        entraWebAppId = $webAppId
    }
} else {
    Write-Step "Phase A — SKIPPED (using cached state)"
    $state = Read-State -Env $Env
    $externalTenantId = $state.entraTenantId
    $apiAppId = $state.entraApiAppId
    $webAppId = $state.entraWebAppId
    if (-not $externalTenantId -or -not $apiAppId -or -not $webAppId) {
        throw "Cached state is missing entraTenantId/ApiAppId/WebAppId. Re-run without -SkipRegister."
    }
    Write-Ok "External tenant id: $externalTenantId"
    Write-Ok "vrbook-api appId = $apiAppId"
    Write-Ok "vrbook-web appId = $webAppId"
}

# ---------------------------------------------------------------------------
# Portal pause — you do setup.md §5 + §6 here
# ---------------------------------------------------------------------------
$apiAppIdNoDashes = $apiAppId.Replace('-','')
Write-Step "Portal pause — do setup.md §5 + §6, then press Enter"
Write-Host ""
Write-Host "  Open: https://entra.microsoft.com  (still signed in to the External tenant)" -ForegroundColor Yellow
Write-Host ""
Write-Host "  §5. External Identities -> User flows -> + New user flow" -ForegroundColor Yellow
Write-Host "      Name: SignUpAndSignIn" -ForegroundColor Yellow
Write-Host "      Identity providers: check 'Email signup' (one-time passcode or password)" -ForegroundColor Yellow
Write-Host "      User attributes:   check Display Name + Email Address" -ForegroundColor Yellow
Write-Host "      Application claims (the load-bearing list):" -ForegroundColor Yellow
Write-Host "        - User Object ID (oid claim)"                                             -ForegroundColor Yellow
Write-Host "        - Display Name"                                                            -ForegroundColor Yellow
Write-Host "        - Email Addresses"                                                         -ForegroundColor Yellow
Write-Host "        - Email Verified"                                                          -ForegroundColor Yellow
Write-Host "        - extension_${apiAppIdNoDashes}_isOwner"                                   -ForegroundColor Yellow
Write-Host "        - extension_${apiAppIdNoDashes}_isAdmin"                                   -ForegroundColor Yellow
Write-Host "      MFA: Off (staging); Required for Owners (prod)." -ForegroundColor Yellow
Write-Host ""
Write-Host "  §6. Open the SignUpAndSignIn user flow -> Applications -> + Add application" -ForegroundColor Yellow
Write-Host "      Pick: vrbook-web-$Env ($webAppId)" -ForegroundColor Yellow
Write-Host ""
Read-Host "Press Enter once both user-flow + application association are done"

# ---------------------------------------------------------------------------
# Phase B — wire values to VrBook
# ---------------------------------------------------------------------------
Write-Step "Phase B.1 — switch CLI back to workforce tenant"
Assert-AzLogin -TenantId $workforceTenantId -SubscriptionId $workforceSubscriptionId

Write-Step "Phase B.2 — write 5 entra-* secrets to $kv"
$webAuthority = "$entraInstanceUrl/$externalTenantId/v2.0"

az keyvault secret set --vault-name $kv --name entra-instance      --value $entraInstanceUrl | Out-Null
Write-Ok "entra-instance      = $entraInstanceUrl"

az keyvault secret set --vault-name $kv --name entra-tenant-id     --value $externalTenantId | Out-Null
Write-Ok "entra-tenant-id     = $externalTenantId"

az keyvault secret set --vault-name $kv --name entra-api-client-id --value $apiAppId | Out-Null
Write-Ok "entra-api-client-id = $apiAppId"

az keyvault secret set --vault-name $kv --name entra-web-authority --value $webAuthority | Out-Null
Write-Ok "entra-web-authority = $webAuthority"

az keyvault secret set --vault-name $kv --name entra-web-client-id --value $webAppId | Out-Null
Write-Ok "entra-web-client-id = $webAppId"

Write-Step "Phase B.3 — update infra/.state/$Env.json"
Update-State -Env $Env -Updates @{
    entraTenantDomain = $ExternalTenantDomain
    entraTenantInstance = $ExternalTenantInstance
    entraTenantId = $externalTenantId
    entraInstance = $entraInstanceUrl
    entraApiAppId = $apiAppId
    entraWebAppId = $webAppId
    entraWebAuthority = $webAuthority
}

if ($SkipRestart) {
    Write-Step "Phase B.4 — API container restart SKIPPED"
} else {
    Write-Step "Phase B.4 — restart $apiContainerApp so JwtBearer reads the new KV values"
    # `az containerapp revision restart` requires --revision <name>; without
    # it the CLI errors out with "the following arguments are required:
    # --revision". Fetch the latest (== active) revision name first.
    $latestRevision = az containerapp show -n $apiContainerApp -g $resourceGroup `
        --query "properties.latestRevisionName" -o tsv
    if (-not $latestRevision) {
        throw "Could not resolve latest revision for $apiContainerApp in $resourceGroup."
    }
    az containerapp revision restart -n $apiContainerApp -g $resourceGroup `
        --revision $latestRevision | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restart revision $latestRevision (exit code $LASTEXITCODE)."
    }
    Write-Ok "Restarted $apiContainerApp revision $latestRevision"
}

Write-Step "Done."
Write-Host ""
Write-Host "Next operational steps (not automated by this script):" -ForegroundColor Yellow
Write-Host "  1. Trigger a fresh web image build so the new NEXT_PUBLIC_ENTRA_* values bake in." -ForegroundColor Yellow
Write-Host "     gh workflow run cd-staging-web.yml --ref develop" -ForegroundColor Gray
Write-Host "     (or just push any commit to develop)" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Open https://ca-vrbook-web-staging.<domain>/ and sign up via the user flow." -ForegroundColor Yellow
Write-Host ""
Write-Host "  3. Bootstrap yourself as Owner + Admin:" -ForegroundColor Yellow
Write-Host "     .\\infra\\scripts\\grant-self-admin.ps1 -Env $Env -UserEmail you@example.com" -ForegroundColor Gray
Write-Host ""
Write-Host "  4. Verify: hit /api/v1/me with the Entra-issued token; expect 200 + isOwner=true + isAdmin=true." -ForegroundColor Yellow
