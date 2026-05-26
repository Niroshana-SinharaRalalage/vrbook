<#
.SYNOPSIS
    Create the foundational Azure resources for a VrBook environment: Resource Group,
    Key Vault, User-Assigned Managed Identity. Idempotent -- safe to re-run.

.PARAMETER Env
    Target environment. One of dev | staging | prod.

.PARAMETER TenantId
    Defaults to the value in _common.ps1 (your AAD tenant id).

.PARAMETER SubscriptionId
    Defaults to the value in _common.ps1.

.PARAMETER Location
    Azure region. Defaults to eastus2.

.EXAMPLE
    .\00-foundation.ps1 -Env staging
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [string]$TenantId,
    [string]$SubscriptionId,
    [string]$Location
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $TenantId)       { $TenantId = $Script:DefaultTenantId }
if (-not $SubscriptionId) { $SubscriptionId = $Script:DefaultSubscriptionId }
if (-not $Location)       { $Location = $Script:DefaultLocation }

$names = Get-ResourceNames -Env $Env

Write-Step "VrBook Foundation -- $Env"
Assert-AzCli
Assert-AzLogin -TenantId $TenantId -SubscriptionId $SubscriptionId

# ---- 1. Resource Group ----
Write-Step "1/3 Resource Group: $($names.ResourceGroup)"
$existing = az group show -n $names.ResourceGroup -o json 2>$null | ConvertFrom-Json
if ($existing) {
    Write-Skip "Resource group already exists in $($existing.location)"
} else {
    az group create -n $names.ResourceGroup -l $Location `
        --tags env=$Env app=vrbook costCenter=product createdBy=foundation-script | Out-Null
    Write-Ok "Created"
}

# ---- 2. User-Assigned Managed Identity ----
Write-Step "2/3 User-Assigned Managed Identity: $($names.ManagedIdentity)"
$identity = az identity show -g $names.ResourceGroup -n $names.ManagedIdentity -o json 2>$null | ConvertFrom-Json
if (-not $identity) {
    $identity = az identity create -g $names.ResourceGroup -n $names.ManagedIdentity -l $Location `
        --tags env=$Env app=vrbook -o json | ConvertFrom-Json
    Write-Ok "Created"
} else {
    Write-Skip "Already exists"
}
Write-Ok "principalId = $($identity.principalId)"
Write-Ok "clientId    = $($identity.clientId)"

# ---- 3. Key Vault ----
Write-Step "3/3 Key Vault: $($names.KeyVault)"
$kv = az keyvault show -g $names.ResourceGroup -n $names.KeyVault -o json 2>$null | ConvertFrom-Json
if (-not $kv) {
    az keyvault create `
        -g $names.ResourceGroup `
        -n $names.KeyVault `
        -l $Location `
        --enable-rbac-authorization true `
        --enable-soft-delete true `
        --retention-days 90 `
        --enable-purge-protection true `
        --sku standard `
        --tags env=$Env app=vrbook | Out-Null
    Write-Ok "Created (soft-delete + purge protection on)"
} else {
    Write-Skip "Already exists"
}

# Grant the calling user "Key Vault Administrator" (so subsequent secret writes work).
$me = az ad signed-in-user show --query id -o tsv
$subId = (az account show --query id -o tsv)
$kvScope = "/subscriptions/$subId/resourceGroups/$($names.ResourceGroup)/providers/Microsoft.KeyVault/vaults/$($names.KeyVault)"

$hasAdmin = az role assignment list --assignee $me --scope $kvScope --role "Key Vault Administrator" -o json | ConvertFrom-Json
if ($hasAdmin.Count -eq 0) {
    az role assignment create --assignee-object-id $me --assignee-principal-type User `
        --role "Key Vault Administrator" --scope $kvScope | Out-Null
    Write-Ok "Granted yourself 'Key Vault Administrator' on $($names.KeyVault) -- wait 30s for propagation"
    Start-Sleep -Seconds 30
} else {
    Write-Skip "You already have 'Key Vault Administrator' on this vault"
}

# Grant the UAMI "Key Vault Secrets User" so Container Apps can read secret refs.
$hasReader = az role assignment list --assignee $identity.principalId --scope $kvScope --role "Key Vault Secrets User" -o json | ConvertFrom-Json
if ($hasReader.Count -eq 0) {
    az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
        --role "Key Vault Secrets User" --scope $kvScope | Out-Null
    Write-Ok "Granted UAMI '$($names.ManagedIdentity)' 'Key Vault Secrets User'"
} else {
    Write-Skip "UAMI already has 'Key Vault Secrets User'"
}

Update-State -Env $Env -Updates @{
    tenantId          = $TenantId
    subscriptionId    = $SubscriptionId
    location          = $Location
    resourceGroup     = $names.ResourceGroup
    keyVaultName      = $names.KeyVault
    keyVaultUri       = "https://$($names.KeyVault).vault.azure.net/"
    uamiName          = $names.ManagedIdentity
    uamiClientId      = $identity.clientId
    uamiPrincipalId   = $identity.principalId
    uamiResourceId    = $identity.id
}

Write-Step "Done."
Write-Host "Next: .\10-store-secrets.ps1 -Env $Env" -ForegroundColor Yellow
