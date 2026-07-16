# Runbook — Pre-launch security hardening checklist (VRB-307)

> Sign this before cutover. Each item names its **evidence** or the **workflow/
> code** that enforces it. Items marked **⏳ prod-arm** are authored now but
> activate with the prod Front Door (VRB-301) — deferred, not dropped.

## Config & auth

- [x] **Fail-fast config validation (G5).** Missing/malformed Entra config makes
      Staging/Production **crash on boot** instead of silently serving
      unauthenticated. Enforced by `.ValidateDataAnnotations().ValidateOnStart()`
      in `src/VrBook.Api/Configuration/ConfigValidationExtensions.cs` (Entra +
      CORS bound + validated). **Closed by VRB-200** — cited here, no new work.
- [ ] **No dev-auth carve-out reachable in prod.** The dev-loopback path is
      Development-only; confirm `ASPNETCORE_ENVIRONMENT=Production` on the prod
      API and that no `[AllowAnonymous]` test backdoor exists (guarded by
      `OpsOps2_AdminSurfaceAndTestBackdoorTests`).
- [ ] **RLS enforced.** Per-module `OpsM9_*_RlsPolicies` migrations applied;
      `TenantGucCommandInterceptor` stamps `app.tenant_id` per command; bypass is
      allowlisted + arch-tested. Verify no policy was dropped in the release.
- [ ] **No `TODO: production` / debug shortcuts** in the shipped diff
      (`grep -rniE "TODO:? *production|FIXME:? *prod" src web infra`).

## Scanning (all three classes)

- [x] **Secret scan (gitleaks) — BLOCKING.** `ci.yml` `secret-scan` job runs
      gitleaks on every PR with `.gitleaks.toml` (default ruleset + a tight
      placeholder/fixture allowlist). A committed secret stops the PR.
- [ ] **GitHub-native secret scanning + push protection — OWNER action.**
      Belt-and-suspenders on top of the gitleaks gate. Repo-admin toggle:
      **Settings → Code security → Secret scanning = Enable, Push protection =
      Enable**. Non-blocking for this story; owner enables at the repo level.
- [x] **Dependency scan — informational (first).** `ci.yml` `dependency-scan`
      job: `dotnet list package --vulnerable --include-transitive` + `npm audit
      --audit-level=high`. Complements Dependabot (`.github/dependabot.yml`).
      Flip to **blocking** after a clean triage.
- [ ] **Image scan (Trivy) triaged.** `cd-staging-{api,web}.yml` scan both
      `vrbook-api` + `vrbook-web` images (informational); review the SARIF and
      ensure **no un-triaged HIGH/CRITICAL-fixable** — suppress accepted findings
      in `.trivyignore` with a reason. One-time pre-launch review.
- [ ] **ZAP baseline triaged.** Run `security-zap.yml` against the near-final
      surface; commit the suppression baseline; **no un-triaged HIGH**. See
      `docs/runbooks/zap-baseline.md`. ⏳ needs the near-final (prod-ish) surface.

## Edge / WAF

- [x] **WAF policy + managed rules authored.** Front Door WAF
      (`infra/modules/front-door.bicep`) with `Microsoft_DefaultRuleSet` 2.1 and a
      **per-client-IP rate-limit rule** (`RateLimitRule`, default **100 req/min**,
      `rateLimitThreshold` param).
- [x] **WAF starts in Detection.** `wafMode` param defaults to **Detection**
      (log-only) so it can't false-positive-block real traffic on day one.
- [ ] **⏳ prod-arm:** enable Front Door on prod (`frontDoorEnabled=isProd`,
      VRB-301), observe a clean window, then **flip `wafMode` → Prevention**
      (`param wafMode=Prevention` in `prod.bicepparam`) and prove the rate limit
      returns **429** with a synthetic request storm. This is the VRB-301/prod
      cutover step.
- [ ] **⏳ prod-arm — Premium SKU for managed rules.** The WAF policy carries
      `Microsoft_DefaultRuleSet`, but managed rule sets require
      **`Premium_AzureFrontDoor`** (Standard rejects them — see `main.bicep`
      Front-Door note). Bump the Front Door profile **and** WAF policy SKU to
      Premium at prod stand-up (the rate-limit custom rule works on either SKU,
      so it is unaffected). Tracked with VRB-301.
- [ ] **⏳ Later pass:** a tighter rate-limit scoped to auth/login paths
      (brute-force) — noted by the TL, not a launch blocker.

## Observability

- [x] WAF blocked-request + rate-limit 429 signals belong on the security
      dashboard; alert on a WAF-block spike (VRB-306 dashboard/alerts substrate).
      ⏳ populated once the WAF is armed on prod.

---

**Sign-off:** _________________  **Date:** __________  (all non-⏳ boxes checked;
⏳ items tracked to VRB-301/prod cutover.)
