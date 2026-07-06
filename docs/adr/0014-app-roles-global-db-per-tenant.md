# 14. App Roles for global Owner/Admin, DB-backed memberships for per-tenant roles

- **Status**: Accepted
- **Date**: 2026-06-26
- **Deciders**: Solutions Architecture (architect consult during OPS.M.0 close-out + OPS.M.1 plan)
- **Tags**: identity, authorization, entra, multi-tenancy

## Context

VrBook ships authorization on two axes:

1. **Global roles** — platform-wide flags. `Admin` (super admin, ~3 internal humans per `docs/MULTI_TENANCY_OPS_PLAN.md` §1) and `Owner` (the legacy "property owner" role retained until OPS.M.4 retires it for `tenant_admin`).
2. **Per-tenant roles** — `tenant_admin` (manages one tenant's properties + bookings) and `tenant_member` (deferred UI; schema supports it).

The original design (ADR-0012 "Claim names are stable" + `docs/OPS_M_0_PLAN.md` §1) put both axes into custom **extension attributes** on the Entra External ID API app registration (`extension_<vrbook-api-appId>_isOwner`, `extension_<vrbook-api-appId>_isAdmin`). The plan: grant-self-admin.ps1 PATCHes the user's extension values via Graph; the user signs out and back in; the new access token carries the extension claims; `[Authorize(Roles="Owner,Admin")]` enforces.

During OPS.M.0 close-out on staging (2026-06-25), we hit an empirical wall: **Entra External ID's CIAM tokens issued via user flows do not reliably emit app-level extension claims, even when `optionalClaims.accessToken` is correctly configured on the API app**. The claim names, the extension property definitions, the user-side values, and the optionalClaims PATCH were all verified in place — Microsoft Graph confirmed each — and yet the access token at the browser was empty of extension claims after every fresh sign-in.

This forced a re-design. The architect was re-consulted (lesson saved at [`feedback_proactive_architect_consult`](../../memory/feedback_proactive_architect_consult.md)) and surfaced **Entra App Roles** as the platform-native mechanism we hadn't tried. Per-tenant roles cannot be embedded in a single access token (a token can't say "admin of tenant X" without per-tenant token re-issue, which contradicts our single-tenant Entra direction in ADR-0012) — they need a different home regardless.

## Decision

Authorization state splits by **role scope**:

### Global roles → Entra App Roles

`Owner` and `Admin` are defined as **App Roles** on the `vrbook-api-<env>` app registration in the External tenant. Users are assigned via `POST /users/{id}/appRoleAssignments` (Graph) or the Enterprise applications → Users and groups portal blade. The access token carries a native `roles` claim:

```json
{ "aud": "<vrbook-api appId>", "roles": ["Owner", "Admin"], "..." }
```

ASP.NET Core's `JwtBearer` automatically maps `roles` to `ClaimTypes.Role`. Every existing `[Authorize(Roles="Owner,Admin")]` controller works unchanged with zero code touching the API.

### Per-tenant roles → `identity.tenant_memberships`

`tenant_admin` and `tenant_member` live in a `identity.tenant_memberships(user_id, tenant_id, role, is_primary, ...)` table (per OPS.M.1 / `docs/OPS_M_1_PLAN.md` §3.2). Token does not carry the tenant. Resolution at request time:

- `UserProvisioningMiddleware` (OPS.M.2, planned) loads memberships for the authenticated `oid` on every authenticated request and adds `ClaimTypes.Role` claims + `app_tenant_id` claim onto the request's `ClaimsPrincipal`.
- `TenantAuthorizationBehavior` (OPS.M.4, planned MediatR pipeline behavior) auto-rejects handlers where the loaded aggregate's `tenant_id` mismatches the claim.

## Rejected alternatives

### A. Continue chasing CIAM extension-claim emission

Try directory-level Custom user attributes (different namespace from app-level extension properties); migrate values; update API claim mapping. **Rejected** — CIAM emission semantics are under-documented; spent ~1h on staging with no success; risk of further dead-ends is unbounded.

### B. DB-backed roles for *everything* (no Entra App Roles)

Token carries identity only; middleware reads global roles from `identity.users.is_owner / is_admin` plus per-tenant roles from `tenant_memberships` on every request. **Rejected for the global axis** — over-corrects. App Roles is the platform-native mechanism for "what role is this user" and is well-supported by ASP.NET out of the box. Adopted for the per-tenant axis only, where it is the correct (and only) answer.

