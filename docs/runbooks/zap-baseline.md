# Runbook — OWASP ZAP baseline scan (OPS.4)

One-time pre-launch passive security scan of the deployed web surface. Compressed from a CI gate to a manual run per [`OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md) §2. Go-live gate: **run complete, every WARN/FAIL triaged, no un-triaged HIGH**.

## When to run

**Late** in the launch sequence — against the near-final deployed surface — so newly-added routes are in scope. Re-run after any material route/UI change before cutover.

## How to run

Actions → `security-zap` → *Run workflow*. Leave `target_url` blank to scan staging web, or pass the near-final/prod-pre-cutover web URL. The workflow warms the target (scale-to-zero) then runs the ZAP baseline (passive spider + passive rules, `-a` includes alpha). It's informational (`fail_action: false`) — the report is the deliverable. Download the ZAP report artifact from the run.

## Triage

For each alert in the report:
- **Fix** it (preferred), or
- **Suppress** with a justified `IGNORE` line in `.zap/rules.tsv` (ruleId + who/when/why), for confirmed false-positives / accepted risk.

Common baseline noise on a Next.js + API surface: missing security headers (CSP/HSTS — decide policy), timestamp/version disclosure (cosmetic), cookie flags. Decide each deliberately; don't blanket-ignore.

## Scope + backlog

The baseline is **passive** (no active attack payloads) and unauthenticated — it covers the public surface. Post-launch backlog: authenticated-context ZAP (session + admin surface) and wiring the baseline as a pre-release CI job. The static attack surface is also covered by `docs/security/threat-model.md`.

## Go-live gate

Go/No-Go: one archived baseline run against the near-final surface, all findings triaged (fixed or justified-IGNORE in `.zap/rules.tsv`), no un-triaged HIGH.
