<#
.SYNOPSIS
    Provision the two AD B2C app registrations (vrbook-api, vrbook-web) and the
    isOwner/isAdmin extension attributes. Assumes the B2C tenant already exists
    (created via Azure portal per docs/b2c/setup.md §1) -- Microsoft does NOT expose
    tenant creation via CLI as of 2026-05.

.PARAMETER Env
    Target environment. dev | staging | prod.

.PARAMETER B2CDomain
    The full domain of your B2C tenant, e.g. vrbookb2cdev.onmicrosoft.com.

.PARAMETER WebRedirectUris
    Redirect URIs for the SPA app registration.
    Defaults to localhost + the production www host.

.EXAMPLE
    .\20-b2c-apps.ps1 -Env dev -B2CDomain vrbookb2cdev.onmicrosoft.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [Parameter(Mandatory=$true)][string]$B2CDomain,
    [string[]]$WebRedirectUris = @(
        'http://localhost:3000/auth/callback',
        'https://www.vrbook.example.com/auth/callback'
    )
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$state = Read-State -Env $Env
if (-not $state.keyVaultName) {
    throw "No state file for env=$Env. Run 00-foundation.ps1 first."
}

Write-Step "VrBook B2C App Registrations -- $Env ($B2CDomain)"
Write-Warn2 "Signing into the B2C tenant (separate from your work tenant)..."
Write-Host "    A browser window will open. Use a Global Admin in $B2CDomain."

az login --tenant $B2CDomain --allow-no-subscriptions | Out-Null

$ctx = az account show -o json | ConvertFrom-Json
Write-Ok "Signed in as $($ctx.user.name) on tenant $($ctx.tenantId)"

# ---- 1. vrbook-api ----
Write-Step "1/3 App registration: vrbook-api"
$apiApp = az ad app list --display-name 'vrbook-api' --query "[0]" -o json | ConvertFrom-Json
if ($apiApp) {
    Write-Skip "vrbook-api exists (appId = $($apiApp.appId))"
} else {
    $apiApp = az ad app create `
        --display-name 'vrbook-api' `
        --sign-in-audience 'AzureADandPersonalMicrosoftAccount' `
        --identifier-uris "https://$B2CDomain/api" `
        -o json | ConvertFrom-Json
    Write-Ok "Created (appId = $($apiApp.appId))"
}
$apiAppId = $apiApp.appId
$apiAppObjectId = $apiApp.id

# Expose `access_as_user` scope if not present.
$existingScopes = $apiApp.api.oauth2PermissionScopes
$hasScope = $existingScopes | Where-Object { $_.value -eq 'access_as_user' }
if (-not $hasScope) {
    $scopeId = [guid]::NewGuid().ToString()
    $body = @{
        api = @{
            oauth2PermissionScopes = @(@{
                id = $scopeId
                adminConsentDescription = 'Allow the application to call the VrBook API on the user''s behalf'
                adminConsentDisplayName = 'Access VrBook API'
                userConsentDescription  = 'Allow this app to call VrBook on your behalf'
                userConsentDisplayName  = 'Access VrBook on your behalf'
                value = 'access_as_user'
                type  = 'User'
                isEnabled = $true
            })
        }
    } | ConvertTo-Json -Depth 10 -Compress
    az rest --method PATCH `
        --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId" `
        --body $body --headers 'Content-Type=application/json' | Out-Null
    Write-Ok "Exposed scope 'access_as_user'"
} else {
    Write-Skip "Scope 'access_as_user' already exposed"
}

# Extension attributes.
$extProps = az rest --method GET --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId/extensionProperties" -o json | ConvertFrom-Json
foreach ($ext in @('isOwner','isAdmin')) {
    if ($extProps.value | Where-Object { $_.name -eq $ext }) {
        Write-Skip "extension_$ext already exists"
    } else {
        $extBody = @{
            name = $ext
            dataType = 'Boolean'
            targetObjects = @('User')
        } | ConvertTo-Json -Compress
        az rest --method POST `
            --uri "https://graph.microsoft.com/v1.0/applications/$apiAppObjectId/extensionProperties" `
            --body $extBody --headers 'Content-Type=application/json' | Out-Null
        Write-Ok "Added extension property '$ext'"
    }
}

