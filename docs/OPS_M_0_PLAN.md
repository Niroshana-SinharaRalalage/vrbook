# OPS.M.0 ≡ OPS.7 — Entra External ID cutover (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-24.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row 5 ("OPS.M.0 ≡ OPS.7 — Entra External ID cutover").
**REPLAN reference**: `docs/REPLAN.md` line 459 ("OPS.7 (Entra) is a prerequisite for OPS.M.2 and folds into the OPS.M critical path.").
**ADR reference**: `docs/adr/0012-entra-external-id-over-b2c.md`.
**Sequence**: After Slice 7 ships, before OPS.M.1.
**Estimate**: 2 days. Hard prerequisite for OPS.M.2.

This plan is the contract. It is intentionally short because most of the code is already there.

---

## 1. What is already shipped (do not re-architect)

**Backend auth — done in code, awaiting config**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs:35-62` registers JwtBearer against `EntraExternalId:Instance/TenantId/ClientId` when all three are set. Authority + issuer mismatch (friendly subdomain vs `{tenantId}.ciamlogin.com`) is already handled via `ValidIssuers = new[] { discoveryAuthority, actualIssuer }`. Clock skew 2 min, audience asserted, lifetime asserted, HTTPS metadata required.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AuthExtensions.cs:33,64-72` registers DevAuth in parallel when `DevAuth:AllowAnonymous=true`. **Both schemes coexist by design.** Default scheme is DevAuth-when-enabled, JwtBearer otherwise. The cutover is therefore additive: switching on Entra keys does not break DevAuth, and disabling DevAuth in prod is one env-var change.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs:18-65` provisions users idempotently on every authenticated request, keyed on `oid`. Reads claims by name (`oid`, `emails`, `email`, `email_verified`, `name`, `extension_isOwner`, `extension_isAdmin`) — claim names match what Entra External ID emits per ADR-0012.
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserCommand.cs:11-22` accepts `B2CObjectId` as `string`. The field name is a leftover from the pre-pivot design (ADR-0012); the type and behaviour are correct for Entra. The rename is deferred to OPS.M.1.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs:42-44` reads `oid` or `nameidentifier`. `IsOwner` / `IsAdmin` check the `extension_isOwner` / `extension_isAdmin` claims AND `Role` claims (DevAuth uses Role; Entra uses extension). Both paths covered.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs:88-132` — synthetic auth with three personas (Owner/Guest/Admin). Cookie-driven (`vrbook-dev-persona`). Stays in place; production sets `DevAuth:AllowAnonymous=false` to disable.
- `src/VrBook.Api/appsettings.json:30-34` declares `EntraExternalId { Instance, TenantId, ClientId }` — empty defaults.

**Infra — KV slots declared, secret seeds missing**:
- `infra/main.bicep:263-266` injects `EntraExternalId__Instance / __TenantId / __ClientId` env vars into the API container as `secretRef`s.
- `infra/main.bicep:308-310` declares the matching `secrets` descriptors `entra-instance / entra-tenant-id / entra-api-client-id`.
- `infra/main.bicep:269` sets `DevAuth__AllowAnonymous = isProd ? 'false' : 'true'`. **Production is already wired to disable DevAuth.** Staging keeps DevAuth on.
- **Gap**: `infra/scripts/10-store-secrets.ps1` has NO seed for `entra-instance / entra-tenant-id / entra-api-client-id`. First Bicep deploy after the Entra cutover would fail at container-start time with "Key Vault secret not found". `signalr-cs` solves the same shape via `Set-KvSecret -Name 'signalr-cs' -Value 'pending-bicep-deploy'`; we mirror that pattern in C1.

