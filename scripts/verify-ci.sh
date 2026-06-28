#!/usr/bin/env bash
# scripts/verify-ci.sh — Bash mirror of verify-ci.ps1.
set -euo pipefail

SHA=$(git rev-parse HEAD)
BRANCH=$(git rev-parse --abbrev-ref HEAD)
printf 'Watching CI for %s @ %s ...\n' "$BRANCH" "$SHA"

find_run() {
  gh run list --branch "$BRANCH" --limit 5 --json databaseId,headSha \
    | jq -r ".[] | select(.headSha == \"$SHA\") | .databaseId" \
    | head -n1
}

RUN_ID=$(find_run)
RETRIES=0
while [[ -z "$RUN_ID" && $RETRIES -lt 8 ]]; do
  printf '  No run yet for %s. Waiting 15s ...\n' "$SHA"
  sleep 15
  RUN_ID=$(find_run)
  RETRIES=$((RETRIES+1))
done

if [[ -z "$RUN_ID" ]]; then
  printf 'FAIL: no GitHub Actions run found for %s after 2 minutes.\n' "$SHA" >&2
  exit 1
fi

printf 'Run id: %s. Streaming ...\n' "$RUN_ID"
gh run watch "$RUN_ID" --exit-status
printf 'GREEN. CI run %s succeeded.\n' "$RUN_ID"
