<#
.SYNOPSIS
    Configure GitHub Actions OIDC federation against the User-Assigned Managed Identity
    created by 00-foundation.ps1. Lets cd-staging.yml / cd-prod.yml deploy to Azure
    without storing any service-principal credentials in GitHub Secrets.

.PARAMETER Env
    Environment to wire up. dev | staging | prod.

.PARAMETER GitHubRepo
    The "owner/repo" slug. Defaults to Niroshana-SinharaRalalage/vrbook.

.PARAMETER Branch
    Which branch the federation trusts. main for prod; develop for staging.
    Defaults to develop for staging and main for prod.

.PARAMETER GitHubEnvironment
    Optional GitHub Environment name. If your workflow uses `environment:` (cd-prod does),
    use that. Defaults to the same as $Env.

.EXAMPLE
    .\30-github-oidc.ps1 -Env staging
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [string]$GitHubRepo = 'Niroshana-SinharaRalalage/vrbook',
    [string]$Branch,
    [string]$GitHubEnvironment
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

if (-not $Branch) {
    $Branch = if ($Env -eq 'prod') { 'main' } else { 'develop' }
}
if (-not $GitHubEnvironment) {
    $GitHubEnvironment = $Env
}

$state = Read-State -Env $Env
if (-not $state.uamiName) {
    throw "No UAMI in state file for env=$Env. Run 00-foundation.ps1 first."
}

Write-Step "GitHub OIDC federation -- $Env (uami: $($state.uamiName))"
Assert-AzLogin -TenantId $state.tenantId -SubscriptionId $state.subscriptionId

# ---- 1. Branch-based federated credential (for push-triggered workflows) ----
Write-Step "1/3 Branch federation: ref:refs/heads/$Branch"
$branchCredName = "github-$($GitHubRepo.Replace('/','-'))-branch-$Branch"
$existing = az identity federated-credential list `
    -g $state.resourceGroup -n $state.uamiName -o json `
    | ConvertFrom-Json `
    | Where-Object { $_.name -eq $branchCredName }

if ($existing) {
    Write-Skip "Federated credential '$branchCredName' already exists"
} else {
    $body = @{
        name = $branchCredName
        issuer = 'https://token.actions.githubusercontent.com'
        subject = "repo:${GitHubRepo}:ref:refs/heads/$Branch"
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json -Compress
    $body | az identity federated-credential create `
        -g $state.resourceGroup `
        -n $state.uamiName `
        --name $branchCredName `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject "repo:${GitHubRepo}:ref:refs/heads/$Branch" `
        --audiences 'api://AzureADTokenExchange' | Out-Null
    Write-Ok "Created"
}

# ---- 2. GitHub Environment federated credential (for cd-prod gated runs) ----
Write-Step "2/3 Environment federation: environment:$GitHubEnvironment"
$envCredName = "github-$($GitHubRepo.Replace('/','-'))-env-$GitHubEnvironment"
$existing = az identity federated-credential list -g $state.resourceGroup -n $state.uamiName -o json `
    | ConvertFrom-Json `
    | Where-Object { $_.name -eq $envCredName }
if ($existing) {
    Write-Skip "Federated credential '$envCredName' already exists"
} else {
    az identity federated-credential create `
        -g $state.resourceGroup `
        -n $state.uamiName `
        --name $envCredName `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject "repo:${GitHubRepo}:environment:$GitHubEnvironment" `
        --audiences 'api://AzureADTokenExchange' | Out-Null
    Write-Ok "Created"
}

# ---- 3. Subscription-level RBAC: grant the UAMI Contributor on the subscription ----
# (Contributor scope can be tightened to the RG after first deploy; subscription scope
# is required for `az deployment sub create`.)
Write-Step "3/3 Subscription RBAC: Contributor on $($state.subscriptionId)"
$subScope = "/subscriptions/$($state.subscriptionId)"
$has = az role assignment list --assignee $state.uamiPrincipalId --scope $subScope --role 'Contributor' -o json | ConvertFrom-Json
if ($has.Count -eq 0) {
    az role assignment create `
        --assignee-object-id $state.uamiPrincipalId `
        --assignee-principal-type ServicePrincipal `
        --role 'Contributor' `
        --scope $subScope | Out-Null
    Write-Ok "Granted Contributor (will tighten to RG scope after first Bicep deploy)"
} else {
    Write-Skip "Already has Contributor"
}

# Also grant User Access Administrator on the RG so it can create role assignments
# (Bicep modules attach Container Apps → KV via assignments).
$rgScope = "/subscriptions/$($state.subscriptionId)/resourceGroups/$($state.resourceGroup)"
$has = az role assignment list --assignee $state.uamiPrincipalId --scope $rgScope --role 'User Access Administrator' -o json | ConvertFrom-Json
if ($has.Count -eq 0) {
    az role assignment create `
        --assignee-object-id $state.uamiPrincipalId `
        --assignee-principal-type ServicePrincipal `
        --role 'User Access Administrator' `
        --scope $rgScope | Out-Null
    Write-Ok "Granted User Access Administrator on $($state.resourceGroup)"
} else {
    Write-Skip "Already has User Access Administrator on $($state.resourceGroup)"
}

Write-Step "Done."
Write-Host ""
Write-Host "Now set these GitHub repository secrets at:" -ForegroundColor Yellow
Write-Host "  https://github.com/$GitHubRepo/settings/secrets/actions"
Write-Host ""
Write-Host "  AZURE_CLIENT_ID       = $($state.uamiClientId)" -ForegroundColor Cyan
Write-Host "  AZURE_TENANT_ID       = $($state.tenantId)" -ForegroundColor Cyan
Write-Host "  AZURE_SUBSCRIPTION_ID = $($state.subscriptionId)" -ForegroundColor Cyan
Write-Host "  ACR_NAME              = crvrbook$Env" -ForegroundColor Cyan
Write-Host ""
if ($Env -eq 'prod') {
    Write-Host "Also create GitHub Environments 'production-soak' (wait_timer=1440) and" -ForegroundColor Yellow
    Write-Host "'production' (require reviewers). See cd-prod.yml comments." -ForegroundColor Yellow
} else {
    Write-Host "Push to '$Branch' to trigger cd-$Env.yml." -ForegroundColor Yellow
}