**Web auth — partly wired, three real bugs**:
- `web/package.json` confirms `@azure/msal-browser 3.27.0` + `@azure/msal-react 2.2.0`.
- `web/src/lib/auth/msalConfig.ts:16-17` reads `NEXT_PUBLIC_B2C_AUTHORITY` + `NEXT_PUBLIC_B2C_CLIENT_ID` — the **ADR-0012 frontend rename was not applied**. Must become `NEXT_PUBLIC_ENTRA_AUTHORITY` + `NEXT_PUBLIC_ENTRA_CLIENT_ID`.
- `web/src/lib/auth/msalConfig.ts:65` defines `apiScopes = ['${clientId}/.default']` where `clientId` is the SPA app (`vrbook-web`). **This mints a token for the SPA itself, not the API** — every authenticated API call returns 401 with audience mismatch. This is the load-bearing bug. Must become `["api://vrbook/access_as_user"]` (matches the identifier URI + scope value declared in `docs/identity/setup.md` §3).
- `web/src/types/env.d.ts:8-9` still types `NEXT_PUBLIC_B2C_*`. Updated in the same commit as `msalConfig.ts`.
- `web/.env.local.example:6-7` still ships `NEXT_PUBLIC_B2C_*` defaults. Updated.
- `web/README.md` lines 45-46 reference `NEXT_PUBLIC_B2C_*`. Updated.
- `web/src/app/auth/callback/page.tsx` (Slice 2) and `web/src/app/auth/signout/page.tsx` are provider-agnostic. **No change.**
- `web/src/lib/auth/useAuth.ts` reads `account.idTokenClaims.extension_isOwner` / `extension_isAdmin` — Entra External ID emits these stripped to the short form per ADR-0012 / `docs/identity/README.md`. No change.

**Web infra — env vars not piped through Bicep at all**:
- `infra/main.bicep:477-483` defines `webEnvVars` with only `NEXT_PUBLIC_API_BASE_URL`. No `NEXT_PUBLIC_ENTRA_*` values reach the container. Because `NEXT_PUBLIC_*` are baked into the JS bundle at `next build` time, the values must reach the **build** (via Docker `--build-arg`), not just the runtime container. Plan adds these as Bicep env vars (for SSR + server components) AND as CI build-args (for the bundle).

**Tests**:
- `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs:56-58` blanks `EntraExternalId:*` so `AuthExtensions.cs` skips JwtBearer registration (otherwise it would fetch the discovery document over the network during test startup). **Stays as-is.** Integration tests will always run on DevAuth.

**Runbook**:
- `docs/identity/setup.md` — 280 lines, end-to-end provisioning runbook. Covers tenant creation, two app registrations, user-flow creation, KV write, self-bootstrap, troubleshooting. **Authoritative; do not duplicate; do not rewrite.**
- `docs/identity/README.md` — provider-level overview. **Authoritative.**
- `infra/scripts/README.md:22-30` already lists "Portal-only — create Entra External tenant" as step 2 of the per-env runbook.
- `infra/scripts/grant-self-admin.ps1` exists and works. Reads `entraTenantDomain` + `entraApiAppId` from `infra/.state/<env>.json`.

---

## 2. Decisions (architect-locked)

### 2.1 Tenant provisioning — **runbook + portal, no Bicep**

As of June 2026, no Bicep namespace creates Entra External tenants. The `Microsoft.Graph/applications@v1.0` extension exists for app registrations but requires the Graph Bicep extension installed per-contributor, which adds friction for two tenants (staging + prod-later). The existing `docs/identity/setup.md` runbook is end-to-end and idempotent against the Graph API. We keep it.

OPS.M.0 adds a thin ops checklist (`docs/runbooks/entra-external-id-setup.md`) that wraps the existing setup.md with go/no-go gates and the order operational steps must run in between code commits.

### 2.2 Cutover model — **additive; A/B in staging; flip in prod**

`AuthExtensions.cs:33-62` registers both DevAuth and JwtBearer simultaneously when configured. Cutover is therefore:

1. Set `EntraExternalId:*` in staging KV. DevAuth stays on. **No behaviour change**; JwtBearer is registered but DevAuth wins as default scheme.
2. Operationally smoke-test a real sign-up + sign-in via the user flow; verify `/api/v1/me` returns 200 with the right claims.
3. (Production cutover, separate task — see Phase 2 note below) Set `EntraExternalId:*` AND `DevAuth__AllowAnonymous=false` in prod via the Bicep ternary `isProd ? 'false' : 'true'` (already in place at `infra/main.bicep:269`). Prod has no A/B window — DevAuth is off as soon as `env=prod` is deployed.

Staging keeps DevAuth on throughout OPS.M and is only flipped off once the multi-tenancy slice ships AND a real-user smoke test passes.

### 2.3 Web app build vs runtime env vars — **build-args for `NEXT_PUBLIC_*`; runtime env for SSR fallback**

