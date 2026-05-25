# Threat Model — VrBook Platform (Phase 1)

> Status: **Draft** (A0). Review with each major change. Owner: Solutions Architecture.
> Methodology: STRIDE per data-flow boundary.

## 1. Scope

In-scope: API (REST + webhooks + outbound iCal feed), workers (Sync, Booking, Notification),
Next.js frontend, Postgres, Redis, Service Bus, Blob, SignalR Service, AD B2C.

Out-of-scope (Phase 1): native mobile, third-party SDKs running in the user's browser
beyond Stripe Elements and MSAL.js, AirBnB's own surface.

## 2. Trust boundaries

```
[ Internet ] ── HTTPS ──> [ Front Door + WAF ] ──> [ Container Apps (API + Web) ]
                                                      │
                                                      ├──> [ Private Endpoints ] ──> [ Postgres ]
                                                      ├──> [ Private Endpoints ] ──> [ Redis ]
                                                      ├──> [ Managed Identity ] ──> [ Key Vault / Blob / Service Bus / ACR ]
                                                      ├──> [ SignalR Service ]      (token-scoped negotiate)
                                                      └──> [ B2C / Stripe / SendGrid / AirBnB ]   (egress)
```

Crossings:

1. Internet → Front Door (TLS termination + WAF DRS).
2. Front Door → Container Apps (HTTPS over Microsoft backbone).
3. API → Postgres / Redis (Private Endpoints, no public route).
4. API → KV / Blob / SB / ACR (Managed Identity, no shared keys in config).
5. API → Stripe / SendGrid / AirBnB / B2C (outbound HTTPS; egress proxy in Phase 2).
6. Stripe → API webhook (signature-verified).
7. AirBnB → API feed (token in URL, hashed at rest).

## 3. STRIDE walk

### 3.1 API

| Threat | Vector | Mitigation |
|---|---|---|
| **S**poofing | Forged JWT | B2C token validation; issuer + audience pinned; rotation honoured. |
| **T**ampering | Request body modification in flight | TLS 1.2+; Front Door rejects HTTP. |
| **R**epudiation | "I didn't cancel" | Append-only `admin.audit_log` with actor + before/after JSON. |
| **I**nfo disclosure | PII in logs | Serilog destructuring filter on `email`, `phone`, `address`; opt-in scope for identity ops. |
| **D**oS | Search endpoint abuse | Rate limit 100/min anon, 600/min authed; Front Door WAF rate-limit policy. |
| **E**oP | Cross-tenant read of another user's booking | Ownership pipeline behavior asserts `caller.UserId == aggregate.OwnerUserId` (proposal §14.2). |

### 3.2 Payment webhook

| Threat | Mitigation |
|---|---|
| Replay attack | Idempotency log keyed on `stripe_event_id` (proposal §9.7). |
| Forged webhook | `Stripe.WebhookSignatureValidator` against KV-stored endpoint secret. |
| Webhook timing attack on signature | Use Stripe.net's constant-time compare. |

### 3.3 Outbound iCal feed

| Threat | Mitigation |
|---|---|
| Feed URL leaks → competitor scrapes availability | High-entropy `outboundToken` (32-byte random), hashed at rest with pepper from KV. Stored as `hash(token + pepper)`; constant-time compare on incoming requests. |
| Brute force token | Per-token rate limit (low traffic by design). |
| Stale token reuse | Token rotation by deleting + recreating `channel_feed`. |

### 3.4 Sync poll (egress to AirBnB)

| Threat | Mitigation |
|---|---|
| SSRF via owner-supplied URL | Allowlist scheme (HTTPS), domain pattern (`*.airbnb.com`, `*.airbnb.*`), reject internal CIDR. |
| Malformed ICS DoS | Body size cap (1MB); parse timeout 5s; Ical.Net exceptions logged + skipped. |

### 3.5 Messaging

| Threat | Mitigation |
|---|---|
| Cross-thread access | Membership check on every API call. |
| SignalR token misuse | Negotiate token scoped to `userId`; group join enforced server-side. |
| Attachment upload abuse | 10MB cap, content-type allowlist (`image/jpeg`, `image/png`, `image/webp`, `application/pdf`); virus scan deferred to Phase 2. |

### 3.6 Key Vault + secrets

| Threat | Mitigation |
|---|---|
| Secret in repo | `.gitignore` covers `appsettings.Local.json`, `.env`, `*.pfx`; GitHub secret scanning + Dependabot enabled. |
| Secret in container image | All secrets via Container Apps `secretRef` → KV; verified via image scan in CI. |
| Stale rotated secret | KV rotation policy 90 days; pipeline reads latest version. |

## 4. OWASP Top 10 mapping

See proposal §14.3 for the per-risk table. This document supersedes it for any future change.

## 5. Known unmitigated risks (Phase 1)

- **Phase 1 has no anti-fraud on booking placement** beyond Stripe Radar. If we see abuse (e.g., test-card carding), enable Radar rules + add per-IP rate limit on `POST /bookings`.
- **No virus scan on attachments.** Acceptable because attachments are accessible only to the two-party thread. Add Defender for Storage in Phase 2 if attachment volume grows.
- **No DR (region failover) build** in Phase 1 — listed in §15.2 as "DR plan, no DR build."

## 6. Security review checklist (PR-level)

A PR is "security-clean" when:

- [ ] No new secrets in source. Confirmed by repo scanner + reviewer.
- [ ] No new public-facing endpoint without explicit `[AllowAnonymous]` + auth justification in PR description.
- [ ] Any new external HTTP call goes through a typed `HttpClient` registered with retry + timeout policy.
- [ ] Any new SQL is parameterised (no string concatenation; EF expressions only outside the curated reports module).
- [ ] Any new file upload validates content-type + size before persistence.
- [ ] Any new role check has an integration test asserting the 403 path.
- [ ] Any PII added to a DTO is documented in this file's appendix.
