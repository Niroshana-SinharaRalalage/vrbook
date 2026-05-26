<#
.SYNOPSIS
    Loads Claude-only service principal credentials from .azure-claude/creds.json and
    logs in with az CLI. Dot-source before any az command. Isolated from your interactive
    az session via $env:AZURE_CONFIG_DIR.

    SECURITY
    - creds.json is gitignored.
    - SP secret passed via env, wiped from env after login.
    - SP scope is RG-Contributor + sub-Reader (see CLAUDE-ACCESS.md).
    - To revoke: az ad sp delete --id <clientId>
#>

$ErrorActionPreference = 'Stop'

$repoRoot  = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$credsPath = Join-Path (Join-Path $repoRoot '.azure-claude') 'creds.json'

if (-not (Test-Path $credsPath)) {
    throw "Claude does not have Azure access. Run the setup in infra/scripts/CLAUDE-ACCESS.md. Expected: $credsPath"
}

$creds = Get-Content $credsPath -Raw | ConvertFrom-Json

$env:AZURE_CONFIG_DIR = Join-Path (Join-Path $repoRoot '.azure-claude') 'azconfig'
if (-not (Test-Path $env:AZURE_CONFIG_DIR)) {
    New-Item -ItemType Directory -Force -Path $env:AZURE_CONFIG_DIR | Out-Null
}

# Check if we already have a valid session for this SP. az writes errors to stderr;
# $ErrorActionPreference=Stop + native commands need explicit ExitCode handling.
$alreadyLoggedIn = $false
$savedEAP = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $accountJson = az account show -o json 2>$null
    if ($LASTEXITCODE -eq 0 -and $accountJson) {
        $current = $accountJson | ConvertFrom-Json
        if ($current.id -eq $creds.subscriptionId -and $current.user.name -eq $creds.clientId) {
            $alreadyLoggedIn = $true
        }
    }
} finally {
    $ErrorActionPreference = $savedEAP
}

if (-not $alreadyLoggedIn) {
    $env:AZURE_CLIENT_ID     = $creds.clientId
    $env:AZURE_CLIENT_SECRET = $creds.clientSecret
    $env:AZURE_TENANT_ID     = $creds.tenantId
    try {
        & az login --service-principal -u $env:AZURE_CLIENT_ID --password $env:AZURE_CLIENT_SECRET --tenant $env:AZURE_TENANT_ID --output none
        if ($LASTEXITCODE -ne 0) { throw "az login failed (exit $LASTEXITCODE)" }
        & az account set --subscription $creds.subscriptionId
        if ($LASTEXITCODE -ne 0) { throw "az account set failed (exit $LASTEXITCODE)" }
    } finally {
        Remove-Item env:AZURE_CLIENT_SECRET -ErrorAction SilentlyContinue
    }
    $subDisplay = $creds.subscriptionId
    if ($creds.subscriptionName) { $subDisplay = $creds.subscriptionName }
    Write-Host "[claude-az] Logged in as SP $($creds.clientId) on $subDisplay" -ForegroundColor DarkGray
} else {
    Write-Host "[claude-az] Reusing cached session as SP $($creds.clientId)" -ForegroundColor DarkGray
}