`NEXT_PUBLIC_*` are inlined into the client bundle at `next build`. Changing the Entra tenant therefore requires a **CI rebuild** of the web Docker image, not just a Container App env-var swap.

Plan:
- Add `NEXT_PUBLIC_ENTRA_AUTHORITY` + `NEXT_PUBLIC_ENTRA_CLIENT_ID` to `infra/main.bicep`'s `webEnvVars` block — these are picked up by Next.js's server-side code paths (server components, middleware) but NOT by the client bundle, which was sealed at build time.
- Update the CI workflow `.github/workflows/cd-staging.yml` (and the equivalent prod workflow) to pass these as `--build-arg` to the `docker build` step that produces the web image. The workflow already reads from KV for other secrets; adding two more is one extra `az keyvault secret show` call per build.
- Document this in the runbook: changing the Entra tenant requires a CI rebuild, not a hot-swap.

### 2.4 MSAL scope fix — **`api://vrbook/access_as_user`**

The current `apiScopes = ['${clientId}/.default']` in `msalConfig.ts:65` requests `vrbook-web/.default`. This mints a token for the SPA, not the API. Per `docs/identity/setup.md` §3, the API's `identifierUris` is `api://vrbook` and the exposed scope value is `access_as_user`. The MSAL request scope is therefore `api://vrbook/access_as_user`.

The fix:
```typescript
export const apiScopes: string[] = ['api://vrbook/access_as_user'];
```

This is constant — it depends on the API's identifier URI, not the web client id. No env var needed.

### 2.5 KV secret seeds — **mirror the `signalr-cs` placeholder pattern in `10-store-secrets.ps1`**

`signalr-cs` ships as `'pending-bicep-deploy'` from `10-store-secrets.ps1:73` so the first Bicep deploy can resolve the `secretRef`. Then a real value is written out-of-band (by the SignalR Bicep module, per Slice 7).

Entra is different: the real value is written by the operator running `docs/identity/setup.md` §7. To prevent first-deploy failure before §7 has been run, we add three placeholders. The setup runbook overwrites them on every re-run.

### 2.6 Prod tenant — **deferred; same runbook re-run with `-Env prod`**

The plan creates only a staging External tenant. A prod tenant is a separate cutover task that lives in the OPS.M close-out (after OPS.M.10 ships). Cost of deferring: zero — the runbook is parameterised on `$Env`. Cost of doing it now: 30-minute tenant provision + portal click-through, plus risk of mis-clicking the prod tenant's MFA policy. Defer until prod cutover is a real event with real users.

### 2.7 Runbook split — **three new runbooks, all thin**

- `docs/runbooks/entra-external-id-setup.md` — operational ordering wrapper around `docs/identity/setup.md`. ~50 lines. Lists go/no-go gates between portal steps.
- `docs/runbooks/entra-key-rotation.md` — covers Microsoft-rotated JWKS signing keys (auto via OIDC discovery; 24h discovery TTL; manual API restart to flush). Forward-compat section for confidential-client rotation (currently N/A — SPA is public-client).
- `docs/runbooks/entra-incident-triage.md` — "users can't sign in" decision tree. Maps AADSTS codes to fixes.

---

## 3. Commit split (3 commits + operational tasks between them)

### C1 — Web MSAL config rename + scope fix + Bicep web env vars + KV seed placeholders

**Web — code**:
- `web/src/lib/auth/msalConfig.ts` — edit:
  - Rename `NEXT_PUBLIC_B2C_AUTHORITY` → `NEXT_PUBLIC_ENTRA_AUTHORITY`.
  - Rename `NEXT_PUBLIC_B2C_CLIENT_ID` → `NEXT_PUBLIC_ENTRA_CLIENT_ID`.
  - Change `apiScopes` to `['api://vrbook/access_as_user']` (§2.4).
  - Update the docstring to reference Entra External ID (not B2C).
  - Keep the `knownAuthorities` array (Entra External ID also requires it); update the comment.
- `web/src/types/env.d.ts` — rename two declarations to match.
- `web/.env.local.example` — rename two example lines.
- `web/README.md` — update the env-var table (lines 45–46) to reference Entra.

**Web — infra**:
- `infra/main.bicep` — edit `webEnvVars` at lines 477–483:
  ```bicep
  { name: 'NEXT_PUBLIC_ENTRA_AUTHORITY', secretRef: 'entra-web-authority' }
  { name: 'NEXT_PUBLIC_ENTRA_CLIENT_ID', secretRef: 'entra-web-client-id' }
  ```