### C. Custom Authentication Extension webhook (CAE)

Register an Entra CAE webhook that injects claims at token-issuance time by calling our API. **Rejected** for OPS.M.0 close-out — adds a publicly-reachable token-time webhook (security surface), a second deploy artifact (the CAE endpoint), and a hard dependency on a still-evolving Entra preview feature. We can adopt CAE later if a use case appears that requires roles in the *raw token* (e.g., a third-party consumer of our access tokens needs role info without calling our middleware). No such use case exists today.

## Consequences

### Positive

- **Zero code change for global Owner/Admin.** Three portal/CLI clicks per environment to define App Roles + assign the first admin. ASP.NET's existing `[Authorize(Roles=...)]` works unchanged.
- **Dynamic role changes** for per-tenant roles. Granting/revoking is a SQL update; the next request loads new state. No "wait for token refresh + sign out + sign in" loop.
- **Per-tenant scope is natural.** The `tenant_admin` role for tenant X never has to be jammed into a token; it lives where it belongs (a row in the DB).
- **Failure mode is benign.** If the membership lookup throws, middleware logs and continues without role claims; `[Authorize]` then rejects with 403. Same observable behavior as a token without the right role — no 500s, no broken sign-in.
- **Audit-friendly.** Every role grant/revoke is a SQL row with `created_at` / `created_by`, and (for the per-tenant path) raises a `TenantMembershipCreated` / `TenantMembershipRoleChanged` / `TenantMembershipRevoked` domain event (per OPS.M.1 Step 1).

### Negative / trade-offs

- **One extra DB read per authenticated request** for per-tenant claim resolution. Acceptable: `UserProvisioningMiddleware` already reads the `users` row per request; the membership lookup piggy-backs on the same scope.
- **Two mechanisms for two scopes.** Operators need to know: "global roles are configured in the Entra portal; per-tenant roles are managed via the admin API or `grant-tenant-role` script." This is documented in [`docs/identity/runbooks/entra-key-rotation.md`](../identity/runbooks/entra-key-rotation.md) §2.
- **Per-tenant role changes don't show up in the user's *current* in-memory `ClaimsPrincipal`** until their next request. Worst case is one stale request — far better than the 60-min token refresh window an Entra-only design would impose.

## What this does **not** change

- **ADR-0012 (Entra External ID for identity) stands.** Entra remains the identity provider; only role plumbing moves.
- **DevAuth** (`DevAuthHandler`) continues to add `ClaimTypes.Role` claims directly from the persona for dev environments. Both schemes converge on the same ASP.NET role-check API.
- **No new public infrastructure.** No webhook, no new endpoint exposed to the internet.

## What ships in OPS.M.1 from this ADR

- The `identity.tenants` aggregate broadens (status lifecycle, currency, timezone, support email, Stripe placeholders).
- The `identity.tenant_memberships` table lands with the shape locked here.
- Domain unit tests for both aggregates.
- A deterministic default-tenant seed row (`00000000-0000-0000-0000-000000000001`) so OPS.M.3b's bulk backfill has a known target.

Consumers (`UserProvisioningMiddleware` enrichment, `TenantAuthorizationBehavior`, the admin endpoint that writes memberships) ship in OPS.M.2 + OPS.M.4 + OPS.M.8 respectively.

## Amendment 2026-07-06 — OPS.M.15 cleanup landed

