# Roles Architecture — App Roles for global, DB for per-tenant

> Status: Proposed — awaiting user review.
> Author: Architect consult, 2026-06-25. **Revised 2026-06-26** after re-consult flagged App Roles as the simpler primary path.
> Supersedes the role-claim flow described in [`docs/OPS_M_0_PLAN.md`](../OPS_M_0_PLAN.md) §1 and [`docs/identity/setup.md`](./setup.md) §3 / §5 / §8.
> Companion ADR: **ADR-0014 — App Roles for global roles, DB for per-tenant roles** (to be written alongside this doc).
> Does **not** supersede [ADR-0012](../adr/0012-entra-external-id-over-b2c.md) — Entra External ID stays as the identity provider. Only the role-flow changes.

---

## 1. Decision (revised)

**Split by role scope:**

- **Global roles (`Owner`, `Admin`)** ship as **Entra App Roles** on the `vrbook-api-staging` / `vrbook-api-prod` app registrations. Entra emits a native `roles` claim in the access token; ASP.NET's JwtBearer maps it to `ClaimTypes.Role` automatically. `[Authorize(Roles="Owner,Admin")]` works with zero code changes.
- **Per-tenant role (`tenant_admin`, OPS.M.5)** ships as a `identity.tenant_memberships(user_id, tenant_id, role)` DB row — a token can't express "admin on tenant X" without per-tenant token re-issue. `UserProvisioningMiddleware` is extended in OPS.M.1 to enrich the request's `ClaimsPrincipal` with these per-tenant claims; this is also where the doc's previous middleware-mutation pattern lands.

### Why this split

The previous draft of this doc recommended DB-backed roles for **everything**, abandoning Entra entirely. Re-consult surfaced that the previous draft was over-correcting: we ran into trouble with `extension_*` attributes (custom user data) but never tried Entra's purpose-built **App Roles** feature (authorization). App Roles is the platform-native mechanism for "what role is this user." It emits a `roles` claim natively — no `optionalClaims` PATCH, no extension property creation, no user-flow application-claims fiddling.

Option-tree:

| Option | Verdict |
|---|---|
| A. Chase extension claims | Rejected — empirically non-emitting in CIAM access tokens issued via user flows. |
| B. DB-backed roles (everything) | Demoted to per-tenant only. Over-corrects for the global Owner/Admin case. |
| C. Custom Authentication Extension webhook | Rejected — adds public webhook, second deploy surface, still-evolving preview feature. |
| **D. App Roles (global) + DB (per-tenant)** | **Chosen.** Zero code for OPS.M.0; DB plumbing arrives in OPS.M.1 already-needed for `tenant_memberships`. |

---

## 2. Justification against constraints

| Constraint (from prompt + plans) | How this design satisfies it |
|---|---|
| Don't block OPS.M.0 closure on CIAM quirks | Zero further CIAM portal/Graph work for roles. The Graph PATCH of `optionalClaims.accessToken` and the per-user extension PATCH from `grant-self-admin.ps1` both go away. |
| Must support `tenant_admin` per-tenant role in OPS.M.5 | Per-tenant role is exactly what a `tenant_memberships(user_id, tenant_id, role)` row models. Token-embedded roles cannot represent "admin on tenant X" without per-tenant token re-issue (this is the bug class `MULTI_TENANCY_OPS_PLAN.md` §2 acknowledges with "switching tenants triggers a re-issue"). DB-backed lookup sidesteps it. |
| Multi-tenancy model: guests platform-wide; tenants supply-side | `users.is_admin` stays global. `tenant_memberships` only exist for supply-side users. Guests have zero `tenant_memberships` rows and `app_tenant_id` resolves to null — matches `MULTI_TENANCY_OPS_PLAN.md` §1 table. |
| Dynamic role changes shouldn't require user sign-out | Granting/revoking a role is a SQL update; the next request loads the new state. The user's *current* in-memory `ClaimsPrincipal` is stale until their next request, but the worst case is one stale request — far better than "wait 60 min for token refresh, then sign out / sign in." |
| Decouple from CIAM idiosyncrasies | CIAM only does what it does reliably: authenticate the human and stamp `oid`+`email`. We rely on nothing else. |
| Blast radius if it fails | If the DB lookup throws, the middleware logs and continues without role claims — `[Authorize]` then rejects with 403, the same observable behavior as a token without the right role. No 500s, no broken sign-in. The existing `try/catch` in `UserProvisioningMiddleware.cs:54-59` already does this. |
| Deploy complexity | Zero new infra. No new endpoint exposed to the internet. No webhook. One EF migration + ~30 lines in the existing middleware. |
| DevAuth keeps working in staging | DevAuth already stamps `ClaimTypes.Role` directly (`DevAuthHandler.cs:121-127`). No change needed to DevAuth. The two schemes converge on the same ASP.NET role check. |

