<#
.SYNOPSIS
    Bootstrap your own B2C account with isOwner=true + isAdmin=true so that the
    [Authorize(Roles="Owner,Admin")] guarded endpoints work for you. One-time per env.
    Sign up at the SignUpSignIn_v1 flow FIRST, then run this.

.PARAMETER Env
    The environment whose B2C tenant to write to. dev | staging | prod.

.PARAMETER UserEmail
    The email you signed up with.

.EXAMPLE
    .\grant-self-admin.ps1 -Env dev -UserEmail you@example.com
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [Parameter(Mandatory=$true)][string]$UserEmail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$state = Read-State -Env $Env
if (-not $state.b2cDomain -or -not $state.b2cApiAppId) {
    throw "B2C state for env=$Env not found. Run 20-b2c-apps.ps1 first."
}

Write-Step "Granting $UserEmail isOwner + isAdmin on $($state.b2cDomain)"
az login --tenant $state.b2cDomain --allow-no-subscriptions | Out-Null

# Find the user's object id. B2C local-account email sign-ins are stored as
# `identities` with signInType=emailAddress, so we need a Graph query.
$users = az rest --method GET `
    --uri "https://graph.microsoft.com/v1.0/users?`$filter=identities/any(c:c/issuerAssignedId eq '$UserEmail' and c/issuer eq '$($state.b2cDomain)')" `
    -o json | ConvertFrom-Json

if ($users.value.Count -eq 0) {
    # Fall back to mail attribute (social sign-ins land here).
    $users = az rest --method GET --uri "https://graph.microsoft.com/v1.0/users?`$filter=mail eq '$UserEmail'" -o json | ConvertFrom-Json
}

if ($users.value.Count -eq 0) {
    throw "No B2C user found with email '$UserEmail'. Sign up via the SignUpSignIn_v1 user flow first."
}

$user = $users.value[0]
Write-Ok "Found user $($user.id) ($($user.displayName))"

$apiAppIdNoDashes = $state.b2cApiAppId.Replace('-', '')
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