- `infra/main.bicep` — wire the `secrets` block on the `webApp` module so the two secretRefs resolve. Today line 503 is `secrets: []` — replace with two entries pointing at the same KV secret names.
- Add corresponding KV secret descriptors. The API already has `entra-instance / entra-tenant-id / entra-api-client-id`; web needs `entra-web-authority` (the full `https://{tenant}.ciamlogin.com/{tenantId}/v2.0` URL — distinct from `entra-instance` which is only the `https://{tenant}.ciamlogin.com` prefix) and `entra-web-client-id` (the `vrbook-web` app's appId — distinct from `entra-api-client-id`).

**CI workflow**:
- `.github/workflows/cd-staging.yml` — add two `az keyvault secret show` calls fetching `entra-web-authority` + `entra-web-client-id`; pass them to the `docker build` step for the web image as `--build-arg NEXT_PUBLIC_ENTRA_AUTHORITY=...` etc.
- Verify the Dockerfile under `web/Dockerfile` accepts `ARG NEXT_PUBLIC_ENTRA_AUTHORITY` + `ARG NEXT_PUBLIC_ENTRA_CLIENT_ID` and re-exports them as `ENV` before `npm run build`.

**Infra — KV placeholders**:
- `infra/scripts/10-store-secrets.ps1` — add after the existing seeds, mirroring the `signalr-cs` pattern:
  ```powershell
  Set-KvSecret -Name 'entra-instance'        -Value 'pending-identity-setup'
  Set-KvSecret -Name 'entra-tenant-id'       -Value 'pending-identity-setup'
  Set-KvSecret -Name 'entra-api-client-id'   -Value 'pending-identity-setup'
  Set-KvSecret -Name 'entra-web-authority'   -Value 'pending-identity-setup'
  Set-KvSecret -Name 'entra-web-client-id'   -Value 'pending-identity-setup'
  ```
- Update `docs/identity/setup.md` §7 to add the two new web secrets (`entra-web-authority` + `entra-web-client-id`) to the existing `az keyvault secret set` block.
- `infra/scripts/README.md` — update the state-file row to add `entraWebAuthority` to the list of keys persisted (already covers `entraWebAppId`).

**Tests** (one negative test, no integration):
- No integration test changes — `IdentityApiFixture` already blanks the Entra keys and runs DevAuth-only.
- Vitest unit test: `web/src/lib/auth/msalConfig.test.ts` — asserts `apiScopes[0] === 'api://vrbook/access_as_user'` so a future regression to `${clientId}/.default` fails CI.

**Acceptance**: `dotnet build` + `cd web && npm run typecheck && npm run lint && npm test` all green. `azd up -e staging` succeeds against the placeholder seeds (Bicep deploys, container apps start, DevAuth still wins — no behaviour change yet).

---

### Operational tasks between C1 and C2 (NOT git commits)

Run in this order. Each step has a verification gate.

1. **Provision the Entra External tenant** — portal-only, ~30 min. Follow `docs/identity/setup.md` §1. Wait for tenant to appear in **Entra admin center → Manage tenants** with type "External".
2. **Switch CLI session to the External tenant** — `az login --tenant vrbookcid.onmicrosoft.com --allow-no-subscriptions`. Verify `az account show` returns the External tenant GUID.
3. **Register `vrbook-api`** — `docs/identity/setup.md` §3. Capture the `appId` (this is `EntraExternalId__ClientId` later). Verify `az ad app show --id $apiAppId --query identifierUris` returns `["api://vrbook"]`.
4. **Register `vrbook-web`** — `docs/identity/setup.md` §4. Capture the `appId`. Verify the redirect URIs include both `http://localhost:3000/auth/callback` (for dev) and `https://ca-vrbook-web-staging.<cae-domain>/auth/callback` (for the deployed Container App — get the domain from `azd env get-values | grep webFqdn`).
5. **Create the SignUpAndSignIn user flow** — portal-only, `docs/identity/setup.md` §5. Verify the application claims list includes both `extension_<api-appid>_isOwner` and `_isAdmin` (these only appear after §3 has been run).
6. **Associate the user flow with `vrbook-web`** — `docs/identity/setup.md` §6.
7. **Write real values to staging KV** — switch back to workforce tenant (`az login --tenant <workforce-tenant>`); follow `docs/identity/setup.md` §7. Five secrets get written (the three API secrets + the two new web ones from C1). Verify with `az keyvault secret show --vault-name kv-vrbook-staging --name entra-instance --query value`.
8. **Update `infra/.state/staging.json`** — add `entraTenantId`, `entraTenantDomain`, `entraInstance`, `entraApiAppId`, `entraWebAppId`. Required by `grant-self-admin.ps1`.
9. **Trigger a CI build of the web image** — push a no-op commit OR run the staging deploy workflow manually. The new MSAL build args are baked into the bundle.
10. **Restart the API container** — `az containerapp revision restart -n ca-vrbook-api-staging -g rg-vrbook-staging`. Forces JwtBearer to re-read `EntraExternalId:*` from the freshly-rotated KV values.
11. **Sign up as the first user** — open `https://ca-vrbook-web-staging.<domain>/`, click sign-in, complete the SignUpAndSignIn flow with an email you control (default: `niroshanaks@gmail.com`).
12. **Bootstrap yourself as Owner+Admin** — `.\infra\scripts\grant-self-admin.ps1 -Env staging -UserEmail niroshanaks@gmail.com`. Sign out + back in.

**Go/no-go gate before C2**: hit `/api/v1/me` with the Entra-issued bearer token; expect 200 with `isOwner: true, isAdmin: true`. If 401 with `IDX10503 signature validation failed`, recheck `EntraExternalId__Instance` matches `https://vrbookcid.ciamlogin.com`. If 401 with `audience invalid`, the `apiScopes` MSAL fix from C1 didn't make it into the build — check the CI build-arg propagation.

---

### C2 — Three runbooks + ADR-0012 follow-up note

**Runbooks**:
- `docs/runbooks/entra-external-id-setup.md` — new (~60 lines):
  - Title + status header consistent with other runbooks.
  - "When to use this runbook" — first-time provisioning per env.
  - Pointer to `docs/identity/setup.md` for the detailed step-by-step (do not duplicate).
  - 12-step operational checklist (the one between C1 and C2 above).
  - Go/no-go gate definition.
  - Rollback: if cutover fails, set `EntraExternalId__*` to empty in KV (or delete the secrets); JwtBearer registration silently skips; DevAuth continues to win.

- `docs/runbooks/entra-key-rotation.md` — new (~80 lines):
  - **Microsoft-rotated JWKS signing keys** (the primary case): Entra rotates the signing key roughly every 24h. ASP.NET Core's `JwtBearerOptions.Authority` fetches the OIDC discovery document and JWKS lazily, with a 24h cache TTL by default. **No operator action required for routine rotations.**
  - **Manual flush procedure**: when troubleshooting a "tokens minted in the last 5 min are failing" scenario, restart the API container apps to flush the in-memory JWKS cache: `az containerapp revision restart -n ca-vrbook-api-<env> -g rg-vrbook-<env>`.
  - **Confidential-client secret rotation** (forward-compat — currently N/A because SPA is public-client): if VrBook moves any server-to-Entra calls to confidential-client flow in the future, document `az ad app credential reset` here. Not used today.
  - **App registration certificate rotation**: N/A for the SPA. The API does not authenticate to Entra directly; it only validates tokens.
  - **Verification**: after rotation or restart, hit `/api/v1/me` with a fresh sign-in. Expect 200.

- `docs/runbooks/entra-incident-triage.md` — new (~120 lines):
  - "Users can't sign in" decision tree.
  - Symptom → cause → fix mapping for common AADSTS codes:
    - `AADSTS50011: redirect URI mismatch` → check `vrbook-web` app's `web-redirect-uris` matches the actual web container's FQDN.
    - `AADSTS500011: resource principal not found` → `api://vrbook` identifier URI missing on `vrbook-api` app.
    - `AADSTS65001: consent required` → re-run `az ad app permission admin-consent --id $webAppId`.
    - `AADSTS700016: application not found in directory` → wrong tenant; check `EntraExternalId__TenantId` matches the External tenant GUID.
    - `IDX10503: signature validation failed` → JWKS cache stale OR `EntraExternalId__Instance` misconfigured.
    - 401 with `audience invalid` → MSAL scope wrong; should be `api://vrbook/access_as_user`.
    - 401 immediately after deploy, all users → KV secret `entra-instance` is still `'pending-identity-setup'`; re-run setup.md §7.
  - "Single user can't sign in" branch — extension claims missing → re-run `grant-self-admin.ps1`.
  - "Everyone's tokens expired at once" branch → tenant outage; check Microsoft Service Health.

**ADR follow-up**:
- `docs/adr/0012-entra-external-id-over-b2c.md` — append a new section "Migration impact (2026-06 follow-up)":
  - Confirm what `ProvisionUserCommand.B2CObjectId` rename is deferred to OPS.M.1 (low-priority cosmetic).
  - Document that the web-side `NEXT_PUBLIC_B2C_*` rename was missed in the original ADR and applied in OPS.M.0.
  - Document the MSAL `apiScopes` fix as a separate bug found during this cutover.
  - Confirm tenant deletion takes 90 days (soft-delete) — operationally relevant if a tenant is provisioned in error.

**Acceptance**: `markdownlint docs/runbooks/entra-*.md` green. Cross-links from `docs/runbooks/README.md` updated to list the three new runbooks. Cross-link from `docs/identity/README.md` to the setup runbook + the triage runbook.

---

### C3 — Production cutover guard (env-file edits + verification of cutover scripting)

This commit is small — it locks in the production environment file so OPS.M downstream slices can rely on `DevAuth__AllowAnonymous=false` in prod.

- `infra/main.bicep:269` — verified: already `isProd ? 'false' : 'true'`. **No change**; we record the verification in this commit's message.
- `docs/runbooks/entra-external-id-setup.md` — add a "Production cutover (separate task)" subsection:
  - Listed prerequisites: prod tenant exists, prod KV has real values for all five `entra-*` secrets, prod web image rebuilt with prod `NEXT_PUBLIC_ENTRA_*` build args.
  - Listed deploy: `azd up -e prod` deploys with `DevAuth__AllowAnonymous=false` automatically.
  - Listed verification: hit `https://api.vrbook.example.com/api/v1/me` with the Entra-issued prod token; expect 200.
  - Listed rollback: re-deploy with `EntraExternalId__*` blanked in prod KV. JwtBearer skips registration; default scheme becomes JwtBearer with no validator → 401 for everyone. **Note: there is no "fall back to DevAuth in prod" path because the Bicep ternary forces `DevAuth__AllowAnonymous=false` in prod.** Rollback for prod means rolling back the deployment, not flipping a flag.
- `docs/runbooks/README.md` — register the three new runbooks under a new "Identity" section if one doesn't exist, otherwise append.

**Acceptance**: `azd up -e staging --what-if` shows no changes (the env file edits don't affect staging). Manual verification: re-read the cutover sequence end-to-end.

---

## 4. Concrete file additions

### Web — config
- `web/src/lib/auth/msalConfig.ts` — **edit** (rename env vars, fix `apiScopes`).
- `web/src/types/env.d.ts` — **edit** (rename two declarations).
- `web/.env.local.example` — **edit** (rename two lines).
- `web/README.md` — **edit** (env-var table).
- `web/src/lib/auth/msalConfig.test.ts` — **new** (Vitest; locks the scope value).

### Infra — Bicep + scripts
- `infra/main.bicep` — **edit** (`webEnvVars` + `webApp` secrets block + two new KV secret descriptors).
- `infra/scripts/10-store-secrets.ps1` — **edit** (five `entra-*` placeholder seeds).
- `infra/scripts/README.md` — **edit** (state-file key list).

### Docs — runbooks
- `docs/runbooks/entra-external-id-setup.md` — **new** (~60 lines; ops wrapper around `docs/identity/setup.md`).
- `docs/runbooks/entra-key-rotation.md` — **new** (~80 lines).
- `docs/runbooks/entra-incident-triage.md` — **new** (~120 lines).
- `docs/runbooks/README.md` — **edit** (register three new runbooks).
- `docs/identity/README.md` — **edit** (cross-link to setup + triage runbooks).
- `docs/identity/setup.md` — **edit** (§7 adds the two new web secrets `entra-web-authority` + `entra-web-client-id`).
- `docs/adr/0012-entra-external-id-over-b2c.md` — **edit** (append follow-up note).

### CI
- `.github/workflows/cd-staging.yml` — **edit** (fetch `entra-web-authority` + `entra-web-client-id` from KV; pass as `--build-arg` to web Docker build).
- `web/Dockerfile` — **edit** (declare `ARG` for the two `NEXT_PUBLIC_ENTRA_*` values; re-export as `ENV` before `npm run build`).

### Code (zero new files)
- No new `.cs` files. No new endpoints. No new domain types. The backend was correct as of ADR-0012; only the web frontend had residual B2C-era code.

---

## 5. Scope-cut order (drop top first when deadline bites)

The 2-day budget is real. Cuts in order:

1. **Prod tenant cutover.** Already deferred by §2.6 — not in this plan. If staging itself slips beyond day 2, ship C1 + C2 only.
2. **`entra-key-rotation.md` runbook.** Lowest-urgency of the three. Microsoft auto-rotates with no operator action; the manual restart procedure is a 3-line note that could live in the incident triage runbook instead. Drop the standalone file; merge the relevant 3 lines into `entra-incident-triage.md`.
3. **`entra-incident-triage.md` runbook full content.** Ship a stub with the top 3 AADSTS codes and a TODO link, expand in a Phase 2 ticket.
4. **Vitest test on MSAL scope.** Drop. The fix is one line and lint is enough to catch regressions if someone reverts.
5. **Bicep `webEnvVars` block.** Drop. Falls back to operator-set Container App env vars (no infra-as-code). Compensated by an extra step in `entra-external-id-setup.md` runbook to set them manually post-deploy. **Not recommended** — defeats reproducibility, but possible if the bicep change is blocking on a separate review.

**Phase-1.5 minimum after all five cuts**: web MSAL scope fix + env-var rename + KV seed placeholders + setup runbook (the operational checklist). Everything else is operationally recoverable.

Never falls: C1 web MSAL scope fix (`api://vrbook/access_as_user`). Without it, no one can call the API even after the cutover.

---

## 6. Out of scope (Phase 2 / future)

- **Prod External tenant provisioning.** Defer to a separate task at prod cutover time. Same runbook re-run with `-Env prod`.
- **`ProvisionUserCommand.B2CObjectId` rename to `EntraObjectId`.** Cosmetic; deferred to OPS.M.1 when the `ICurrentUser` shape is being touched anyway.
- **`SwaggerConfig.cs:32` description "AD B2C bearer token"** — cosmetic; one-line update bundled into OPS.M.1.
- **`web/src/lib/api/devAuth.ts` and `DevPersonaSwitcher.tsx` comments referencing "production Entra auth"** — accurate but pre-cutover; no change required.
- **Federated IdP (Google) at sign-up** — optional; document in setup runbook but do not configure for the OPS.M.0 cutover. Per ADR-0012, Google is for guests; Apple is Phase 2.
- **MFA on Owners in staging.** Off by default (per `docs/identity/setup.md` §5.6). Forced on at prod cutover.
- **`Microsoft.Identity.Web` package migration.** ADR-0012 notes this is a one-line dep swap; stay on vanilla `JwtBearer` per the ADR's negative-consequences §.
- **Per-tenant Entra tenants in multi-tenancy.** OPS.M.0 sets up a single Entra tenant that the whole platform shares (every VrBook tenant signs into the same Entra tenant). Per-tenant Entra tenants are Phase 2 / not planned — `tenant_id` is a VrBook concept, not an Entra one.
- **`HttpCurrentUser.B2CObjectId` rename** — cosmetic; deferred.
- **Adding `entra-*` secret rotation to OPS.6 / OPS.7.** Since the `entra-*` secrets are not VrBook-rotated (they're Microsoft-side identifiers + the `vrbook-api` appId, which doesn't rotate), there's nothing to rotate. The rotation runbook covers only Microsoft-side automatic key rotation.

---

## 7. Verification recipe (end-of-OPS.M.0)

Run after operational tasks 1–12 above are complete.

1. **Build**: `dotnet build` green. `cd web && npm install && npm run typecheck && npm run lint && npm test` green. `grep -r "NEXT_PUBLIC_B2C" web/` returns zero hits.

2. **Staging KV has real values**:
   ```powershell
   az keyvault secret show --vault-name kv-vrbook-staging --name entra-instance      --query value
   az keyvault secret show --vault-name kv-vrbook-staging --name entra-tenant-id     --query value
   az keyvault secret show --vault-name kv-vrbook-staging --name entra-api-client-id --query value
   az keyvault secret show --vault-name kv-vrbook-staging --name entra-web-authority --query value
   az keyvault secret show --vault-name kv-vrbook-staging --name entra-web-client-id --query value
   ```
   None should return `'pending-identity-setup'`.

3. **API container picks up Entra config**: hit `/api/v1/healthz` then check the API container's startup logs — expect to see `Microsoft.AspNetCore.Authentication.JwtBearer` register without error. No `IDX20803: Unable to obtain configuration from` failures.

4. **JWKS discovery works**: in a browser, open `https://vrbookcid.ciamlogin.com/<tenantId>/v2.0/.well-known/openid-configuration`. Expect a 200 JSON document with `jwks_uri`. Open that `jwks_uri` — expect a `{keys: [...]}` document with at least one RSA key.

5. **End-to-end sign-up**:
   - Open `https://ca-vrbook-web-staging.<cae-domain>/`.
   - Click sign-in. Land on `https://vrbookcid.ciamlogin.com/...` (Entra-hosted UI).
   - Complete the SignUpAndSignIn flow with `niroshanaks@gmail.com`.
   - Get redirected back to `/auth/callback`, then `/`.
   - `useAuth().isAuthenticated === true`. `useAuth().user.oid` is a Guid.

6. **First-login provisioning**: hit `/api/v1/me`. Expect 200 with `userId` (the app-side Guid). Verify a row was inserted into `identity.users` with `b2c_object_id` matching the Entra `oid` claim.

7. **Bootstrap as Owner+Admin**: run `.\infra\scripts\grant-self-admin.ps1 -Env staging -UserEmail niroshanaks@gmail.com`. Sign out and back in. Hit `/api/v1/me` — expect `isOwner: true, isAdmin: true`.

8. **Token audience is correct**: decode the access token at `https://jwt.ms` after sign-in. `aud` claim should be the `vrbook-api` appId (`entra-api-client-id` value), NOT the SPA's appId. If `aud` shows the SPA's id, the MSAL `apiScopes` fix from C1 did not propagate — recheck the CI build args.

9. **DevAuth still works in staging**: open `/` in an incognito window. Expect to be auto-signed-in as the Owner persona (DevAuth wins as default scheme when `DevAuth__AllowAnonymous=true`).

10. **DevAuth disabled in prod (verified, no live test yet)**: confirm `infra/main.bicep:269` is `isProd ? 'false' : 'true'`. Confirm `azd up -e prod --what-if` (if you have a prod env wired) shows `DevAuth__AllowAnonymous=false`. The first real prod deploy is gated by the prod-tenant follow-up task.

11. **Negative path — bad token**: hit `/api/v1/me` with `Authorization: Bearer not-a-real-jwt`. Expect 401.

12. **Negative path — wrong audience**: in MSAL Inspector, manually request a token with scope `${webClientId}/.default` (revert to the pre-fix scope). Hit `/api/v1/me` with it. Expect 401 with `audience invalid`. This confirms the audience guard is doing real work.

If steps 5 + 6 + 7 + 8 pass, OPS.M.0 ships and OPS.M.2 can proceed. Step 8 is the critical one; if it fails, the whole cutover is non-functional.

---

## 8. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/OPS_M_0_PLAN.md`.
2. I open C1 — Web MSAL config rename + scope fix + Bicep web env vars + KV seed placeholders.
3. I pause and ask you to run the 12-step operational checklist (portal + CLI work that I cannot do).
4. After step 12's go/no-go passes, I open C2 — three runbooks + ADR-0012 follow-up.
5. I open C3 — production cutover guard documentation. No code change beyond ADR + runbook edits.
6. Each commit ends with: I push, CI runs, I report green/red. OPS.M.0 ends with the verification recipe in §7 passing on staging.

If you reject or want changes: point at the specific decision in §2 or the specific commit in §3; I revise this document and re-submit.

**Open questions surfaced for approval**:
- Confirm Azure subscription access to create the External tenant.
- Confirm we want a NEW staging Entra tenant (default) vs reuse of an existing one.
- Confirm prod tenant is deferred to a separate task at prod cutover time.
- Confirm Google IdP at staging sign-up — yes/no.
- Confirm MFA on staging Owners — default off.
