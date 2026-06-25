# =====================================================================================
# Common helpers for VrBook bootstrap scripts. Sourced by 00..30-*.ps1.
# =====================================================================================

# Defaults inferred from BookingApp_Proposal.md §3.3 (naming) and §15.2 (env matrix).
$Script:DefaultTenantId       = '369a3c47-33b7-4baa-98b8-6ddf16a51a31'
$Script:DefaultSubscriptionId = 'ebb8304a-6374-4db0-8de5-e8678afbb5b5'
$Script:DefaultLocation       = 'eastus2'

# Predictable resource naming. {kind}-vrbook-{env} for dash-friendly,
# {kind}vrbook{env} for things Azure forbids dashes in (KV, ACR, storage).
function Get-ResourceNames {
    param([Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env)

    [pscustomobject]@{
        ResourceGroup       = "rg-vrbook-$Env"
        KeyVault            = "kv-vrbook-$Env"         # 3-24 chars, dashes OK
        ManagedIdentity     = "id-vrbook-$Env"
        ContainerRegistry   = "crvrbook$Env"           # alphanumeric, 5-50 chars
        ServiceBus          = "sb-vrbook-$Env"
        SignalR             = "sr-vrbook-$Env"
        StorageAccount      = "stvrbook$Env"           # alphanumeric, 3-24 chars
        Postgres            = "psql-vrbook-$Env"
        Redis               = "redis-vrbook-$Env"
        AppInsights         = "appi-vrbook-$Env"
        LogAnalytics        = "law-vrbook-$Env"
        ContainerAppsEnv    = "cae-vrbook-$Env"
        FrontDoor           = "fd-vrbook-$Env"
        ApiContainerApp     = "api-vrbook-$Env"
        WebContainerApp     = "web-vrbook-$Env"
        MigratorJob         = "migrator-vrbook-$Env"
        SyncWorkerJob       = "sync-worker-vrbook-$Env"
        BookingWorker       = "booking-worker-vrbook-$Env"
        NotificationWorker  = "notification-worker-vrbook-$Env"
        # B2C tenants are tied to the FRIENDLY NAME you typed in the portal.
        # The exact value is captured into infra/.state/<env>.json by 20-b2c-apps.ps1.
    }
}

function Write-Step {
    param([string]$Message)
    Write-Host ''
    Write-Host '===========================================================================' -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host '===========================================================================' -ForegroundColor Cyan
}

function Write-Ok    { param([string]$Message) Write-Host "  [OK] $Message" -ForegroundColor Green }
function Write-Skip  { param([string]$Message) Write-Host "  [-] $Message" -ForegroundColor Gray }
function Write-Warn2 { param([string]$Message) Write-Host "  [!] $Message" -ForegroundColor Yellow }
function Write-Fail  { param([string]$Message) Write-Host "  [X] $Message" -ForegroundColor Red }

function Assert-AzCli {
    $v = az --version 2>$null | Select-String '^azure-cli'
    if (-not $v) {
        throw 'Azure CLI not found. Install from https://aka.ms/installazurecliwindows then re-run.'
    }
}

function Assert-AzLogin {
    param([string]$TenantId, [string]$SubscriptionId)

    $current = az account show --query "{tenantId:tenantId,subId:id,name:name,user:user.name}" -o json 2>$null | ConvertFrom-Json
    if (-not $current -or $current.tenantId -ne $TenantId -or $current.subId -ne $SubscriptionId) {
        Write-Warn2 "Current az context doesn't match the target subscription. Switching..."
        az login --tenant $TenantId | Out-Null
        az account set --subscription $SubscriptionId
    }
    $current = az account show -o json | ConvertFrom-Json
    Write-Ok "Signed in as $($current.user.name)"
    Write-Ok "Subscription: $($current.name) ($($current.id))"
    Write-Ok "Tenant: $($current.tenantId)"
}

function Get-StateFilePath {
    param([string]$Env)
    # Windows PowerShell 5.1's Join-Path only accepts 2 positional args
    # (Path + ChildPath). The 3+ arg form is PS 7+. Nest the calls so
    # this works on both.
    $stateRoot = Join-Path (Join-Path $PSScriptRoot '..') '.state'
    $dir = $stateRoot | Resolve-Path -ErrorAction SilentlyContinue
    if (-not $dir) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        $dir = $stateRoot
    }
    Join-Path $dir "$Env.json"
}

function Read-State {
    param([string]$Env)
    $path = Get-StateFilePath -Env $Env
    if (Test-Path $path) {
        return Get-Content $path -Raw | ConvertFrom-Json
    }
    return [pscustomobject]@{}
}

function Save-State {
    param([string]$Env, [object]$State)
    $path = Get-StateFilePath -Env $Env
    $State | ConvertTo-Json -Depth 10 | Set-Content -Path $path -Encoding utf8
    Write-Ok "State saved → $path"
}

function Update-State {
    param([string]$Env, [hashtable]$Updates)
    $state = Read-State -Env $Env
    foreach ($k in $Updates.Keys) {
        if ($state.PSObject.Properties[$k]) {
            $state.PSObject.Properties[$k].Value = $Updates[$k]
        } else {
            $state | Add-Member -NotePropertyName $k -NotePropertyValue $Updates[$k] -Force
        }
    }
    Save-State -Env $Env -State $state
}

function New-RandomSecret {
    param([int]$Length = 48)
    $bytes = New-Object byte[] $Length
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    [Convert]::ToBase64String($bytes) -replace '[+/=]', ''
}
