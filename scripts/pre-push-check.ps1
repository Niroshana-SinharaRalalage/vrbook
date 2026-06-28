#!/usr/bin/env pwsh
# scripts/pre-push-check.ps1
#
# Runs every check CI runs, locally, before push. Exit 0 = green, push is
# safe. Non-zero = abort push and surface the diagnostic.
#
# Authored as Slice OPS.M.10.2 F-1 (per `docs/OPS_M_10_2_CI_ROOT_CAUSE.md`
# §2.2). Background: the project's `cd-staging-api.yml` workflow runs lint
# (`dotnet format --verify-no-changes`), build (`-c Release`), and tests
# (`--filter "Category!=Integration"`). Local iteration loops kept using
# `Category=Unit` (narrower) and `Debug` build, so test failures only
# surfaced in CI. This script aligns local validation with CI exactly.

$ErrorActionPreference = 'Stop'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

function Step([string]$name, [scriptblock]$body) {
    Write-Host "==> $name" -ForegroundColor Cyan
    $stepSw = [System.Diagnostics.Stopwatch]::StartNew()
    & $body
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: $name (after $($stepSw.Elapsed.TotalSeconds.ToString('0.0'))s)" -ForegroundColor Red
        exit 1
    }
    Write-Host "OK:   $name ($($stepSw.Elapsed.TotalSeconds.ToString('0.0'))s)" -ForegroundColor Green
}

# 1. Docker MUST be running for the testcontainer suite. Without it the
#    `TwoTenantApiFixture` + `TenantIdRolloutFixture` tests silently skip
#    locally and we lose the ~92 tests CI exercises in step 9.
Step "Docker daemon" { docker version --format '{{.Server.Version}}' | Out-Null }

# 2. Restore (same as CI cd-staging-api.yml step 6)
Step "dotnet restore" { dotnet restore src/VrBook.sln }

# 3. Lint (same as CI step 7). THIS is the step that's caught CHARSET / BOM /
#    IDE0011 / import-ordering issues that `dotnet build` doesn't.
Step "dotnet format --verify-no-changes" { dotnet format src/VrBook.sln --verify-no-changes --no-restore }

# 4. Build Release (same as CI step 8). Release config promotes some analyzer
#    rules (S1481 unused locals, S3923 redundant ternary) that Debug doesn't.
Step "dotnet build -c Release" { dotnet build src/VrBook.sln --no-restore --configuration Release }

# 5. Tests with the EXACT CI filter. This is the CI signal — NOT
#    `Category=Unit` which is narrower by ~92 tests and silently green
#    while CI is red.
Step "dotnet test --filter Category!=Integration" {
    dotnet test src/VrBook.sln --no-build --configuration Release `
        --filter "Category!=Integration" `
        --logger "console;verbosity=minimal"
}

# 6. Print summary
Write-Host ""
Write-Host "GREEN. Total: $($sw.Elapsed.TotalSeconds.ToString('0.0'))s." -ForegroundColor Green
Write-Host "Safe to push. After 'git push', run 'pwsh scripts/verify-ci.ps1' to confirm CI green." -ForegroundColor Yellow
exit 0
