#!/usr/bin/env pwsh
# scripts/verify-ci.ps1
#
# Blocks until the most-recent push's GitHub Actions run finishes. Exits 0
# on success. Authored as Slice OPS.M.10.2 F-1 to enforce "fix shipped vs
# fix worked" — `git push` returning 0 is not the same as CI being green;
# this script proves the latter.

$ErrorActionPreference = 'Stop'

$sha = (git rev-parse HEAD).Trim()
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
Write-Host "Watching CI for $branch @ $sha ..." -ForegroundColor Cyan

function Get-RunId {
    $runs = gh run list --branch $branch --limit 5 --json databaseId,headSha,status,conclusion `
        | ConvertFrom-Json
    $match = $runs | Where-Object { $_.headSha -eq $sha } | Select-Object -First 1
    if ($match) { return $match.databaseId } else { return $null }
}

$runId = Get-RunId
$retries = 0
while (-not $runId -and $retries -lt 8) {
    Write-Host "  No run yet for $sha. Waiting 15s ..." -ForegroundColor DarkGray
    Start-Sleep 15
    $runId = Get-RunId
    $retries++
}
if (-not $runId) {
    Write-Host "FAIL: no GitHub Actions run found for $sha after 2 minutes." -ForegroundColor Red
    exit 1
}

Write-Host "Run id: $runId. Streaming ..." -ForegroundColor Cyan
gh run watch $runId --exit-status
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: CI run $runId did not succeed. Inspect with: gh run view $runId --log-failed" -ForegroundColor Red
    exit 1
}
Write-Host "GREEN. CI run $runId succeeded." -ForegroundColor Green
exit 0
