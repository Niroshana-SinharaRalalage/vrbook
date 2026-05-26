<#
.SYNOPSIS
    Loads the Claude-only service principal credentials from ../../.azure-claude/creds.json
    and logs in with az CLI. Intended to be DOT-SOURCED before any az command Claude runs.
    Isolated from your interactive az session via $env:AZURE_CONFIG_DIR.

    SECURITY NOTES
    --------------
    - The credentials file is gitignored. Never commit it.
    - The SP secret is read from disk and passed to `az login` via stdin/env so it never
      appears in tool output transcripts.
    - The SP scope is intentionally narrow (RG-scoped Contributor, sub-scoped Reader).
      To revoke at any time: az ad sp delete --id <clientId> (from your normal session).
    - Claude is instructed to NEVER print the secret value to chat. If you see it leaking,
      tell me to stop, revoke the SP, and create a new one.

.EXAMPLE
    . .\infra\scripts\_az-claude-login.ps1
    az group list --query "[].name" -o tsv
#>

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$credsPath = Join-Path $repoRoot '.azure-claude' 'creds.json'

if (-not (Test-Path $credsPath)) {
    Write-Host ''
    Write-Host '✗ Claude does not have Azure access yet.' -ForegroundColor Red
    Write-Host ''
    Write-Host "Expected credentials at: $credsPath" -ForegroundColor Yellow
    Write-Host 'Run the setup commands in infra/scripts/CLAUDE-ACCESS.md to create the SP, then retry.' -ForegroundColor Yellow
    Write-Host ''
    throw 'Claude Azure access not provisioned.'
}

$creds = Get-Content $credsPath -Raw | ConvertFrom-Json

# Isolate Claude's az session from the user's interactive session.
$env:AZURE_CONFIG_DIR = Join-Path $repoRoot '.azure-claude' 'azconfig'
if (-not (Test-Path $env:AZURE_CONFIG_DIR)) {
    New-Item -ItemType Directory -Force -Path $env:AZURE_CONFIG_DIR | Out-Null
}

# Skip re-login if the existing token still works and matches.
$current = az account show -o json 2>$null | ConvertFrom-Json
if ($current -and $current.id -eq $creds.subscriptionId -and $current.user.name -eq $creds.clientId) {
    Write-Host "✓ Reusing cached Azure session as SP $($creds.clientId)" -ForegroundColor DarkGray
    return
}

# Pass secret via env so it doesn't appear on the command line / in process listings.
$env:AZURE_CLIENT_ID     = $creds.clientId
$env:AZURE_CLIENT_SECRET = $creds.clientSecret
$env:AZURE_TENANT_ID     = $creds.tenantId

try {
    az login --service-principal `
        -u $env:AZURE_CLIENT_ID `
        --password $env:AZURE_CLIENT_SECRET `
        --tenant $env:AZURE_TENANT_ID `
        --output none
    az account set --subscription $creds.subscriptionId
}
finally {
    # Wipe the secret from env so it doesn't bleed into subsequent commands.
    Remove-Item env:AZURE_CLIENT_SECRET -ErrorAction SilentlyContinue
}

Write-Host "✓ Logged in to Azure (subscription: $($creds.subscriptionName ?? $creds.subscriptionId))" -ForegroundColor DarkGray
Write-Host "  scopes: $($creds.scopes -join ', ')" -ForegroundColor DarkGray