Slice OPS.M.15 (documented in [`OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](../OPS_M_15_APP_ROLES_CLEANUP_PLAN.md) and [`OPS_M_15_CLOSE_OUT.md`](../OPS_M_15_CLOSE_OUT.md)) closed the pre-ADR-0014 legacy authorization surface that was still present as dead weight belt-and-braces alongside the App Roles / DB-memberships shape this ADR pins.

**What OPS.M.15 removed:**

- `HttpCurrentUser.OwnerClaim` / `AdminClaim` constants (`extension_isOwner` / `extension_isAdmin`).
- `HttpCurrentUser.ReadBoolClaim` private helper.
- `ICurrentUser.IsOwner` / `IsAdmin` boolean accessors — retired outright, not reshaped. Business logic that previously read them migrated to `HasTenantRole(tenantId, "tenant_admin")` — see the 9-site call-site sweep in the close-out §1.
- Every `[Authorize(Roles="Owner,Admin")]` / `[Authorize(Roles="Admin")]` decorator on 12 controllers — migrated to `[Authorize]` + handler-level `HasTenantRole` where the role check was load-bearing.
- `AuthExtensions.AddPolicy("OwnerOrAdmin", ...)` + `AddPolicy("Admin", ...)` — neither was referenced anywhere.
- `TestAuthHandler` emission of `extension_isOwner` / `extension_isAdmin` claims + `TestPersona.IsOwner`/`IsAdmin` boolean fields (reshaped to `Roles: IReadOnlyList<string>`).
- SPA `useAuth.ts` reads of `claims.extension_isOwner` / `extension_isAdmin` + the `AuthUser.isOwner`/`isAdmin` fields.

**What OPS.M.15 KEEPS (current shape, DO NOT DELETE):**

- **`Owner` + `Admin` App Role definitions on `vrbook-api-<env>` app registration in Entra.** These emit the token's `roles` claim which JwtBearer maps to `ClaimTypes.Role`; `HttpCurrentUser.HasRole` reads that. Deleting the App Role definitions in the Entra portal breaks every authenticated admin request. See `OPS_M_15_APP_ROLES_CLEANUP_PLAN.md` §4 risk #5.
- **`identity.tenant_memberships.role="tenant_admin"`** — the DB-authoritative shape `HasTenantRole` reads. Materialized by `UserProvisioningMiddleware` into `HttpContext.Items` per-request.
- **`HttpCurrentUser.PlatformAdminItemKey` / `PlatformAdminRole` / DB `identity.users.is_platform_admin` flag** — CURRENT SHAPE per §7-Q6. Materialized as a synthesized `ClaimTypes.Role="PlatformAdmin"` claim.
- **`identity.users.is_owner` / `is_admin` DB columns** + the `UserDto.IsOwner` / `IsAdmin` wire-contract fields — kept for one cycle per §7-Q1. Follow-up slice drops both together after the SPA nav is refactored to key on membership roles instead. The columns still feed the DTO but nothing in server-side authorization keys off them.

**Guardrails added:**

- `OpsM15_NoLegacyExtensionClaimSymbolsTests` (5 facts) — pins the absence of the legacy claim readers.
- `OpsM15_NoOwnerAdminRoleAttributeTests` (3 facts) — pins the absence of `[Authorize(Roles="Owner"|"Admin")]`; allowlist is `{"PlatformAdmin"}` only.
- `OpsM15_ICurrentUserRoleShapeTests` (2 facts) — pins the removed accessors + the surviving `HasRole` reader.
- `OpsM15_OwnerActionHandlerRoleGateTests` (3 facts) — pins the handler-level `HasTenantRole(booking.TenantId, "tenant_admin")` on booking transitions.
- Web arch test `useAuth-noExtensionClaimReads.test.ts` — pins the absence of `extension_*` claim reads on the SPA side.

Any future PR that resurrects the legacy pattern fails one or more of these facts before merge.

## References

- [`docs/identity/roles-architecture.md`](../identity/roles-architecture.md) — the design doc this ADR formalises.
- [`docs/OPS_M_0_PLAN.md`](../OPS_M_0_PLAN.md) §"Post-cutover correction" — the empirical OPS.M.0 finding.
- [`docs/OPS_M_1_PLAN.md`](../OPS_M_1_PLAN.md) — the schema work this ADR is the foundation of.
- [`docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](../OPS_M_15_APP_ROLES_CLEANUP_PLAN.md) — legacy retirement plan (2026-07-06).
- [`docs/OPS_M_15_CLOSE_OUT.md`](../OPS_M_15_CLOSE_OUT.md) — legacy retirement close-out (2026-07-06).
- [`docs/MULTI_TENANCY_OPS_PLAN.md`](../MULTI_TENANCY_OPS_PLAN.md) §1 + §2 — the role taxonomy and authorization design.
- [ADR-0012](./0012-entra-external-id-over-b2c.md) — the identity provider decision (unchanged).
- [`docs/identity/runbooks/entra-external-id-setup.md`](../identity/runbooks/entra-external-id-setup.md) §7 — operational procedure for App Role definition + assignment.
