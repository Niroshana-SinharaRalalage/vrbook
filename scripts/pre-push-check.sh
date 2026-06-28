#!/usr/bin/env bash
# scripts/pre-push-check.sh — Bash mirror of pre-push-check.ps1 for
# Linux/WSL/Git-bash. See the PowerShell variant for full rationale.

set -euo pipefail
SW=$(date +%s)

step() {
  local name="$1"; shift
  printf '==> %s\n' "$name"
  local t
  t=$(date +%s)
  if "$@"; then
    printf 'OK:   %s (%ds)\n' "$name" "$(( $(date +%s) - t ))"
  else
    printf 'FAIL: %s (after %ds)\n' "$name" "$(( $(date +%s) - t ))" >&2
    exit 1
  fi
}

step "Docker daemon"               docker version --format '{{.Server.Version}}'
step "dotnet restore"               dotnet restore src/VrBook.sln
step "dotnet format --verify"       dotnet format src/VrBook.sln --verify-no-changes --no-restore
step "dotnet build -c Release"      dotnet build src/VrBook.sln --no-restore --configuration Release
step "dotnet test (CI filter)"      \
    dotnet test src/VrBook.sln --no-build --configuration Release \
        --filter "Category!=Integration" --logger "console;verbosity=minimal"

printf '\nGREEN. Total: %ds. Safe to push.\n' "$(( $(date +%s) - SW ))"
printf 'After push, run: bash scripts/verify-ci.sh\n'
