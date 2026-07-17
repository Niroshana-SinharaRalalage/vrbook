<#
.SYNOPSIS
    Populate Key Vault with secrets the VrBook stack needs. Auto-generates random
    secrets where applicable; prompts for external provider secrets (Stripe, SendGrid).
    Idempotent -- re-running updates existing secret values to a new version.

.PARAMETER Env
    Target environment. One of dev | staging | prod.

.PARAMETER NonInteractive
    Skip prompts; only seed the auto-generated secrets. Useful for CI.

.EXAMPLE
    .\10-store-secrets.ps1 -Env staging
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [switch]$NonInteractive
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$state = Read-State -Env $Env
if (-not $state.keyVaultName) {
    throw "No state file for env=$Env. Run 00-foundation.ps1 first."
}
$kv = $state.keyVaultName

Write-Step "VrBook Secrets -- $Env (vault: $kv)"

function Set-KvSecret {
    param([string]$Name, [string]$Value, [string]$Description, [switch]$Overwrite)
    $existing = az keyvault secret show --vault-name $kv --name $Name -o json 2>$null | ConvertFrom-Json
    if ($existing -and -not $Overwrite) {
        Write-Skip "$Name (already set; pass -Overwrite to rotate)"
        return
    }
    az keyvault secret set --vault-name $kv --name $Name --value $Value --description $Description | Out-Null
    Write-Ok "$Name"
}