# ---- 2. vrbook-web ----
Write-Step "2/3 App registration: vrbook-web"
$webApp = az ad app list --display-name 'vrbook-web' --query "[0]" -o json | ConvertFrom-Json
if ($webApp) {
    Write-Skip "vrbook-web exists (appId = $($webApp.appId))"
} else {
    $redirectArgs = $WebRedirectUris | ForEach-Object { $_ }
    $webApp = az ad app create `
        --display-name 'vrbook-web' `
        --sign-in-audience 'AzureADandPersonalMicrosoftAccount' `
        --is-fallback-public-client true `
        --web-redirect-uris @redirectArgs `
        --enable-id-token-issuance true `
        --enable-access-token-issuance true `
        -o json | ConvertFrom-Json
    Write-Ok "Created (appId = $($webApp.appId))"
}
$webAppId = $webApp.appId
$webAppObjectId = $webApp.id

# Grant 'access_as_user' on vrbook-api to vrbook-web.
$scopeId = (az ad app show --id $apiAppId --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv)
$existingPerms = az ad app permission list --id $webAppId -o json | ConvertFrom-Json
$hasPerm = $existingPerms | Where-Object { $_.resourceAppId -eq $apiAppId -and ($_.resourceAccess | Where-Object { $_.id -eq $scopeId }) }
if (-not $hasPerm) {
    az ad app permission add --id $webAppId --api $apiAppId --api-permissions "$scopeId=Scope" | Out-Null
    az ad app permission admin-consent --id $webAppId | Out-Null
    Write-Ok "Granted vrbook-web → vrbook-api/access_as_user (admin-consented)"
} else {
    Write-Skip "Permission already granted"
}

# ---- 3. Persist values to state + KV ----
Write-Step "3/3 Persist values"
$b2cTenantId = $ctx.tenantId

Update-State -Env $Env -Updates @{
    b2cDomain          = $B2CDomain
    b2cTenantId        = $b2cTenantId
    b2cApiAppId        = $apiAppId
    b2cWebAppId        = $webAppId
    b2cInstance        = "https://$($B2CDomain.Split('.')[0]).b2clogin.com"
    b2cSignUpSignInPolicyId = 'B2C_1_SignUpSignIn_v1'
}

# Switch BACK to the work tenant to write to Key Vault.
Write-Step "Switching back to work tenant to update Key Vault..."
az login --tenant $state.tenantId | Out-Null
az account set --subscription $state.subscriptionId

$kv = $state.keyVaultName
function Upsert-KvSecret {
    param([string]$Name, [string]$Value)
    az keyvault secret set --vault-name $kv --name $Name --value $Value | Out-Null
    Write-Ok "$Name"
}

Upsert-KvSecret -Name 'b2c-instance' -Value "https://$($B2CDomain.Split('.')[0]).b2clogin.com"
Upsert-KvSecret -Name 'b2c-domain'   -Value $B2CDomain
Upsert-KvSecret -Name 'b2c-tenant-id'-Value $b2cTenantId
Upsert-KvSecret -Name 'b2c-api-client-id' -Value $apiAppId
Upsert-KvSecret -Name 'b2c-web-client-id' -Value $webAppId

Write-Step "Done."
Write-Host ""
Write-Host "What you must STILL do in the B2C portal (no CLI exists for these):" -ForegroundColor Yellow
Write-Host "  1. Create user flows B2C_1_SignUpSignIn_v1, B2C_1_PasswordReset_v1, B2C_1_ProfileEdit_v1"
Write-Host "     (User flows blade → + New user flow. See docs/b2c/setup.md §5.)"
Write-Host "  2. (Optional) Add Google as identity provider for SignUpSignIn_v1."
Write-Host "  3. Sign up your own account, then run:"
Write-Host "     ./scripts/grant-self-admin.ps1 -Env $Env -UserEmail <your-email>"
Write-Host ""
Write-Host "Then:"
Write-Host "  .\30-github-oidc.ps1 -Env $Env"