The single trade-off: **one extra DB round-trip per authenticated request**. This already happens — `UserProvisioningMiddleware` already calls `ProvisionUserCommand` on every request, which already issues a `GetByB2CObjectIdAsync`. We piggy-back on the row that's already being read. Cost in execution: one additional `Include(u => u.Memberships)`, no extra round-trip.

---

## 3. Schema changes

### 3.1 `identity.users` — no change

Columns `is_owner BOOL DEFAULT FALSE` and `is_admin BOOL DEFAULT FALSE` already exist (see `Migrations/20260525190601_Init_IdentitySchema.cs:50-51`). These are the source of truth for the platform-global `Admin` role and the legacy `Owner` role.

`is_owner` is retained for the duration of OPS.M (it's read by every `[Authorize(Roles="Owner,Admin")]` controller — see grep results below) and is scheduled for **deprecation in OPS.M.4** when `TenantAuthorizationBehavior` lands, per `MULTI_TENANCY_OPS_PLAN.md` §10. After OPS.M.4, a `tenant_admin` membership is what was previously "Owner."

### 3.2 `identity.tenant_memberships` — new (lands in OPS.M.1, this doc reserves the shape)

```sql
CREATE TABLE identity.tenant_memberships (
    id              uuid PRIMARY KEY,
    user_id         uuid NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    tenant_id      uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE CASCADE,
    role            text NOT NULL CHECK (role IN ('tenant_admin', 'tenant_member')),
    is_primary      bool NOT NULL DEFAULT false,
    created_at      timestamptz NOT NULL,
    created_by      uuid NULL,
    deleted_at      timestamptz NULL,
    deleted_by      uuid NULL
);

CREATE UNIQUE INDEX ux_tenant_memberships_user_tenant
    ON identity.tenant_memberships(user_id, tenant_id)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_tenant_memberships_user
    ON identity.tenant_memberships(user_id)
    WHERE deleted_at IS NULL;
```

Notes:
- `tenant_member` is reserved per `MULTI_TENANCY_OPS_PLAN.md` §1 ("Deferred. Schema supports it; UI ships in Phase 2"). Only `tenant_admin` is read by this doc's middleware change today.
- The `is_primary` flag answers "which tenant is the user *currently* acting as" for users with multiple memberships. Mirrors the OPS.M.5 plan.
- The `tenants` table already exists as a placeholder (`Migrations/20260613003433_Slice3_TenantsPlaceholder.cs`).

### 3.3 OPS.M.0 scope vs OPS.M.1 scope

This doc *defines* the membership table shape but **the migration is created in OPS.M.1** alongside the full tenant aggregate. OPS.M.0's role-claim fix uses **only the existing `users.is_owner` / `users.is_admin` columns** — no new migration required, no new code outside of the middleware. This keeps OPS.M.0 a 1-2 hour close-out rather than a multi-day slip.

---

## 4. Middleware change

### 4.1 The minimal OPS.M.0 patch (this slice)

Edit `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs`. After the existing `ProvisionUserCommand` returns `userId`, load the persisted role flags and add them as claims on the request's `ClaimsPrincipal`:

```csharp
// After: ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId;

// Load DB-side roles. ProvisionUserHandler already wrote them (from token if present,
// otherwise from existing row state). Re-reading guarantees we see admin-tool changes
// that happened mid-session.
var user = await users.GetByIdAsync(userId, ctx.RequestAborted);
if (user is not null)
{
    var roleClaims = new List<Claim>();
    if (user.IsOwner) roleClaims.Add(new Claim(ClaimTypes.Role, "Owner"));
    if (user.IsAdmin) roleClaims.Add(new Claim(ClaimTypes.Role, "Admin"));

    if (roleClaims.Count > 0 && ctx.User.Identity is ClaimsIdentity primary)
    {
        primary.AddClaims(roleClaims);
    }
}
```

Inject `IUserRepository users` into the middleware's `InvokeAsync` parameter list (same DI pattern as the existing `IMediator mediator`).

This change is enough for `[Authorize(Roles="Owner,Admin")]` to succeed for an Entra-authenticated user whose `is_owner=true` in the DB. **It is the entire OPS.M.0 close-out.**

### 4.2 Why mutate the existing `ClaimsPrincipal` rather than synthesize a new one

ASP.NET's role check (`IsInRole`) reads claims via the principal's `RoleClaimType`. JwtBearer sets that to `ClaimTypes.Role` by default for the principal it builds. Adding `Claim(ClaimTypes.Role, "Owner")` directly is the smallest possible change. We do NOT touch `JwtBearerOptions.TokenValidationParameters.RoleClaimType` — the standard wiring is fine.

### 4.3 What changes in OPS.M.1+ (forward look)

When `tenant_memberships` lands, the middleware extends the same block:

```csharp
foreach (var m in user.Memberships.Where(m => m.DeletedAt is null))
{
    roleClaims.Add(new Claim(ClaimTypes.Role, m.Role));  // "tenant_admin"
    if (m.IsPrimary)
    {
        primary.AddClaim(new Claim("app_tenant_id", m.TenantId.ToString()));
    }
}
```

`HttpCurrentUser.TenantId` (added by `MULTI_TENANCY_OPS_PLAN.md` §2) reads `app_tenant_id` off the principal.

### 4.4 `HttpCurrentUser` change

`HttpCurrentUser.IsOwner` / `IsAdmin` today read `extension_isOwner` / `extension_isAdmin` claims OR `IsInRole`. After this doc lands, only `IsInRole` matters. The `extension_*` paths can stay for DevAuth compatibility (DevAuth synthesizes the extension claim today; see `DevAuthHandler.cs:114-115`) — they become harmless. Cleanup is deferred to OPS.M.1's `ICurrentUser` reshape.

---

## 5. Role administration API — script + endpoint, both

### 5.1 `infra/scripts/grant-self-admin.ps1` — rewrite as a SQL UPDATE

The current implementation does `az login --tenant $ExternalTenant`, finds the Entra user, PATCHes `extension_*` properties on the Entra user. None of this is needed.

Replacement (sketch — actual file lives at `infra/scripts/grant-self-admin.ps1`):

```powershell
param(
  [ValidateSet('dev','staging','prod')][string]$Env,
  [string]$UserEmail
)
. (Join-Path $PSScriptRoot '_common.ps1')
$state = Read-State -Env $Env

# Reuse the existing KV secret name (postgres-connection-string) and the
# psql binary available on the runner. No az login required.
$pgConn = az keyvault secret show --vault-name "kv-vrbook-$Env" --name postgres-connection-string --query value -o tsv

psql $pgConn -c @"
  UPDATE identity.users
     SET is_owner = true, is_admin = true, updated_at = now()
   WHERE email = '$UserEmail';
"@
```

This makes the bootstrap idempotent, environment-symmetric, and free of CIAM dependence. The user signs up via the Entra user flow once (so `UserProvisioningMiddleware` creates the `users` row); then this script flips the flags.

### 5.2 New admin endpoint — `POST /api/v1/admin/users/{id}/roles`

For ongoing role management (not the one-off bootstrap), add an admin-gated endpoint in `src/VrBook.Api/Controllers/AdminController.cs`:

```csharp
[HttpPost("{id:guid}/roles")]
[Authorize(Roles = "Admin")]
public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetRolesRequest req, CancellationToken ct)
    => Ok(await mediator.Send(new SetUserRolesCommand(id, req.IsOwner, req.IsAdmin), ct));
```

Backed by a new `SetUserRolesCommand` in `VrBook.Modules.Identity.Application.Users.Commands`. The command is `IAuditable` (per `MULTI_TENANCY_OPS_PLAN.md` §9 — "Mark every Super Admin command IAuditable"). The handler calls `User.GrantOwner()` / `User.RevokeOwner()` / `User.GrantAdmin()` / `User.RevokeAdmin()` — methods that **already exist** on the `User` aggregate (`User.cs:106-109`).

The script-based path stays for the FIRST admin per environment (chicken-and-egg: no admin exists to call the endpoint). After that, the endpoint is the documented path. The OPS.M.8 Super Admin console (per `MULTI_TENANCY_OPS_PLAN.md` §9) consumes this same command.

### 5.3 What this removes

- The Graph PATCH on the Entra user (steps in `docs/identity/setup.md` §8).
- The `optionalClaims.accessToken` configuration on the `vrbook-api` app registration.
- The `extension_<api-app-id>_isOwner` / `_isAdmin` extension property definitions on the `vrbook-api` app.
- The "application claims" checkboxes in the user flow for the two `extension_*` items.

These should be left in place for the staging tenant (no harm — they're inert) and not provisioned at all when the prod tenant lands. The setup runbook (§3, §5, §8) gets a strikethrough block pointing at this doc.

---

## 6. Tenant scoping (OPS.M.5 readiness)

`tenant_admin` is per-tenant. The token never carries the tenant. Resolution rule:

1. **Path-derived**: `/api/v1/tenants/{tenantId}/...` or `/api/v1/admin/tenants/{tenantId}/...`. The tenant id appears in the route; an `AuthorizationHandler` checks `currentUser.HasTenantRole(tenantId, "tenant_admin")`.
2. **Aggregate-derived** (the OPS.M.4 pattern): for handlers that don't take a tenant id directly, `TenantAuthorizationBehavior` resolves the aggregate's `tenant_id` from the request, then checks the same `HasTenantRole`. This is exactly what `MULTI_TENANCY_OPS_PLAN.md` §2 already specifies.
3. **Primary-tenant fallback**: for UI screens that say "manage my tenant," the active tenant comes from `is_primary=true` membership, surfaced as `currentUser.TenantId`.

Subdomain routing (`tenantA.vrbook.example.com`) is explicitly out of scope per `MULTI_TENANCY_OPS_PLAN.md` §11 — we don't depend on it.

`HasTenantRole(Guid tenantId, string role)` reads the loaded `Memberships` collection (already on the principal as facts) — no additional DB round-trip per check.

---

## 7. Implementation plan (revised — App Roles primary)

### OPS.M.0 close-out (today, ~10 minutes, portal only)

#### Step 1 — Define App Roles on `vrbook-api-staging`
*Path*: Entra admin center → App registrations → `vrbook-api-staging` → **App roles** → **+ Create app role**
*Action*: Add two roles:
- Display name `Owner`, value `Owner`, allowed members `Users/Groups`, description "Property owner; can manage their listings + bookings."
- Display name `Admin`, value `Admin`, allowed members `Users/Groups`, description "Platform admin; cross-tenant access."

#### Step 2 — Assign current user to both roles
*Path*: Entra admin center → Enterprise applications → `vrbook-api-staging` → **Users and groups** → **+ Add user/group**
*Action*: Pick `niroshanaks@gmail.com`, select role `Owner`. Repeat for `Admin`.
*Fallback*: if portal UI is greyed out (CIAM preview rotation), use one Graph call: `POST /users/{userId}/appRoleAssignments` with `{ "principalId": "<userId>", "resourceId": "<vrbook-api-staging servicePrincipal objectId>", "appRoleId": "<roleId>" }`.

#### Step 3 — Verify
1. Close incognito; fresh sign-in.
2. DevTools Console — decode the access token. Expect `roles: ["Owner", "Admin"]` (order may differ).
3. Visit `/admin` — loads via Entra without DevAuth fallback.
4. Once verified, set `DevAuth__AllowAnonymous=false` in staging Container App env so DevAuth can't silently authorize anymore.

### OPS.M.1 — DB plumbing for `tenant_admin` (deferred; not now)

Lands as part of the tenant aggregate work in OPS.M.1, NOT in OPS.M.0 close-out:

#### Step 4 — Add `identity.tenant_memberships` table (EF migration)
Per shape defined in §3.2 above.

#### Step 5 — Extend `UserProvisioningMiddleware` to load tenant memberships
*File*: `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs`
*Change*: After `ProvisionUserCommand` returns `userId`, load `tenant_memberships` for the user, add `ClaimTypes.Role` claims for each membership role (e.g. `tenant_admin`), and add `app_tenant_id` claim if a primary membership exists. Wrap in the existing try/catch.
*Acceptance*: integration test asserts a user with a `tenant_admin` membership for tenant X can access `/tenants/{X}/admin/*` and is rejected for tenant Y.

#### Step 6 — Per-tenant grant API
*Files (new)*:
- `src/Modules/VrBook.Modules.Identity/Application/Memberships/Commands/GrantTenantRoleCommand.cs`
- Admin endpoint `POST /api/v1/admin/tenants/{tenantId}/members` in `AdminController`.

### What this slice (today) does NOT touch

- `UserProvisioningMiddleware.cs` — no code change today.
- `infra/scripts/grant-self-admin.ps1` — no rewrite needed for the global role bootstrap; App Role assignment in Step 2 is the bootstrap. The script can be kept for compatibility or quietly retired.
- Any DB migration — `users.is_owner` / `is_admin` columns remain but go unused for the Entra path. They stay populated for the DevAuth path (which uses them when a real DB-backed persona seeds data).
- No new ASP.NET role policies — the existing `OwnerOrAdmin` / `Admin` policies in `AuthExtensions.cs` already match the App Role values one-to-one.

### Step 3 — Add `SetUserRolesCommand` + admin endpoint
*Files (new)*:
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetUserRolesCommand.cs`
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetUserRolesHandler.cs`
*File (edit)*: `src/VrBook.Api/Controllers/AdminController.cs` — add `POST /api/v1/admin/users/{id}/roles`.
*Acceptance*: integration test asserts a non-admin caller is rejected 403 and an admin caller flips a user's `is_owner` flag.

### Step 4 — Integration test for Entra-path role resolution
*File (new)*: `tests/VrBook.Api.IntegrationTests/Identity/RoleResolutionTests.cs`
*Scenario*: Authenticate with a synthetic JwtBearer principal carrying `oid`/`email` only (no extension claims). Seed `identity.users` row with `is_admin=true`. Hit `/api/v1/admin/users` (an admin-gated endpoint). Expect 200. Repeat with `is_admin=false`; expect 403.
*Why this test matters*: it is the regression net for the actual bug we just fixed.

### Step 5 — Update OPS.M.0 plan + identity setup runbook + ADR-0012 footer
*Files*:
- `docs/OPS_M_0_PLAN.md` — append a "Post-cutover correction" subsection at the end of §1 pointing at this doc. Mark operational task 12 (`grant-self-admin.ps1`) as "rewritten — see roles-architecture.md §5".
- `docs/identity/setup.md` — strike through §3 step 3 (extension attributes) and §5 step 5 (application claims for extensions) and §8 (Graph PATCH); replace with one paragraph pointing here.
- `docs/identity/README.md` — update the "Extension attributes" + "Bootstrapping yourself" sections.
- `docs/adr/0012-entra-external-id-over-b2c.md` — append a short note: "The `extension_*` role claim path described in this ADR was abandoned during OPS.M.0 close-out; see ADR-0014 / `docs/identity/roles-architecture.md`."

### Step 6 — Write ADR-0014 (DB-backed roles)
*File (new)*: `docs/adr/0014-db-backed-roles-over-entra-extension-claims.md`
*Shape*: standard ADR template. Status: Accepted. Date: 2026-06-25. Context: the empirical extension-claim non-emission problem documented in this design doc's prompt. Decision: per this doc §1. Consequences: per this doc §2. Links: this doc, ADR-0012.

---

## 8. What changes vs OPS_M_0_PLAN.md and ADR-0012

| Reference | Original assumption | What this doc changes |
|---|---|---|
| `docs/OPS_M_0_PLAN.md` §1 "What is already shipped — Backend auth" | Roles flow as `extension_*` claims in the access token. | Roles flow from the DB via middleware enrichment. The backend `JwtBearer` registration is untouched; only the middleware adds role claims. |
| `docs/OPS_M_0_PLAN.md` operational step 12 (`grant-self-admin.ps1`) | Sets Entra extension attributes via Graph PATCH. | Becomes a SQL UPDATE on `identity.users`. |
| `docs/OPS_M_0_PLAN.md` §7 verification step 7 ("Bootstrap as Owner+Admin") | Sign out + back in to refresh extension claims. | Sign out + back in still recommended (in-memory principal is per-request, but the user's browser may cache state); the **role-bearing** step is now the SQL update + next request. |
| `docs/identity/setup.md` §3 step 3 (extension attribute creation) | Defines `isOwner` / `isAdmin` extension properties on `vrbook-api`. | No longer needed; can be removed from new tenants. Staging tenant keeps them as inert artifacts. |
| `docs/identity/setup.md` §5 step 5 (application claims) | Adds `extension_<api-app-id>_isOwner/isAdmin` to the user flow's emitted claims. | No longer needed. |
| `docs/identity/setup.md` §8 (Graph PATCH for bootstrap) | Bootstraps the first admin by patching Entra. | Bootstraps by SQL UPDATE. |
| `ADR-0012` "What does not change" list | Implies extension claim plumbing is the role channel. | The role channel is the DB. ADR-0012's main decision (Entra External ID for identity) stands. |

**A new ADR (ADR-0014) is warranted** because:
- This is a directional change at the same altitude as ADR-0012 (where roles live, not just how a knob is configured).
- It will be cited by OPS.M.1's tenant aggregate work, OPS.M.4's `TenantAuthorizationBehavior`, and OPS.M.8's Super Admin console.
- ADR-0012 has multiple decision points; an in-place edit would muddy its historical record.

---

## 9. Non-goals

- **RBAC2 / permission graphs / ACLs**. We have four role values (`guest`, `owner` [deprecating], `admin`, `tenant_admin`). Anything richer is out of scope.
- **Per-tenant Entra tenants** — explicitly out of scope per `MULTI_TENANCY_OPS_PLAN.md` §11.
- **Custom Authentication Extension webhook** — designed against and rejected for OPS.M.0; revisit if a use case appears that requires roles in the *raw token* (e.g., a third-party API consuming our access tokens needs role data without calling our middleware). None exists today.
- **Token re-issue on role grant** — out. The next request picks up the new role. A token does not need to be revoked or refreshed.
- **Multi-region role replication** — out. Single Postgres, single source of truth.
- **Role hierarchy / inheritance** — out. `Admin` does not "inherit" `Owner`; controllers list both explicitly as `[Authorize(Roles="Owner,Admin")]`. This is already the convention across `AdminController`, `PropertiesController`, `BookingsController`, `PricingController`, `ReviewsController`, `ReportsController`, `SyncController`, `NotificationsController`, `PaymentsController`. Do not refactor as part of this doc.
- **Subdomain-derived tenancy** — out per `MULTI_TENANCY_OPS_PLAN.md` §11.
- **Removing the `extension_*` claim path from `DevAuthHandler` / `HttpCurrentUser`** — out. They're harmless and removing them is a separate cosmetic pass slated for OPS.M.1.

---

## 10. Open questions for review

1. Confirm the staging App Role values stay exactly `Owner` and `Admin` (case-sensitive — JwtBearer matches verbatim against `[Authorize(Roles="Owner,Admin")]`).
2. Confirm the Entra extension artifacts from the previous direction (extension properties on `vrbook-api-staging`, `optionalClaims.accessToken` PATCH, extension values set on `niroshanaks@gmail.com`) stay in place as inert (recommended) vs are cleaned up. Recommendation: leave inert; cleanup is zero value.
3. Confirm `DevAuth__AllowAnonymous=false` flip is part of OPS.M.0 close-out (it should be — leaving DevAuth open in staging defeats the verification).