function Read-SecretPrompt {
    param([string]$Label, [string]$Hint = '')
    if ($NonInteractive) { return $null }
    Write-Host ''
    Write-Host "  $Label" -ForegroundColor Yellow
    if ($Hint) { Write-Host "    $Hint" -ForegroundColor Gray }
    $sec = Read-Host -AsSecureString '    enter value (or press Enter to skip)'
    if ($sec.Length -eq 0) { return $null }
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
    try { return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# ---- Auto-generated secrets ----
Write-Step "Auto-generated"
Set-KvSecret -Name 'feed-pepper' `
    -Value (New-RandomSecret -Length 48) `
    -Description 'Pepper used when hashing outbound iCal feed tokens. See proposal §14.4.'

Set-KvSecret -Name 'postgres-admin-password' `
    -Value ('P' + (New-RandomSecret -Length 30) + '!1') `
    -Description 'PostgreSQL Flexible Server admin password. Provisioned by Bicep.'

# Placeholder so Bicep deploy doesn't fail when looking for the secret ref.
# The operator writes the real connection string post-Bicep-deploy via:
#   az keyvault secret set --vault-name <kv> --name postgres-cs --value \
#     "Host=<fqdn>;Database=vrbook;Username=vrbook_admin;Password=<pwd>;Ssl Mode=Require;Trust Server Certificate=true"
# Database name MUST be `vrbook`. `postgres` (the built-in default) is not the app DB.
# INFRA.1 shipped with `Database=postgres` by accident and cost a full session to unwind.
Set-KvSecret -Name 'postgres-cs' -Value 'pending-bicep-deploy;Database=vrbook' `
    -Description 'Postgres ConnectionString. Overwritten by operator post-Bicep-deploy. MUST include Database=vrbook.'
Set-KvSecret -Name 'redis-cs' -Value 'pending-bicep-deploy' `
    -Description 'Redis ConnectionString. Overwritten by Bicep post-deploy.'
Set-KvSecret -Name 'signalr-cs' -Value 'pending-bicep-deploy' `
    -Description 'SignalR connection string. Overwritten by Bicep post-deploy.'
Set-KvSecret -Name 'appi-cs' -Value 'pending-bicep-deploy' `
    -Description 'Application Insights connection string. Overwritten by Bicep post-deploy.'

# OPS.M.0 — Entra External ID placeholders. Bicep secretRefs resolve at
# container-start time; without these seeds the first deploy fails before
# docs/identity/setup.md §7 has been run. The real values are written by
# the operator via the setup runbook.
Set-KvSecret -Name 'entra-instance' -Value 'pending-identity-setup' `
    -Description 'Entra External ID tenant instance URL. Overwritten by docs/identity/setup.md §7.'
Set-KvSecret -Name 'entra-tenant-id' -Value 'pending-identity-setup' `
    -Description 'Entra External ID tenant GUID. Overwritten by docs/identity/setup.md §7.'
Set-KvSecret -Name 'entra-api-client-id' -Value 'pending-identity-setup' `
    -Description 'vrbook-api app registration appId. Overwritten by docs/identity/setup.md §7.'
Set-KvSecret -Name 'entra-web-authority-admin' -Value 'pending-identity-setup' `
    -Description 'NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN: MSAL authority for the AdminSignUpSignIn user flow (Entra local only). See ADR-0016.'
Set-KvSecret -Name 'entra-web-authority-guest' -Value 'pending-identity-setup' `
    -Description 'NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST: MSAL authority for the GuestSignUpSignIn user flow (Entra local + 4 socials). See ADR-0016.'
Set-KvSecret -Name 'entra-web-client-id' -Value 'pending-identity-setup' `
    -Description 'vrbook-web SPA app registration appId. Overwritten by docs/identity/setup.md §7.'
Set-KvSecret -Name 'entra-tenant-issuer-host' -Value 'pending-identity-setup' `
    -Description 'EntraExternalId__TenantIssuerHost: External tenant issuer host (e.g. vrbookcid.ciamlogin.com) used by IdentityProviderClassifier.'

# VRB-201 (G6) — UNCONDITIONAL placeholder seeds for every Bicep secretRef that has
# no alternate producer. A Container App secretRef binds at revision-provision time,
# so an unseeded secret makes the whole main.bicep deploy fail atomically. These MUST
# be seeded even in -NonInteractive mode (the prompted section below only runs when an
# operator is present, and returns $null in CI — which is exactly the fresh-deploy case
# that failed). The interactive prompts further down `-Overwrite` these with real
# values; on re-run the idempotent skip preserves whatever real value is already set.
#   NOTE: acs-connection-string is intentionally NOT seeded here — infra/modules/
#   acs.bicep writes it at deploy time (it is the documented producer).
#   NOTE: appinsights-connection-string is likewise NOT seeded here — infra/main.bicep
#   (VRB-311) writes it from appi.outputs.connectionString (documented producer;
#   the web app dependsOn it so it exists before the secretRef binds).
Set-KvSecret -Name 'stripe-secret' -Value 'pending-identity-setup' `
    -Description 'Stripe__SecretKey (sk_...). Placeholder so the secretRef resolves on a fresh deploy; operator overwrites via the interactive prompt below (test for dev/staging, live for prod).'
Set-KvSecret -Name 'stripe-webhook-secret' -Value 'pending-identity-setup' `
    -Description 'Stripe__WebhookSecret (whsec_...). Placeholder so the secretRef resolves on a fresh deploy; operator overwrites via the interactive prompt below.'
Set-KvSecret -Name 'stripe-publishable-key' -Value 'pending-identity-setup' `
    -Description 'Stripe__PublishableKey (pk_...). Non-secret but Bicep-referenced; operator/VRB-204 sets the real value. Test mode for dev/staging, live for prod.'
Set-KvSecret -Name 'acs-sender-address' -Value 'pending-identity-setup' `
    -Description 'Acs__SenderAddress (e.g. donotreply@<managed-domain>.azurecomm.net). Bicep-referenced with no other producer; operator/VRB-204 sets the real sender (custom domain + DKIM for prod).'

# Slice OPS.2.2 — Playwright E2E persona passwords. NOT bound by any Container
# App secretRef (the nightly workflow fetches them via `az keyvault secret show`
# at run time), so they don't gate a Bicep deploy — but seed placeholders here
# so the vault inventory is complete and the operator has an obvious slot to
# fill. The operator sets the REAL passwords when creating the three Entra CIAM
# personas per docs/OPS_2_PLAYWRIGHT_PLAN.md §7 + the E2E runbook. Passwords
# live ONLY in KV, never in source. See docs/adr/0019 (lands OPS.2.8).
Set-KvSecret -Name 'e2e-guest-password' -Value 'pending-identity-setup' `
    -Description 'E2E_GUEST_PASSWORD: Playwright guest persona (e2e-guest@vrbook.test) Entra-local password. Set by operator when the CIAM persona is created; staging only.'
Set-KvSecret -Name 'e2e-owner-password' -Value 'pending-identity-setup' `
    -Description 'E2E_OWNER_PASSWORD: Playwright owner/tenant-admin persona (e2e-owner@vrbook.test) Entra-local password. Set by operator; staging only.'
Set-KvSecret -Name 'e2e-platform-admin-password' -Value 'pending-identity-setup' `
    -Description 'E2E_PLATFORM_ADMIN_PASSWORD: Playwright platform-admin persona (e2e-platform-admin@vrbook.test) Entra-local password. Set by operator; staging only.'

# ---- Prompted external secrets ----
Write-Step "External providers -- enter values now (press Enter to skip & set later)"

$stripeSecret = Read-SecretPrompt -Label 'Stripe -- secret key' -Hint 'sk_test_... from https://dashboard.stripe.com/test/apikeys'
if ($stripeSecret) {
    Set-KvSecret -Name 'stripe-secret' -Value $stripeSecret `
        -Description 'Stripe API secret key. Test mode for dev/staging; live for prod.' -Overwrite
}

$stripeWebhook = Read-SecretPrompt -Label 'Stripe -- webhook signing secret' -Hint 'whsec_... from the webhook endpoint settings page'
if ($stripeWebhook) {
    Set-KvSecret -Name 'stripe-webhook-secret' -Value $stripeWebhook `
        -Description 'Stripe webhook signature verification secret. See proposal §9.7.' -Overwrite
}

# VRB-201 — removed orphan prompts for `sendgrid-key` (email is Azure Communication
# Services now, not SendGrid — ADR-0011) and `b2c-api-client-secret` (auth moved to
# Entra External ID, ADR-0012). Neither is referenced by infra/main.bicep. Any
# lingering copies in an existing vault should be deleted by the operator:
#   az keyvault secret delete --vault-name <kv> --name sendgrid-key
#   az keyvault secret delete --vault-name <kv> --name b2c-api-client-secret

Write-Step "Done."
Write-Host "Inventory:" -ForegroundColor Yellow
az keyvault secret list --vault-name $kv --query "[].{name:name,updated:attributes.updated,enabled:attributes.enabled}" -o table

Write-Host ""
Write-Host "Next:" -ForegroundColor Yellow
Write-Host "  .\20-b2c-apps.ps1 -Env $Env -B2CDomain <your-b2c-tenant>.onmicrosoft.com"
Write-Host "  (after creating the B2C tenant in the portal per docs/b2c/setup.md §1)"
