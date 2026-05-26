<#
.SYNOPSIS
    Bootstrap your own Entra External ID account with isOwner=true + isAdmin=true so
    [Authorize(Roles="Owner,Admin")] guarded endpoints work for you. One-time per env.
    Sign up via the External tenant's user flow FIRST, then run this.

    Per ADR-0012, identity is Microsoft Entra External ID (replaced AD B2C).

.PARAMETER Env
    Which environment's External tenant to write to. dev | staging | prod.

.PARAMETER UserEmail
    The email you signed up with.

.PARAMETER ExternalTenant
    The External tenant domain (e.g. vrbookcid.onmicrosoft.com). If not provided,
    reads from infra/.state/<env>.json (key: entraTenantDomain).

.PARAMETER ApiAppId
    The vrbook-api app registration's appId in the External tenant. If not provided,
    reads from infra/.state/<env>.json (key: entraApiAppId).

.EXAMPLE
    .\grant-self-admin.ps1 -Env staging -UserEmail you@example.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [Parameter(Mandatory=$true)][string]$UserEmail,
    [string]$ExternalTenant,
    [string]$ApiAppId
)

. (Join-Path $PSScriptRoot '_common.ps1')

$state = Read-State -Env $Env
if (-not $ExternalTenant) { $ExternalTenant = $state.entraTenantDomain }
if (-not $ApiAppId)       { $ApiAppId       = $state.entraApiAppId }

if (-not $ExternalTenant -or -not $ApiAppId) {
    throw "ExternalTenant + ApiAppId required (either as params or in infra/.state/$Env.json keys entraTenantDomain + entraApiAppId). Run docs/identity/setup.md §1-4 first."
}

Write-Step "Granting $UserEmail isOwner + isAdmin on $ExternalTenant"
az login --tenant $ExternalTenant --allow-no-subscriptions | Out-Null

# Try identities first (local accounts), fall back to mail (federated accounts).
$users = az rest --method GET `
    --uri "https://graph.microsoft.com/v1.0/users?`$filter=identities/any(c:c/issuerAssignedId eq '$UserEmail' and c/issuer eq '$ExternalTenant')" `
    -o json | ConvertFrom-Json

if (-not $users.value -or $users.value.Count -eq 0) {
    $users = az rest --method GET --uri "https://graph.microsoft.com/v1.0/users?`$filter=mail eq '$UserEmail'" -o json | ConvertFrom-Json
}

if (-not $users.value -or $users.value.Count -eq 0) {
    throw "No Entra user found with email '$UserEmail'. Sign up via the SignUpAndSignIn user flow first."
}

$user = $users.value[0]
Write-Ok "Found user $($user.id) ($($user.displayName))"

$apiAppIdNoDashes = $ApiAppId.Replace('-', '')
$ownerProp = "extension_${apiAppIdNoDashes}_isOwner"
$adminProp = "extension_${apiAppIdNoDashes}_isAdmin"

$patch = @{ $ownerProp = $true; $adminProp = $true } | ConvertTo-Json -Compress

az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/users/$($user.id)" `
    --body $patch --headers 'Content-Type=application/json' | Out-Null

Write-Ok "Set $ownerProp = true"
Write-Ok "Set $adminProp = true"
Write-Step "Done."
Write-Host "Sign out and sign back in on the web app to pick up the new claims." -ForegroundColor Yellow
