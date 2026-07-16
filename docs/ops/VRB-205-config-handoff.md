# VRB-205 — config handoff (CORS / callback URLs / rate limits / CDN, gap G8)

CONFIG lane delivers the in-lane parts; the DEVOPS/infra pieces are handed to Agent 1 as **exact shapes** (CONFIG does not edit DEVOPS-owned live files). Scope approved by the TL 2026-07-15.

## Delivered by CONFIG (this PR)
1. **CORS** — `CorsOptions` (`Cors:AllowedOrigins`) is now a validated, fail-fast options class (`.ValidateOnStart()` via `AddValidatedConfiguration`, VRB-200 pattern): required + each a valid absolute origin (scheme+host, no trailing slash) in Staging/Production; Development keeps the appsettings localhost default (dev-loopback carve-out). `Program.cs` consumes the validated `CorsOptions` shape.
2. **MSAL redirect/callback URIs** — documented per-env below (no Entra portal changes here).
6. **CDN / cache** — strategy documented below.

## Handed to DEVOPS / infra (Agent 1) — exact shapes

### Item 3 — `NEXT_PUBLIC_API_BASE_URL` from a Bicep output, not the hard-coded FQDN (`cd-staging-web.yml:149`, closes G8)
- **main.bicep** — add an output off the API container app:
  ```bicep
  output apiBaseUrl string = 'https://${apiApp.properties.configuration.ingress.fqdn}/api/v1'
  ```
  (use whatever the API Container App resource symbol is; `.properties.configuration.ingress.fqdn` is the stable FQDN.)
- **cd-staging-web.yml** — replace the literal `ca-vrbook-api-staging.<...>.azurecontainerapps.io` at ~line 149 with the deployment output (e.g. read `az deployment group show ... --query properties.outputs.apiBaseUrl.value`, or pass it as the `NEXT_PUBLIC_API_BASE_URL` build-arg). No more baked FQDN → survives any CAE rebuild.

### Item 4 — Postgres firewall IPs → Bicep param (not literals in `main.bicep`)
- **main.bicep** — replace the `174.104.204.213` / `135.18.171.52` literals with:
  ```bicep
  @description('Client IPs allowed through the Postgres firewall (owner office + CAE outbound).')
  param allowedClientIps array = [
    '174.104.204.213'   // owner home/office
    '135.18.171.52'     // CAE outbound
  ]
  ```
  then iterate the param to create the `firewallRules` child resources. Per-env override via `*.bicepparam`.

## Item 5 — deferred to VRB-214 (SETTINGS lane)
The outbound iCal rate-limiter policy (`ChannelPollOptions` host-suffixes/token/window/burst, currently hard-coded) is **not** externalized here — it's owned by **VRB-214** ("availability + iCal cadence + rate-limit policy") to avoid two stories double-owning `ChannelPollOptions`. Documented as per-replica (in-memory, gap G28) until then.

## MSAL redirect/callback URIs (item 2) — per env
The SPA uses `NEXT_PUBLIC_ENTRA_AUTHORITY_{ADMIN,GUEST}` + `NEXT_PUBLIC_ENTRA_CLIENT_ID` (CONFIG-MATRIX rows 20–22). Redirect URIs default to the SPA origin and **must be registered as reply URLs** on the `vrbook-web` Entra app registration, per env:

| Env | SPA origin (redirect URI base) | Reply URLs to register |
|---|---|---|
| dev | `http://localhost:3000` | `http://localhost:3000`, `http://localhost:3000/auth/callback` |
| staging | staging web Container App FQDN | `https://<web-fqdn>`, `https://<web-fqdn>/auth/callback` |
| prod | prod web domain | `https://<prod-domain>`, `https://<prod-domain>/auth/callback` |

Admin + guest flows share the SPA app-registration (one `vrbook-web` client id); the authority differs per flow (ADR-0016), the reply URLs are the same origin.

## CDN / cache (item 6) — strategy
- **Public images** (`stvrbook{env}` blob `property-images`): **no CDN today** (CURRENT-STATE §10). Recommended: front the container with Azure CDN / Front Door and set long `Cache-Control: public, max-age=31536000, immutable` on image blobs (content-hashed names). Infra follow-up (Front Door is prod-only today).
- **Public/SEO API responses** (anonymous `GET /properties`, `/properties/{slug}`, `/amenities`): safe to cache briefly at the edge — recommend `Cache-Control: public, max-age=60` once a CDN/Front Door sits in front. Until then, no-store defaults are fine.
- The Next.js SEO pages set their own caching (web lane); this doc covers the API + blob surface.
