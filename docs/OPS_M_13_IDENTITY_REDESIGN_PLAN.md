# Slice OPS.M.13 — Identity Redesign (email-canonical users + tenant picker + verified-email guard)

- **Status:** DRAFT for reviewer sign-off (`niroshanaks`). Not committed.
- **Date:** 2026-07-01.
- **Author (role):** Platform Enterprise Architect.
- **Predecessors:** [`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md`](./OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md) §5.1 (schema shape) · [`OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md`](./OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md) §2.6 (ownership map).
- **Scope:** ONE vertical slice. Ships email-canonical `identity.users`, a
  `user_identities` side-table, an email-verified guard on provisioning, a
  `GET /api/v1/me/tenants` endpoint, a sign-in tenant-picker page, and an
  `X-Active-Tenant` header contract. Bundles OPS.M.16 (migration audit table)
  as a sub-commit so this slice's own backfill is its first customer.
- **Explicitly NOT in this slice:** DevAuth retirement (OPS.M.14), App-Role
  cleanup (OPS.M.15), social IdPs (OPS.M.12). Those stay queued.
- **User's approved business decisions (2026-07-01):**
  1. Multi-tenant humans **YES** — tenant-picker at sign-in, Slack/GitHub/Notion pattern.
     **User cannot switch tenants mid-session; must sign out to change tenant.**
  2. Verified email required. Refuse to bind an existing profile when `email_verified=false`.

---

## §1 Plain-English overview

Right now, when you sign in to VrBook, the app creates a row keyed on the
sign-in session's device fingerprint (technically an "object id"). Every
time your browser gets a fresh one — new device, cleared cookies, a
maintenance rotation on Microsoft's side — the app treats you as a new
person. Your bookings, your admin permissions, and your workspace
membership all sit on whichever old row happens to match today's session,
and today's session usually matches nothing. That's why the walk keeps
breaking: the "you" that clicks Confirm has no workspace attached.

This slice fixes that once. From now on the app identifies you by your
**email address**, not by the session fingerprint. Sign in from any device,
from any tab, after any cache clear, and you land on the same "you" with
all your bookings, your admin bit, and every workspace you belong to.

Because a real human can legitimately belong to more than one workspace
(a management company with two property portfolios; an assistant helping
several owners), sign-in now has a small extra step for those humans:
after Microsoft finishes authenticating you, if you're a member of more
than one workspace the app shows you a **workspace picker**. Pick one,
land in it, work in it. If you want to switch to a different workspace,
sign out and sign back in. This mirrors Slack's, GitHub's, and Notion's
long-established pattern.

If you only belong to one workspace, you never see the picker — the app
picks it for you and drops you on the home page. If you belong to zero
workspaces, you land on the public browse view (same as today's
non-owner guests).

The app also now refuses to attach a fresh sign-in to an existing profile
if Microsoft says the sign-in's email isn't verified. This closes a class
of email-hijack risk that we currently protect against with a fragile
heuristic (guessing if the identity is fake based on the shape of its id).

**What breaks:** the schema shape change will re-attribute a small number
of duplicate rows in the staging database to one survivor row per human.
Your existing bookings, reviews, and audit-log entries all follow the
survivor row — same emails, same properties, same tenants, no visible
loss. The pre-migration state gets snapshotted so any misattribution can
be reversed manually.

**When your walk is unblocked:** at sub-commit M.13.6, once the tenant
picker's header pipeline is wired end-to-end. Everything after that is
sign-out flow polish + the close-out. The full slice takes 8 sub-commits.

**What sign-in looks like after this ships:** Sign in with Microsoft →
(if you're in >1 workspace) pick a workspace → arrive at home. That's it.

---

## §2 Detailed scope

### §2.1 Schema changes

**New table `identity.user_identities`.** Holds the (provider, external
id) → app-user-id mapping so a single `identity.users` row can be linked
to N identities from N providers (Entra today; Google + Microsoft
consumer in OPS.M.12; test-provider for the integration test suite).

```sql
CREATE TABLE identity.user_identities (
    id             uuid PRIMARY KEY,
    user_id        uuid NOT NULL
                     REFERENCES identity.users("Id") ON DELETE CASCADE,
    provider       text NOT NULL
                     CHECK (provider IN ('entra','google','microsoft','apple','test')),
    external_id    text NOT NULL,
    first_seen_at  timestamptz NOT NULL,
    last_seen_at   timestamptz NOT NULL,
    row_version    bigint NOT NULL,
    created_at     timestamptz NOT NULL,
    updated_at     timestamptz NOT NULL,
    CONSTRAINT user_identities_provider_extid_uq UNIQUE (provider, external_id)
);

CREATE INDEX ix_user_identities_user_id
    ON identity.user_identities (user_id);
```

**Modified table `identity.users`.**
- Add `is_email_verified` column (already exists as `email_verified` per
  `20260525190601_Init_IdentitySchema.cs:52`; the column is already
  correctly named `email_verified` — no add needed. Named in this doc
  as-is).
- Drop `b2c_object_id` column. Every real oid moves to
  `user_identities.external_id` with `provider='entra'` (backfill).
- Re-establish unique index on `lower(email)` with active-row filter.
  Postgres partial UNIQUE index is native — no `citext` extension needed
  (avoid the ext install for staging + prod). Slice 4's
  `Slice4_DropEmailUnique` (`20260622144933`) removed the previous
  full-column UNIQUE; this slice replaces it with a stricter partial-UNIQUE
  on the lowercased expression.

```sql
CREATE UNIQUE INDEX users_email_active_lower_uq
    ON identity.users (lower(email))
    WHERE deleted_at IS NULL;
```

**Modified FK-holder tables (6):** no schema change — the FK columns
(`booking.bookings.guest_user_id`, `reviews.reviews.guest_user_id`,
`messaging.threads.guest_user_id` + `.owner_user_id`,
`identity.audit_log.actor_user_id`, `notifications.notification_log.recipient_user_id`,
`identity.tenant_memberships.user_id`) already point at `identity.users.Id`
as `uuid`. Only the **row values** need rewriting during backfill (see
§3.4). `catalog.properties.owner_user_id` also references
`identity.users.Id` (per `PropertyConfiguration.cs:48`) — included as a
7th FK-holder for the backfill.

**Bundled sub-slice OPS.M.16 — `identity.migration_audit` table.** Per
`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md` §4.5. This slice's own backfill
(§3.4) is the first customer. Landed early in the sub-commit sequence so
the backfill INSERTs into it inside the same migration transaction:

```sql
CREATE TABLE identity.migration_audit (
    id              uuid PRIMARY KEY,
    migration_name  text NOT NULL,
    step_name       text NOT NULL,
    affected_count  integer NOT NULL,
    notes           text,
    executed_at     timestamptz NOT NULL DEFAULT NOW()
);

CREATE INDEX ix_migration_audit_migration_executed
    ON identity.migration_audit (migration_name, executed_at DESC);
```

### §2.2 Provisioning handler rewrite

Rename `ProvisionUserCommand` / `ProvisionUserHandler` →
`ProvisionOrLinkUserCommand` / `ProvisionOrLinkUserHandler`. New shape:

```csharp
public sealed record ProvisionOrLinkUserCommand(
    string Provider,          // 'entra' today; 'google'|'microsoft' post-M.12
    string ExternalId,        // the oid claim (or Google 'sub', etc.)
    string Email,
    bool   EmailVerified,
    string DisplayName) : IRequest<Guid>;
```

Algorithm (email-first; see §3.5 for full pseudocode):
1. Find `user_identities` by `(provider, external_id)` — cache-fast path.
2. Miss → find `users` by `lower(email)` where `deleted_at IS NULL`.
   - Match found + `email_verified=true` → INSERT `user_identities` row
     linking new identity to existing user. Return `user.Id`.
   - Match found + `email_verified=false` → throw
     `BusinessRuleViolationException("email_unverified_cannot_bind_profile")`.
     The controller catches → 403 (see §3.5 for the DTO shape).
   - No match → INSERT users row + first user_identities row in one
     transaction. Return `user.Id`.

Concurrency: rely on the `(provider, external_id)` UNIQUE constraint +
`lower(email) WHERE deleted_at IS NULL` UNIQUE. Two simultaneous
INSERTs of the same fresh identity → one wins, one gets 23505; catch +
retry the SELECT once. No advisory lock needed (see §3.5 subsection on
races).

### §2.3 Tenant selection mechanism

**Chosen: `X-Active-Tenant` HTTP header set by SPA on every API call.**

Full justification in §3.1. Short form: JWT stateless, session-per-tab
isolation (a natural fit — user cannot switch mid-session, but multi-tab
is allowed with the same active tenant per tab), zero server session
state, works cleanly with 100 % SPA architecture, no cookie plumbing.
The header is validated by middleware against DB membership on every
authenticated request.

### §2.4 Middleware changes

Before: `UserProvisioningMiddleware` reads oid → provisions users row
by oid → reads memberships → writes ONE `IsPrimary=true` tenant as
`app_tenant_id` claim.

After:
1. Reads `oid` + `emails`/`email` + `email_verified` + `name` claims
   from ClaimsPrincipal.
2. Sends `ProvisionOrLinkUserCommand(provider='entra', ExternalId=oid, ...)`.
3. Stamps `HttpContext.Items[HttpCurrentUser.AppUserIdItemKey] = userId`.
4. Reads all active `tenant_memberships` for userId — same query as today.
5. Reads `X-Active-Tenant` header from the request.
   - Header present + parses as GUID + matches one of the caller's active
     memberships → stamp `HttpContext.Items["VrBook:ActiveTenantId"] = tenantId`
     AND add the `app_tenant_id` claim.
   - Header present + does NOT match a membership → do NOT stamp; do NOT
     add claim. Downstream M.4 gate 403s naturally. (Middleware itself
     does NOT 403 — it lets `[Authorize]` + M.4 handle rejection so
     public browse endpoints without `[Authorize]` still work.)
   - Header absent → do NOT stamp; do NOT add claim. Same downstream
     behavior. (Public browse works; authed writes hit M.4 and 403.)
6. `IsPlatformAdmin` read — unchanged, stamped on `HttpContext.Items`.
7. **DROP** the Role-claim writes on ClaimsIdentity for memberships
   (previously `UserProvisioningMiddleware.cs:82-93`). This is the piece
   deferred to OPS.M.15 in the previous review; keeping it here would
   mean the M.13 slice STILL leaves the muddled role shape behind. We
   pull that cleanup forward one slice — see §3.3 for why.

`HttpCurrentUser.TenantId` changes:
- Before: reads `app_tenant_id` claim from ClaimsPrincipal.
- After: reads `HttpContext.Items["VrBook:ActiveTenantId"]` first
  (validated header); falls back to `app_tenant_id` claim ONLY for
  compatibility during the sub-commit sequence (dropped in M.13.6
  once the header pipeline is live).

### §2.5 API surface additions

**New `GET /api/v1/me/tenants`.** Returns the list of tenants the current
user is an active member of. Used by the SPA to decide 0-/1-/many-tenant
routing.

```csharp
[HttpGet("tenants")]                            // /api/v1/me/tenants
[Authorize]
[SwaggerOperation(Summary = "List the caller's active tenant memberships.")]
public async Task<ActionResult<MyTenantsResponse>> GetMyTenants(CancellationToken ct)
    => Ok(await mediator.Send(new GetMyTenantsQuery(), ct));
```

Response DTO:

```csharp
public sealed record MyTenantsResponse(IReadOnlyList<MyTenantMembershipDto> Tenants);
public sealed record MyTenantMembershipDto(
    Guid   TenantId,
    string TenantSlug,
    string TenantDisplayName,
    string Role,             // 'tenant_admin' | 'tenant_member'
    bool   IsPrimary,        // legacy field — retained for UI ordering hint
    string TenantStatus);    // 'Active' | 'Suspended' | 'Onboarding'
```

Handler joins `tenant_memberships` (user_id, active) → `tenants` (id).
Sort: `IsPrimary DESC, TenantDisplayName ASC` — primary first, then
alphabetical.

**No other new endpoints needed for the slice.** The M.4 gate + existing
admin endpoints keep working; the only change is where the tenant
identifier is sourced from.

### §2.6 Frontend changes

Files touched:
- **NEW** `web/src/app/select-tenant/page.tsx` — the picker.
- **NEW** `web/src/lib/tenants/activeTenant.ts` — sessionStorage helpers
  (`getActiveTenantId`, `setActiveTenantId`, `clearActiveTenantId`).
- **NEW** `web/src/lib/tenants/useMyTenants.ts` — react-query hook
  wrapping `GET /api/v1/me/tenants`.
- **EDIT** `web/src/lib/api/client.ts` — inject `X-Active-Tenant` header
  from sessionStorage on every authed fetch.
- **EDIT** `web/src/app/auth/callback/page.tsx` — after MSAL sets the
  active account, fetch `/me/tenants`, then:
  - 0 tenants → `router.replace('/')` (public browse; user sees signed-in
    header but no admin nav).
  - 1 tenant → `setActiveTenantId(tenants[0].tenantId)`; `router.replace(returnTo)`.
  - >1 tenants → `router.replace('/select-tenant?returnTo=' + returnTo)`.
- **EDIT** `web/src/app/auth/signout/page.tsx` — call
  `clearActiveTenantId()` before MSAL sign-out completes.
- **EDIT** `web/src/lib/auth/useAuth.ts` — expose `activeTenantId` so
  header / nav can render "Signed in as X in workspace Y".

Sub-commit sequence in §4 walks these in order.

### §2.7 Backfill migration

Full algorithm in §3.4. Summary of what runs in the M.13.4 migration:
1. Snapshot `identity.users` + all 7 FK-holder tables to `_pre_m13_snap` schema.
2. For each distinct `lower(email)` group: pick survivor row per §3.4 rules.
3. Rewrite FKs across 7 tables → survivor id.
4. Populate `user_identities` from each row's `b2c_object_id` (survivor rows
   get one identity row per historical oid).
5. Soft-delete non-survivor rows (`deleted_at = NOW()`) — keeps identity
   rows resolvable by id for any orphan audit references we might have missed.
6. Drop `identity.users.b2c_object_id` column.
7. Create the `users_email_active_lower_uq` partial UNIQUE index.
8. INSERT one row per step into `identity.migration_audit`.

### §2.8 Migration audit — sub-slice or separate slice?

**Decision:** bundle as sub-commit M.13.2 (schema) + used by M.13.4
(backfill). The M.13 backfill is the first migration where lack of audit
would leave us blind post-deploy, per Ev-F in the previous review — it
would be inconsistent to ship this backfill without the audit table. The
admin endpoint that queries it (`GET /api/v1/admin/platform/migration-audit`)
ships as part of M.13.2 too so the row visibility works day-one.

Architect's original recommendation (§4.5 previous review) was
"sub-slice"; this doc confirms and lands it inside M.13.

---

## §3 Technical decisions with justification

### §3.1 Session mechanism for active tenant

**Options considered:**
- **(a) `X-Active-Tenant` HTTP header** set by SPA on every API call.
- **(b) Session cookie** set by an API `POST /api/v1/me/pick-tenant` call.
- **(c) Short-lived server-issued session token** carrying tenant in a
  claim.
- **(d) URL path prefix** `/t/{tenantId}/...` on every route.
- **(e) Custom claim requested at token acquisition time** (MSAL scope
  parameterization).

**Chosen: (a) `X-Active-Tenant` HTTP header.**

**Why:**

1. **Stateless architecture is already the current shape.** API is Bearer
   + JWT; no session cookie today; adding one for tenant selection
   introduces a whole new state axis that must be invalidated on sign-out
   and revoked on member-of-tenant revocation. The header approach adds
   zero server state.

2. **Per-tab isolation is a free win.** `sessionStorage` is per-tab in
   every browser (see MDN's ["Window.sessionStorage — Data is retained
   only for the duration of the page session"](https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage);
   MSAL already uses it — `msalConfig.ts:46`). Two tabs, same
   sign-in, same email, different tenants selected → each tab carries
   its own active tenant. The user's constraint is "cannot switch
   mid-session"; two tabs are two sessions. **This means two tabs CAN
   sit in different tenants**, which is a legitimate power-user pattern
   for someone managing multiple properties. If we wanted to
   force-uniform across tabs we'd use `localStorage` + a storage event
   — this doc chooses `sessionStorage` for the more powerful default.
   Recorded as decision because the user might prefer force-uniform;
   see §7 open questions.

3. **Slack / GitHub / Notion pattern verified.**
   - Slack: workspace is in the URL (`{workspace}.slack.com`) plus a
     `session_id` cookie scoped to that subdomain. Sign-out clears the
     cookie. Sign-in flow goes to a workspace-picker landing page for
     multi-workspace users.
   - GitHub: no per-org "active" state — org lives in URL segments
     (`github.com/{org}/...`) and the user's cookie is one identity
     across all orgs. This is closer to option (d).
   - Notion: workspace picker on sign-in, then `X-NotionSpace` header
     set on API calls from the SPA (verified in devtools). Sign-out
     clears sessionStorage.
   - This slice's chosen pattern matches Notion closely — an SPA + JWT
     Bearer + an "active workspace" header. GitHub's URL-based pattern
     was considered as option (d) but rejected because it would break
     every existing `/admin/...` URL bookmark.

4. **Cookies not chosen (option b) because:**
   - Bearer + cookie is a mixed auth model that the SPA client already
     tries to avoid (previous review §2.1 change #1 wants to drop
     `credentials: 'include'` post-DevAuth retirement). Adding a
     cookie now walks the SPA backwards.
   - Sign-out has two things to clear (MSAL sessionStorage + tenant
     cookie); one is more reliable.
   - Cookie doesn't survive an `Authorization: Bearer` override without
     `credentials: include`; would force us to keep that flag.

5. **Not option (c) because** it requires wiring a new short-lived
   token mint endpoint + refresh logic. Adds one whole new auth
   surface for something a stateless header solves.

6. **Not option (d) — URL path prefix** because:
   - Every existing route bookmark (`/admin/bookings/...`,
     `/admin/onboarding/...`) breaks unless we ship redirects.
   - Deep-link handling becomes more complex (see §3.2 deep-link
     analysis).

7. **Not option (e) — custom scope** because MSAL doesn't let the
   client re-mint tokens on demand without user interaction; and the
   API would need to validate the scope's tenant claim on every
   request AGAINST the DB membership anyway. All complexity, no
   payoff.

**Header shape:** `X-Active-Tenant: 00000000-0000-0000-0000-000000000001`.
Lowercase UUID, standard 8-4-4-4-12 form. Rejected shapes: tenant
slug (mutable), name (mutable + unicode), array of GUIDs (unnecessary —
user can only be active in one). Header name uses `X-` prefix per
convention despite RFC 6648 deprecation — no strong opinion here;
consistent with existing `X-Test-Persona` used in `TwoTenantDevAuthHandler`.

**Storage on SPA:** `sessionStorage['vrbook:active-tenant-id']`.
Cleared on sign-out. **NOT `localStorage`** — a returning user next
morning re-picks (either auto if 1 tenant, or via picker if >1). This
matches the "sign out to change tenant" rule the user set.

**Tab-multiplex behavior — user's stated rule:** "user cannot switch
mid-session". `sessionStorage` is per-tab; opening a new tab has no
active tenant → picker or auto-pick fires on first API call. This is
the correct behavior: **each tab's session is independent**. If the
user prefers force-uniform across tabs (any new tab inherits the
current tenant) — flag noted in §7.

### §3.2 Tenant-picker page routing

**Chosen: dedicated `/select-tenant` page.**

**Why:**

1. **Auto-pick on 1 tenant, no UI at all.** Never landing on a modal or
   inline picker for the 90 % of users who belong to one workspace.
   Modal (option b) always renders SOMETHING even for the 1-tenant
   case (unless suppressed by client-side conditional), and inline on
   `/` (option c) means the home page has a conditional-rendered picker
   which visually clutters the guest-browse view.

2. **Deep-link handling is a first-class case, not an afterthought.**
   If a user has `https://web/admin/bookings/{id}` bookmarked and
   loads it before picking a tenant:
   - Callback page (`/auth/callback`) is the primary landing point
     right after MSAL — the `returnTo` param carries the desired URL.
   - If active-tenant is unset AND user has >1 tenant → callback
     redirects to `/select-tenant?returnTo=/admin/bookings/{id}`.
   - After pick → `router.replace(returnTo)`.
   - If active-tenant is unset AND user has 1 tenant → callback
     auto-picks + navigates to returnTo. Same as one hop of a picker
     without the picker.
   - Direct load of `/admin/bookings/{id}` with sessionStorage empty
     (returning to open tab) → API call for the page's data fires
     without `X-Active-Tenant` → middleware doesn't stamp
     ActiveTenantId → M.4 gate 403s → page-level error boundary
     shows "Please pick a workspace" with a button linking to
     `/select-tenant?returnTo=<currentUrl>`. **This is the deep-link
     recovery UX.**

3. **The user cannot switch mid-session** simplifies the picker's
   contract dramatically: it's a one-way route. Once picked, the
   picker page redirects to returnTo and is not intended to be
   revisited except via sign-out → sign-in.

4. **Picker page has a "sign in as different user" link** for the case
   where a user landed here by accident and actually wanted to switch
   accounts. Calls `useAuth().signOut()` directly.

**Picker UX (draft):**

```
+---------------------------------------------+
|  Pick a workspace                           |
|                                             |
|  You're a member of these workspaces.       |
|  Choose one to continue.                    |
|                                             |
|  [ Sunset Bays Cottage        tenant_admin ]|
|  [ Blue Ridge Rentals         tenant_admin ]|
|  [ Mountain Escapes           tenant_member]|
|                                             |
|  Signed in as niroshanaks@gmail.com         |
|  [ Sign in as a different user ]            |
+---------------------------------------------+
```

Sorting: `IsPrimary` first, then `TenantDisplayName` ascending. Displays
the role next to the name so a `tenant_member` in a workspace can pick
knowing what they'll be able to do.

**Rejected: modal on `/`** — always renders a fragment even for the
1-tenant case; complicates the home page's own routing (which is public
+ signed-in dual-mode).

**Rejected: inline on account/dashboard** — same problem plus routes
the picker deep in the app, past a lot of shells that expect an active
tenant.

### §3.3 API enforcement of active tenant

**Chosen: middleware validates `X-Active-Tenant` on every authenticated
request; the M.4 `TenantAuthorizationBehavior` remains the primary
enforcement point.**

**Why:**

1. **Single-source-of-truth for the active-tenant read.** All handlers
   read `ICurrentUser.TenantId` today. That accessor now reads
   `HttpContext.Items["VrBook:ActiveTenantId"]` (populated by the
   middleware ONLY when the header validated against DB membership).
   No handler-code changes required. The M.4 behavior's null-check on
   `currentUser.TenantId` still fires 403 for authed writes with no
   active tenant — the exact behavior we want.

2. **Public / anonymous endpoints keep working.** Middleware doesn't
   throw when the header is absent; only downstream `[Authorize]` +
   M.4 do. Property browse (public, no `[Authorize]`) → no header,
   no rejection. Bookings admin (`[Authorize]` + `ITenantScoped`) →
   no header + no active-tenant read → M.4 403 with the same audit-log
   shape we have today.

3. **Bookmarked URL in wrong tenant — the "URL-in-tenant-B while
   member-of-A" case.** Setup: user is a member of tenant A only.
   They have `https://web/admin/bookings/{bookingIdFromTenantB}`
   bookmarked (someone shared the link). They sign in, active-tenant
   auto-picks to A, they navigate to the URL. The API's booking read
   endpoint's handler queries the booking by id, then the row-level
   check in the handler (existing pattern) rejects because
   `booking.TenantId != currentUser.TenantId`. Response: **404**
   (the standard NotFound response — do not leak tenant B's existence).
   This is what happens today for cross-tenant reads; unchanged.

4. **Bookmarked URL where the user IS a member of tenant B but
   didn't pick it.** User is member of both A and B. Picked A. URL
   is a tenant-B booking. Read handler → 404 (same as above).
   Discovery UX: the front-end catches 404 on admin routes AND
   detects the user has >1 tenant → shows an inline "You may need to
   switch workspace. Sign out and pick a different workspace" panel
   with a `Sign out` button. This is deferred to a follow-up UX
   polish sub-commit — see M.13.7 in §4.

5. **Rejected option (b) — each controller reads the header.** DRY
   violation; every controller would gain a hard-to-test header
   check; the M.4 behavior would still exist for the auth
   enforcement. Two enforcement points is a bug factory.

6. **Rejected option (c) — TenantAuthorizationBehavior gains a
   "must have picked" check.** Semantically same as our chosen
   design but conflates two responsibilities (validating header +
   validating tenant match). Cleaner to keep the middleware
   responsible for parsing/validating the header and stamping
   `Items`, and M.4 responsible for the equality check.

**Consequence: pull the ClaimsIdentity role-write drop forward from
M.15 into this slice.** Reason: the middleware is already being
restructured here. Leaving the role-write in and cleaning it up in
M.15 means the middleware ships in an intermediate state where it
BOTH stamps `Items["ActiveTenantId"]` AND writes `Role` claims for
memberships. Two mechanisms in flight is bug-prone. Pull the write
drop into M.13. `HasTenantRole` on `ICurrentUser` already checks
`user.IsInRole(role) || user.HasClaim(c => (c.Type == ClaimTypes.Role || c.Type == "roles") && ...)`
(`HttpCurrentUser.cs:137-140`); it will start returning `false` for
memberships once we drop the write. **We need to replace it with a
membership-set read.** Concrete new plumbing:

```csharp
// ICurrentUser gains:
IReadOnlyDictionary<Guid, string> MembershipRoles { get; }
// Stamped by middleware from tenant_memberships DB read.
// Consumed by HttpCurrentUser.HasTenantRole and any handler that needs to know
// "what role does this user have in tenant X".
```

Documented as decision here so the M.15 slice becomes even simpler
(just App Role portal deletion + `IsOwner`/`IsAdmin` legacy column drop).

### §3.4 Backfill migration algorithm — email-canonical collapse

**Complete algorithm, executable as one migration transaction.**

**Step 1 — snapshot to a rollback schema.**

```sql
CREATE SCHEMA IF NOT EXISTS _pre_m13_snap;
CREATE TABLE _pre_m13_snap.users        AS TABLE identity.users;
CREATE TABLE _pre_m13_snap.tenant_memberships AS TABLE identity.tenant_memberships;
CREATE TABLE _pre_m13_snap.audit_log    AS TABLE identity.audit_log;
CREATE TABLE _pre_m13_snap.bookings     AS SELECT "Id", guest_user_id FROM booking.bookings;
CREATE TABLE _pre_m13_snap.reviews      AS SELECT "Id", guest_user_id FROM reviews.reviews;
CREATE TABLE _pre_m13_snap.threads      AS SELECT "Id", guest_user_id, owner_user_id FROM messaging.threads;
CREATE TABLE _pre_m13_snap.notification_log AS SELECT "Id", recipient_user_id FROM notifications.notification_log;
CREATE TABLE _pre_m13_snap.properties   AS SELECT "Id", owner_user_id FROM catalog.properties;
INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(), 'OpsM13_UserIdentities_And_EmailCanonical', 'snapshot',
        (SELECT COUNT(*) FROM _pre_m13_snap.users), 'pre-migration snapshot', NOW());
```

The `_pre_m13_snap` schema is left in place post-migration for 30 days.
A follow-up cleanup migration (`OpsM13a_Drop_PreM13_Snapshot`) drops it
after the operator confirms the walk passes. Runbook update lands with
M.13 close-out.

**Step 2 — survivor pick per email group.**

Survivor precedence:
1. `is_platform_admin = true` beats `false`.
2. `deleted_at IS NULL` beats soft-deleted (only active rows are
   considered; soft-deleted are inherently non-survivors and get
   re-linked to the survivor for audit-continuity).
3. Highest active-membership count (tie-break on multiple PAs — an
   edge case but explicit).
4. Oldest `created_at` (deterministic tie-break).

Critically: **oldest CreatedAt, NOT newest.** F11.7.6.4's data-heal
migration picked oldest CreatedAt as well (`20260701000000_..._SoftDeleteDuplicateUsers.cs:44`)
but the surviving row for `niroshanaks` in that heal was the wrong
one — the F11.7.6 doc §3.6 documented that historical DevAuth persona
rows (created earlier) beat real-Entra rows (created later) under
this rule, but the DevAuth persona rows lack the memberships. This
slice's tie-break puts `is_platform_admin` first, THEN active-member
count, THEN CreatedAt. That correctly picks the real-Entra row with
memberships over the DevAuth persona row without.

```sql
WITH ranked AS (
    SELECT
        u."Id",
        lower(u.email) AS email_key,
        ROW_NUMBER() OVER (
            PARTITION BY lower(u.email)
            ORDER BY u.is_platform_admin DESC,
                     (SELECT COUNT(*) FROM identity.tenant_memberships tm
                       WHERE tm.user_id = u."Id" AND tm.deleted_at IS NULL) DESC,
                     u.created_at ASC
        ) AS rn
    FROM identity.users u
    WHERE u.deleted_at IS NULL
)
SELECT "Id", email_key, rn FROM ranked;
-- survivor per email group = rn=1
```

Materialize into a temporary work table so subsequent steps can join
against it:

```sql
CREATE TEMP TABLE _work_survivor_map AS
SELECT
    u."Id" AS non_survivor_id,
    s."Id" AS survivor_id
FROM (
    SELECT "Id", lower(email) AS email_key, ROW_NUMBER() OVER (
        PARTITION BY lower(email)
        ORDER BY is_platform_admin DESC,
                 (SELECT COUNT(*) FROM identity.tenant_memberships tm
                   WHERE tm.user_id = users."Id" AND tm.deleted_at IS NULL) DESC,
                 created_at ASC
    ) AS rn FROM identity.users users
    WHERE deleted_at IS NULL
) u
JOIN (
    SELECT "Id", lower(email) AS email_key FROM identity.users
    WHERE deleted_at IS NULL
) s ON s.email_key = u.email_key
   AND s."Id" = (
     SELECT "Id" FROM identity.users u2
     WHERE lower(u2.email) = u.email_key AND u2.deleted_at IS NULL
     ORDER BY is_platform_admin DESC,
              (SELECT COUNT(*) FROM identity.tenant_memberships tm
                WHERE tm.user_id = u2."Id" AND tm.deleted_at IS NULL) DESC,
              created_at ASC
     LIMIT 1
   )
WHERE u.rn > 1;
```

(Simpler shape possible; final migration SQL will use a CTE + WINDOW
for compactness.)

**Step 3 — FK rewrites across the 7 FK-holder tables.**

Ordered by lowest FK count first (so failures happen on small tables
first for debuggability):

```sql
-- 3a. tenant_memberships (many→one; may collide on unique constraint)
UPDATE identity.tenant_memberships tm
   SET user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE tm.user_id = m.non_survivor_id
   AND NOT EXISTS (
       SELECT 1 FROM identity.tenant_memberships existing
       WHERE existing.user_id = m.survivor_id
         AND existing.tenant_id = tm.tenant_id
         AND existing.deleted_at IS NULL
   );
-- The NOT EXISTS clause avoids the (user_id, tenant_id) partial-unique
-- collision if survivor + non-survivor were both members of the same
-- tenant. Duplicates left on non-survivor rows get soft-deleted at
-- step 5.

-- 3b. booking.bookings
UPDATE booking.bookings b
   SET guest_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE b.guest_user_id = m.non_survivor_id;

-- 3c. reviews.reviews (guest_user_id per ReviewConfiguration.cs:22)
UPDATE reviews.reviews r
   SET guest_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE r.guest_user_id = m.non_survivor_id;

-- 3d. messaging.threads (BOTH guest and owner FKs)
UPDATE messaging.threads t
   SET guest_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE t.guest_user_id = m.non_survivor_id;
UPDATE messaging.threads t
   SET owner_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE t.owner_user_id = m.non_survivor_id;

-- 3e. notifications.notification_log
UPDATE notifications.notification_log n
   SET recipient_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE n.recipient_user_id = m.non_survivor_id;

-- 3f. identity.audit_log (nullable FK — only rewrite non-nulls)
UPDATE identity.audit_log a
   SET actor_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE a.actor_user_id = m.non_survivor_id;

-- 3g. catalog.properties (per PropertyConfiguration.cs:48)
UPDATE catalog.properties p
   SET owner_user_id = m.survivor_id
  FROM _work_survivor_map m
 WHERE p.owner_user_id = m.non_survivor_id;
```

Each `UPDATE` INSERTs one row into `identity.migration_audit` with
the affected_count.

**Step 4 — populate `user_identities` from historical `b2c_object_id`.**

```sql
-- Every active (post-collapse) users row gets a user_identities row
-- for its current b2c_object_id (real Entra oid or DevAuth persona oid).
INSERT INTO identity.user_identities
    (id, user_id, provider, external_id, first_seen_at, last_seen_at,
     row_version, created_at, updated_at)
SELECT
    gen_random_uuid(),
    u."Id",
    CASE WHEN u.b2c_object_id ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
         THEN 'entra'
         ELSE 'test'  -- DevAuth persona oids (dev-owner-*, dev-guest-*, dev-admin-*)
    END,
    u.b2c_object_id,
    u.created_at,
    COALESCE(u.last_login_at, u.created_at),
    1, NOW(), NOW()
FROM identity.users u
WHERE u.deleted_at IS NULL;

-- Also link non-survivor (about-to-be-soft-deleted) rows' oids to
-- their survivor, so later sign-ins under the historical DevAuth oid
-- still resolve to the survivor row.
INSERT INTO identity.user_identities
    (id, user_id, provider, external_id, first_seen_at, last_seen_at,
     row_version, created_at, updated_at)
SELECT
    gen_random_uuid(),
    m.survivor_id,
    CASE WHEN u.b2c_object_id ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
         THEN 'entra'
         ELSE 'test'
    END,
    u.b2c_object_id,
    u.created_at,
    COALESCE(u.last_login_at, u.created_at),
    1, NOW(), NOW()
FROM identity.users u
JOIN _work_survivor_map m ON m.non_survivor_id = u."Id"
ON CONFLICT (provider, external_id) DO NOTHING;
-- ON CONFLICT: if two non-survivor rows shared an oid (shouldn't happen
-- given today's unique index on b2c_object_id, but defense) skip.
```

**Step 5 — soft-delete non-survivors.**

```sql
UPDATE identity.users u
   SET deleted_at = NOW(),
       updated_at = NOW()
  FROM _work_survivor_map m
 WHERE u."Id" = m.non_survivor_id;
```

**Step 6 — drop `b2c_object_id` column + unique index.**

```sql
DROP INDEX IF EXISTS identity."IX_users_b2c_object_id";
ALTER TABLE identity.users DROP COLUMN b2c_object_id;
```

**Step 7 — create the new email-canonical unique index.**

```sql
CREATE UNIQUE INDEX users_email_active_lower_uq
    ON identity.users (lower(email))
    WHERE deleted_at IS NULL;
```

**Step 8 — final audit + verify.**

```sql
INSERT INTO identity.migration_audit
    (id, migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(), 'OpsM13_UserIdentities_And_EmailCanonical',
        'complete',
        (SELECT COUNT(*) FROM identity.users WHERE deleted_at IS NULL),
        'active user rows post-collapse', NOW());
```

**Expected effect on `niroshanaks`'s data (staging):**

- 4 broken user rows collapse to 1 survivor row (the one with
  `is_platform_admin=true` OR — if none is PA — the one with the most
  memberships; if all zero, the oldest CreatedAt).
- 3 DevAuth persona rows (`dev-owner-*`, `dev-guest-*`, `dev-admin-*`
  with rewritten email per Ev-B) get collapsed under the survivor
  matching their (rewritten) email — spec: each DevAuth row was
  rewritten to have `niroshanaks@gmail.com` OR to `dev-owner-00000000@vrbook.local`.
  Rows that still hold non-`niroshanaks` emails (DevAuth stub emails)
  form their own email groups and become their own survivors (each is
  a single-row group). Rows that hold `niroshanaks@gmail.com`
  collapse under the same survivor. **All 3 DevAuth persona rows are
  soft-deleted as non-survivors**; their oids land in `user_identities`
  linked to the survivor. That means a future DevAuth-scheme sign-in
  (once DevAuth is fully retired in OPS.M.14 this is moot; interim if
  DevAuth is briefly re-enabled it still resolves to the correct row).
- Audit-log rewrites: ~50-200 rows for `niroshanaks`-attributed
  actor_user_id across 48 hours of walks. Acceptable noise — the
  migration_audit row records the exact count. No sensible way to
  "reduce" audit noise here without losing the actor→survivor
  attribution link. Documented.

**Rollback runbook (staging):**

```powershell
# Manual restore from _pre_m13_snap using pg_dump/pg_restore is
# overkill. Use SQL restore in a single transaction:
BEGIN;
DELETE FROM identity.user_identities;
DELETE FROM identity.users;
INSERT INTO identity.users SELECT * FROM _pre_m13_snap.users;
DELETE FROM identity.tenant_memberships;
INSERT INTO identity.tenant_memberships SELECT * FROM _pre_m13_snap.tenant_memberships;
-- Restore each FK-holder table:
UPDATE booking.bookings b SET guest_user_id = s.guest_user_id
  FROM _pre_m13_snap.bookings s WHERE b."Id" = s."Id";
UPDATE reviews.reviews r SET guest_user_id = s.guest_user_id
  FROM _pre_m13_snap.reviews s WHERE r."Id" = s."Id";
-- ... etc for threads, notification_log, audit_log, properties.
-- Re-create b2c_object_id column (from users snap):
ALTER TABLE identity.users ADD COLUMN b2c_object_id text;
UPDATE identity.users u SET b2c_object_id = s.b2c_object_id
  FROM _pre_m13_snap.users s WHERE u."Id" = s."Id";
CREATE UNIQUE INDEX "IX_users_b2c_object_id" ON identity.users (b2c_object_id);
DROP INDEX identity.users_email_active_lower_uq;
COMMIT;

-- Then roll the API deployment back to pre-M.13 revision.
```

The rollback is destructive to any post-migration writes (bookings
placed AFTER the M.13.4 migration ran under the new survivor id will
have their `guest_user_id` restored to a pre-migration id that may not
match the new writer's identity). For staging this is acceptable —
staging is single-tenant + `niroshanaks`. For prod (not this slice)
the rollback strategy must be different; documented in §7 as an open
question for the prod cutover.

### §3.5 New provisioning handler — email-first algorithm

**Full pseudocode:**

```csharp
public async Task<Guid> Handle(ProvisionOrLinkUserCommand cmd, CancellationToken ct)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Provider);
    ArgumentException.ThrowIfNullOrWhiteSpace(cmd.ExternalId);
    ArgumentException.ThrowIfNullOrWhiteSpace(cmd.Email);

    var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();

    // Branch 1 — identity hit. The cache-fast happy path.
    var identity = await _db.UserIdentities.AsTracking()
        .FirstOrDefaultAsync(i => i.Provider == cmd.Provider && i.ExternalId == cmd.ExternalId, ct);

    if (identity is not null)
    {
        identity.LastSeenAt = _clock.UtcNow;
        var user = await _db.Users.AsTracking()
            .FirstAsync(u => u.Id == identity.UserId, ct);
        user.RefreshFromLogin(cmd.DisplayName, cmd.EmailVerified);
        // NOTE: we do NOT rewrite user.email from the token here.
        // Email can drift on Entra side (user changes their email);
        // if that happens we surface it via a follow-up "please confirm
        // your email change" flow (out of scope for this slice).
        await _db.SaveChangesAsync(ct);
        return user.Id;
    }

    // Branch 2 — identity miss + email hit.
    var existingUser = await _db.Users.AsTracking()
        .FirstOrDefaultAsync(u => u.EmailNormalized == normalizedEmail && u.DeletedAt == null, ct);
    // ^ EmailNormalized is a computed column mapped to lower(email);
    //   OR use `EF.Functions.ILike` + `u.Email == cmd.Email` — pick one
    //   at implementation; DB-level UNIQUE lives on lower(email).

    if (existingUser is not null)
    {
        if (!cmd.EmailVerified)
        {
            // §3.5 guard — the user's approved decision. Refuse to bind
            // an existing profile to a fresh unverified identity.
            throw new BusinessRuleViolationException(
                "email_unverified_cannot_bind_profile",
                $"Cannot bind provider '{cmd.Provider}' identity to existing profile: email '{cmd.Email}' is not verified. Verify your email with your identity provider and sign in again.");
        }

        var newIdentity = UserIdentity.Create(
            existingUser.Id, cmd.Provider, cmd.ExternalId, _clock.UtcNow);
        _db.UserIdentities.Add(newIdentity);
        existingUser.RefreshFromLogin(cmd.DisplayName, cmd.EmailVerified);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "user_identities_provider_extid_uq"))
        {
            // Race: another request just inserted the same identity.
            // Re-query — Branch 1 hits.
            _db.ChangeTracker.Clear();
            var raced = await _db.UserIdentities
                .FirstAsync(i => i.Provider == cmd.Provider && i.ExternalId == cmd.ExternalId, ct);
            return raced.UserId;
        }
        return existingUser.Id;
    }

    // Branch 3 — identity miss + email miss. Fresh provision.
    var user2 = User.Provision(normalizedEmail, cmd.DisplayName, cmd.EmailVerified);
    var firstIdentity = UserIdentity.Create(user2.Id, cmd.Provider, cmd.ExternalId, _clock.UtcNow);
    _db.Users.Add(user2);
    _db.UserIdentities.Add(firstIdentity);
    try
    {
        await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex, "users_email_active_lower_uq"))
    {
        // Race on fresh email: another request just created the users row.
        _db.ChangeTracker.Clear();
        var raced = await _db.Users
            .FirstAsync(u => u.EmailNormalized == normalizedEmail && u.DeletedAt == null, ct);
        // Verify email_verified, then link the identity (retry).
        if (!cmd.EmailVerified) throw new BusinessRuleViolationException(
            "email_unverified_cannot_bind_profile", ...);
        var retryIdentity = UserIdentity.Create(raced.Id, cmd.Provider, cmd.ExternalId, _clock.UtcNow);
        _db.UserIdentities.Add(retryIdentity);
        await _db.SaveChangesAsync(ct);
        return raced.Id;
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex, "user_identities_provider_extid_uq"))
    {
        _db.ChangeTracker.Clear();
        var raced = await _db.UserIdentities
            .FirstAsync(i => i.Provider == cmd.Provider && i.ExternalId == cmd.ExternalId, ct);
        return raced.UserId;
    }
    return user2.Id;
}

private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
    ex.InnerException is Npgsql.PostgresException pex
    && pex.SqlState == "23505"
    && pex.ConstraintName == constraintName;
```

**Provider inference — how the handler knows what to pass:**

The middleware inspects the `iss` (issuer) claim:
- Entra External ID token → `iss` matches
  `https://{tid}.ciamlogin.com/{tid}/v2.0` → `provider = "entra"`.
- Post-OPS.M.12: Google token federated through Entra still has
  `iss = https://{tid}.ciamlogin.com/{tid}/v2.0` (per token-auth
  deep-dive §2.5) — the `idp` claim carries `google.com`. The
  middleware can OPTIONALLY inspect `idp` and pass `provider =
  "google"` for finer accounting. **For this slice: always pass
  `provider = "entra"`.** OPS.M.12 lands the `idp`-based
  discrimination.
- Test (`TestAuthenticationHandler` in OPS.M.14) → `provider = "test"`.

Helper in `HttpCurrentUser` or the middleware directly:

```csharp
private static string InferProvider(ClaimsPrincipal user) => "entra";
// Post-M.12: switch on user.FindFirstValue("idp") for social IdPs.
```

**Multi-tab race handling:**

Two tabs sign in simultaneously as a fresh oid → both hit Branch 3 →
both try to INSERT `users`. The Postgres partial-UNIQUE on
`lower(email) WHERE deleted_at IS NULL` catches the second → 23505 →
handler catches → re-queries → returns the winning user id. Total
worst case: one extra SELECT round-trip. Advisory lock rejected —
adds a lock contention point on every fresh sign-in for a scenario
that only happens on rapid clean-slate sign-ins.

Same shape for two identities racing: `user_identities_provider_extid_uq`
catches the second insert.

**Guardrail lifecycle:**

The old `IsRealEntraOid` GUID-shape guardrail in
`ProvisionUserHandler.cs:126` is DELETED — its purpose was to detect
"two-real-Entra oids colliding on email" as the email-hijack case.
Post-M.13 that class of collision is impossible (identity is (provider,
external_id); collision on shared email is a link, not a claim). The
verified-email guard replaces it with a cleaner semantic.

### §3.6 Tenant-picker UX flow diagram

**Text sequence for a full sign-in flow (post-M.13):**

```
[User clicks Sign In on any page — <SignInButton/> triggers useAuth.signIn()]
   → File: web/src/lib/auth/useAuth.ts:43-45 (unchanged)
   → MSAL loginRedirect(loginRequest)  →  Entra /authorize endpoint
   → User authenticates (email + OTP, or later Google/Microsoft)
   → Entra redirects browser to https://web/auth/callback?code=...

[/auth/callback page]
   → File: web/src/app/auth/callback/page.tsx (MODIFIED in M.13.5)
   → MSAL processes redirect, gets tokens, sets active account
   → Fetches GET /api/v1/me/tenants  (this is the NEW endpoint)
     ├─ 0 tenants → clearActiveTenantId(); router.replace('/')
     ├─ 1 tenant  → setActiveTenantId(t[0].tenantId);
     │              router.replace(searchParams.get('returnTo') ?? '/')
     └─ >1 tenants → router.replace('/select-tenant?returnTo=' + returnTo)

[/select-tenant page — only reached in the multi-tenant case]
   → File: web/src/app/select-tenant/page.tsx (NEW in M.13.5)
   → Fetches (cached) list from useMyTenants() hook
   → Renders picker; user clicks tenant
   → setActiveTenantId(chosen.tenantId)
   → router.replace(searchParams.get('returnTo') ?? '/')

[User navigates the app — /admin/bookings, /account/bookings, etc.]
   → Component code calls apiFetch('/api/v1/admin/bookings', ...)
   → apiFetch reads getActiveTenantId() from sessionStorage
     File: web/src/lib/api/client.ts (MODIFIED in M.13.6)
   → If active-tenant is set → headers['X-Active-Tenant'] = tenantId
   → Fetch fires with both Authorization + X-Active-Tenant

[API side]
   → File: src/VrBook.Api/Program.cs (order unchanged)
   → app.UseAuthentication() validates JWT (Program.cs:165)
   → app.UseIdentityModule() runs UserProvisioningMiddleware (Program.cs:166)
     File: UserProvisioningMiddleware.cs (MODIFIED in M.13.3 + M.13.6)
     → Provisions or links user via ProvisionOrLinkUserCommand
     → Stamps Items[AppUserIdItemKey] = userId
     → Reads Request.Headers["X-Active-Tenant"]
     → Validates parses-as-GUID + matches an active tenant_memberships row
     → If valid: stamps Items["VrBook:ActiveTenantId"] = tenantId
                 + adds app_tenant_id claim + stamps MembershipRoles
     → If invalid or missing: does not stamp; no throw
   → app.UseAuthorization() runs (Program.cs:167)
   → Handler runs; TenantAuthorizationBehavior reads currentUser.TenantId
     File: TenantAuthorizationBehavior.cs (UNCHANGED)
     → currentUser.TenantId sourced from Items["VrBook:ActiveTenantId"]
       (via HttpCurrentUser.TenantId — MODIFIED in M.13.6)
     → 403 if null or mismatch, per today's behavior

[Sign-out flow]
   → File: web/src/app/auth/signout/page.tsx (MODIFIED in M.13.7)
   → clearActiveTenantId() BEFORE msalInstance.logoutRedirect()
     — ensures no stale tenant persists past sign-out
```

### §3.7 Sign-out flow

**File touched:** `web/src/app/auth/signout/page.tsx` +
`web/src/lib/auth/useAuth.ts` (`signOut` callback).

**Concrete plumbing:**

```typescript
// web/src/lib/tenants/activeTenant.ts (NEW)
const KEY = 'vrbook:active-tenant-id';
export const getActiveTenantId = (): string | null =>
  typeof window === 'undefined' ? null : sessionStorage.getItem(KEY);
export const setActiveTenantId = (id: string): void => {
  if (typeof window !== 'undefined') sessionStorage.setItem(KEY, id);
};
export const clearActiveTenantId = (): void => {
  if (typeof window !== 'undefined') sessionStorage.removeItem(KEY);
};

// useAuth.ts signOut — call clearActiveTenantId BEFORE MSAL logout
const signOut = useCallback(() => {
  clearActiveTenantId();
  void instance.logoutRedirect();
}, [instance]);
```

**Multi-tab sign-out behavior:**
`sessionStorage` is per-tab, so a sign-out in tab A does not clear
tab B's active-tenant. Tab B still has an MSAL token (valid until
expiry OR until the user hits an interactive scenario). Once tab B's
token expires + `acquireTokenSilent` fails → `InteractionRequiredAuthError`
→ `acquireTokenRedirect` bounces to Entra → Entra detects the
sign-out on the shared browser (via the ESTSAUTH cookie which IS
shared across tabs) → forces a fresh sign-in → returns → auth
callback flow starts over → re-picks tenant. Correct behavior.

**Force-sign-out-all-tabs pattern (deferred):** if we ever want
tab-A sign-out to force tab-B to sign out too, add a
`localStorage` "signout epoch" bump on sign-out + a `storage` event
listener on every page. Not in this slice. Noted in §7.

---

## §4 Sub-commit sequence

Each sub-commit ships CI-green. RED commits are marked as such and
labelled explicitly. Sequence names follow the master TODO convention
(`Slice OPS.M.13.N: <summary>`).

```
M.13.1 — RED. Failing tests that pin the target shape.
    Files:
    - tests/VrBook.Architecture.Tests/OpsM13_EmailCanonicalUsersShapeTests.cs (NEW)
      * Assert `identity.users` has NO b2c_object_id column (via reflection
        on User + EF model snapshot).
      * Assert `identity.user_identities` table exists in the model
        snapshot with the expected columns + UNIQUE.
      * Assert User has NO GetByB2CObjectIdAsync method-shape reference
        in ProvisionOrLinkUserHandler source text.
    - tests/VrBook.Architecture.Tests/OpsM13_ProvisioningEmailFirstShapeTests.cs (NEW)
      * Assert ProvisionOrLinkUserHandler source references
        `Provider` + `ExternalId` + `EmailVerified` in the algorithm.
      * Assert BusinessRuleViolationException with code
        `email_unverified_cannot_bind_profile` exists in source.
    - tests/VrBook.Api.IntegrationTests/Identity/ProvisionOrLinkUserHandlerTests.cs (NEW; Unit)
      * Fresh email + verified → provisions users + user_identities;
        returns id.
      * Fresh email + UNVERIFIED → throws.
      * Existing user + fresh identity + verified → links; returns
        existing id.
      * Existing user + fresh identity + UNVERIFIED → throws.
      * Existing identity → returns existing user id, refreshes
        LastSeenAt.
    - tests/VrBook.Api.IntegrationTests/Identity/TenantPickerHeaderPipelineTests.cs (NEW; Unit)
      * Middleware stamps Items["VrBook:ActiveTenantId"] when header
        valid + member.
      * Middleware does NOT stamp when header invalid or not-member.
      * HttpCurrentUser.TenantId reads Items["VrBook:ActiveTenantId"].
    All tests deliberately fail — the code doesn't exist yet.
    Marker: [Trait("Category", "Unit")] so they run under the CI filter.
    Local validation: `dotnet test --filter "Category!=Integration"` shows
      the M.13.* tests as expected failures + no other regression.
    CI expectation: RED (marked explicit) — but tests referenced from a
      test class listed in a `[Trait("Category", "RedInProgress")]` filter
      that CI's build.yml SKIPS. This mirrors the OPS.M.7 F11.7.6.1 RED
      pattern that ProvisionUserHandlerTests already uses.

M.13.2 — GREEN. Schema migration: user_identities + migration_audit tables.
    Files:
    - src/Modules/VrBook.Modules.Identity/Domain/UserIdentity.cs (NEW)
      * Aggregate. Provider (enum-guarded), ExternalId, UserId FK,
        FirstSeenAt, LastSeenAt. Create static factory. No mutating
        methods except UpdateLastSeen.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserIdentityConfiguration.cs (NEW)
      * Table map + UNIQUE (provider, external_id) + FK to users.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IdentityDbContext.cs
      * DbSet<UserIdentity> UserIdentities.
      * DbSet<MigrationAuditEntry> MigrationAudit.
    - src/Modules/VrBook.Modules.Identity/Domain/MigrationAuditEntry.cs (NEW)
      * POCO. Immutable. Used for read + insert only.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/{ts}_OpsM13_UserIdentitiesAndMigrationAudit.cs (NEW)
      * CREATE TABLE identity.user_identities (per §2.1).
      * CREATE TABLE identity.migration_audit (per §2.1).
      * No data movement in this migration — that's M.13.4.
    - src/VrBook.Api/Controllers/PlatformAdminController.cs (NEW or extend existing)
      * GET /api/v1/admin/platform/migration-audit?since=&migrationName=
        [Authorize(Roles="PlatformAdmin")]
    - src/Modules/VrBook.Modules.Identity/Application/MigrationAudit/Queries/... (NEW)
    Tests moved from RED to GREEN:
    - OpsM13_EmailCanonicalUsersShapeTests only the user_identities
      table assertion passes (b2c_object_id column drop is M.13.4).
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green.

M.13.3 — GREEN. ProvisionOrLinkUserHandler + email-first algorithm + guard.
    Files:
    - src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionOrLinkUserCommand.cs (NEW)
    - src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionOrLinkUserHandler.cs (NEW)
      * Full algorithm per §3.5. Injects IdentityDbContext + IUnitOfWork
        + IClock.
    - src/Modules/VrBook.Modules.Identity/Domain/User.cs
      * Add `Provision(email, displayName, emailVerified)` overload
        (no oid arg — oid is now on UserIdentity).
      * Keep old `Provision(b2cObjectId, email, ...)` [Obsolete] with
        [ObsoleteAttribute("Use email-first Provision overload; see OPS.M.13")].
      * DO NOT drop b2c_object_id from User yet — still stored until
        M.13.4 migration drops the column.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs
      * Switch to sending ProvisionOrLinkUserCommand (not
        ProvisionUserCommand). Provider inference stub returns "entra".
      * DO NOT wire X-Active-Tenant header yet — that's M.13.6.
      * DO NOT drop the Role-claim writes yet — that's M.13.6 too.
    - Delete ProvisionUserCommand + ProvisionUserHandler.cs (superseded).
      * Retire the RealEntraOid + PickSurvivor helpers — no longer needed
        because collision is now a link-not-claim.
    - Retire IUserRepository.GetByB2CObjectIdAsync + GetActiveByEmailAsync
      (kept in code until M.13.4 for use by the migration; removed then).
    Tests moved to GREEN:
    - All ProvisionOrLinkUserHandlerTests (Branch 1/2/3 + guards + race).
    - OpsM13_ProvisioningEmailFirstShapeTests.
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green.

M.13.4 — GREEN. Backfill migration + drop b2c_object_id + partial-UNIQUE.
    Files:
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/{ts}_OpsM13_CollapseEmailCanonical.cs (NEW)
      * Full 8-step algorithm per §3.4.
      * INSERTs into identity.migration_audit at each step.
      * Cross-schema UPDATEs against booking / reviews / messaging /
        notifications / catalog. Requires the migrator role's
        BYPASSRLS (already granted per OpsM9 — see previous review Ev-E).
    - src/Modules/VrBook.Modules.Identity/Domain/User.cs
      * Drop `B2CObjectId` property.
      * Drop `ClaimOidForExistingProfile` method (retired — no longer
        called after M.13.3).
      * Update `RefreshFromLogin` signature if needed (accepts only
        displayName + emailVerified).
      * Add computed property `EmailNormalized` (or use `Email.Value.ToLowerInvariant()`
        expression in the handler — pick one).
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs
      * Remove b2c_object_id mapping.
      * Add HasIndex on lower(email) with filter deleted_at IS NULL.
    - Retire IUserRepository.GetByB2CObjectIdAsync + GetActiveByEmailAsync.
    - Update HttpCurrentUser + ICurrentUser:
      * Rename B2CObjectId → ExternalObjectId (returns the current
        session's oid claim; consumers change).
      * Keep the property for now — the "cleanup B2C symbol" is
        formally under OPS.M.14; but this slice drops the DB column
        so we don't want to leave a dangling `User.B2CObjectId`. The
        aggregate loses the property; ICurrentUser keeps
        ExternalObjectId as the token-side accessor.
    Tests moved to GREEN:
    - OpsM13_EmailCanonicalUsersShapeTests fully passes (no
      b2c_object_id column, new UNIQUE index exists in snapshot).
    - NEW: integration test `UserBackfillMigrationTests` (Category=Integration)
      * Docker-Compose Postgres, seed 4 rows sharing email, run migration,
        assert 1 survivor active + others soft-deleted + FKs rewritten
        + user_identities populated + migration_audit rows present.
    Local validation:
    - Unit filter: `dotnet test --filter "Category!=Integration"`.
    - Integration (optional, docker-compose up postgres): `dotnet test --filter "Category=Integration"`
      — this is where the backfill correctness lands.
    CI expectation:
    - Unit CI: green.
    - Integration CI (if wired): green. Currently CI runs unit-only per
      the "Use CI's test filter locally" memory — DO NOT block M.13.4
      shipping on integration CI green (it's not run in CI); DO run
      integration locally against the compose Postgres before push.

M.13.5 — GREEN. GET /me/tenants API + tenant picker frontend (no header pipeline yet).
    Files (API):
    - src/Modules/VrBook.Modules.Identity/Application/Users/Queries/GetMyTenantsQuery.cs (NEW)
    - src/Modules/VrBook.Modules.Identity/Application/Users/Queries/GetMyTenantsHandler.cs (NEW)
    - src/VrBook.Contracts/Dtos/Identity.cs
      * Add MyTenantsResponse + MyTenantMembershipDto.
    - src/VrBook.Api/Controllers/IdentityController.cs
      * Add GET /api/v1/me/tenants endpoint.
    Files (Frontend):
    - web/src/lib/tenants/activeTenant.ts (NEW)
      * getActiveTenantId / setActiveTenantId / clearActiveTenantId.
    - web/src/lib/tenants/useMyTenants.ts (NEW)
      * react-query hook wrapping GET /api/v1/me/tenants.
    - web/src/app/select-tenant/page.tsx (NEW)
      * List rendered from useMyTenants; click sets sessionStorage +
        router.replace(returnTo).
      * "Sign in as different user" secondary action.
    - web/src/app/auth/callback/page.tsx
      * After MSAL sets active account, fetch /me/tenants.
      * Route by count: 0 → '/'; 1 → auto-pick + returnTo; >1 → /select-tenant.
    Tests:
    - Api integration: GetMyTenantsHandlerTests — 0/1/N tenant shapes.
    - Web unit: useMyTenants + activeTenant helpers.
    Local validation: `dotnet test --filter "Category!=Integration"` +
      `cd web && npm test`.
    CI expectation: green.

M.13.6 — GREEN. X-Active-Tenant header pipeline (frontend interceptor +
    backend middleware validation + HttpCurrentUser.TenantId source switch).
    Files (Frontend):
    - web/src/lib/api/client.ts
      * In apiFetch, if !anonymous, read getActiveTenantId(); if set,
        headers['X-Active-Tenant'] = tenantId.
    Files (API):
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs
      * Read Request.Headers["X-Active-Tenant"] AFTER memberships read.
      * Parse; find matching membership; if found → stamp
        Items["VrBook:ActiveTenantId"] + add app_tenant_id claim +
        stamp Items["VrBook:MembershipRoles"] as dictionary
        (Guid tenantId → set of role strings).
      * DROP the loop writing ClaimTypes.Role claims for each membership
        (per §3.3 rationale for pulling this forward).
      * KEEP the PlatformAdmin role claim write (it's DB-derived and
        the PlatformAdmin bypass still uses it).
    - src/VrBook.Contracts/Interfaces/ICurrentUser.cs
      * Add `IReadOnlyDictionary<Guid, IReadOnlySet<string>> MembershipRoles { get; }`.
    - src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs
      * TenantId — read from Items["VrBook:ActiveTenantId"] first, then
        fall back to app_tenant_id claim (deleted next slice; kept for
        now so anything that still reads the claim keeps working).
      * MembershipRoles — read from Items["VrBook:MembershipRoles"].
      * HasTenantRole — reads MembershipRoles instead of ClaimsPrincipal.Role
        claims + app_tenant_id claim.
      * ActiveTenantItemKey + MembershipRolesItemKey constants.
    Tests moved to GREEN:
    - TenantPickerHeaderPipelineTests (Unit) — all cases.
    - NEW: HttpCurrentUser tests for MembershipRoles + TenantId read.
    - Existing TenantAuthorizationBehavior tests should still pass —
      IsPlatformAdmin bypass unchanged; the tenant equality check
      now reads Items instead of claim.
    Local validation: `dotnet test --filter "Category!=Integration"`.
    CI expectation: green.
    **This is the sub-commit where the walk unblocks end-to-end.**

M.13.7 — GREEN. Sign-out flow + deep-link recovery UX.
    Files (Frontend):
    - web/src/lib/auth/useAuth.ts
      * signOut: call clearActiveTenantId() before instance.logoutRedirect().
    - web/src/app/auth/signout/page.tsx
      * Guard: clearActiveTenantId() on mount (double-safety in case the
        button flow didn't fire it).
    - web/src/app/(components)/AdminGuard.tsx (or nearest error boundary
      for admin routes — file may need to be created if none exists)
      * If API returns 403 with `code=cross_tenant_write_rejected` AND
        the user is a member of >1 tenant → show
        "You need to switch workspace to view this. Sign out and pick a
         different workspace." with a Sign Out button.
      * If member of exactly 1 tenant → generic "access denied" page.
    Tests:
    - Web unit: sign-out clears sessionStorage; admin guard renders
      the switch-workspace CTA for multi-tenant users only.
    Local validation: `cd web && npm test`.
    CI expectation: green.

M.13.8 — GREEN. Doc close-out.
    Files:
    - docs/OPS_M_13_CLOSE_OUT.md (NEW)
      * What shipped in each sub-commit. Verify checklist.
      * What was deferred (Owner/Admin legacy column drop → M.15,
        DevAuth retirement → M.14, App-Role portal cleanup → M.15,
        migration_audit endpoint UI → follow-up UX slice).
      * Rollback runbook link + confirmation that _pre_m13_snap is
        left in place for 30 days.
      * Staging walk verification results: 4 broken rows collapsed
        to 1; user signs in, picks tenant (or auto-picks), confirms
        booking, sees no 403.
      * Prod cutover checklist: NONE — this slice is staging-only.
        Prod cutover is a follow-up gate.
    - docs/OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md — add a top-note
      referencing this slice as the executed shape.
    - docs/OPS_M_12_SOCIAL_IDPS_PLAN.md — update §0 note that M.13
      is a prerequisite (already partially says so).
    Doc-only commit. No code change.
    CI expectation: SKIPPED per the "No CI for doc-only commits"
      memory (cd-staging-api.yml paths exclude *.md).
```

**Sub-commit count: 8.** M.13.1 is RED (explicit) + 6 GREEN code commits
+ M.13.8 doc close-out. The walk unblocks at M.13.6.

---

## §5 Test coverage requirements

### §5.1 Arch tests locking the new shape

- `identity.users` has no `b2c_object_id` column (reflection on
  EF model snapshot).
- `identity.user_identities` exists with columns matching §2.1 +
  UNIQUE (provider, external_id) + FK to `identity.users`.
- `identity.migration_audit` exists.
- Unique index on `lower(email) WHERE deleted_at IS NULL` present
  (reflection on model snapshot AND runtime `pg_indexes` check).
- `ProvisionOrLinkUserHandler` source references `Provider` +
  `ExternalId` + throws `email_unverified_cannot_bind_profile`.
- `User.Provision` no longer accepts `b2cObjectId` parameter (or
  keeps it only as `[Obsolete]`).
- `UserProvisioningMiddleware` source references `X-Active-Tenant`
  header + reads it via `Request.Headers`.
- `HttpCurrentUser.TenantId` source references
  `Items["VrBook:ActiveTenantId"]`.
- `TenantAuthorizationBehavior` unchanged (arch guardrail — if this
  file changes in M.13, that's a bug).
- No production code references `IsRealEntraOid` or
  `ClaimOidForExistingProfile` (deleted symbols).

### §5.2 Integration tests

- **Fresh sign-in verified email → provisions.** Send
  `ProvisionOrLinkUserCommand(entra, oid-A, alice@example.com,
  verified=true, "Alice")` → returns `userId`; DB has 1 users row
  with `email='alice@example.com'` + 1 user_identities row
  `(entra, oid-A)`.

- **Fresh sign-in unverified email → PROVISIONS a fresh row.** The
  verified-email guard applies only to the LINK-to-existing case.
  Fresh users get a fresh row regardless of `email_verified` (they
  can't hijack a profile that doesn't exist). The row records
  `email_verified=false`; the SPA can later trigger a "verify your
  email" flow (out of scope for this slice; see previous review
  §5.1 which locks this semantic). Send `(entra, oid-A,
  alice@example.com, verified=false, "Alice")` with no pre-existing
  users row → returns userId with `email_verified=false` on the row.

- **Existing user + fresh identity + UNVERIFIED → refuses.** Pre-seed
  users row for alice with 1 user_identities `(entra, oid-A)`.
  Send `(entra, oid-B, alice@example.com, verified=false, "Alice")` →
  throws `BusinessRuleViolationException` with rule code
  `email_unverified_cannot_bind_profile`. THIS is where the guard
  fires (per §3.5 Branch 2). Same guard also fires on the Branch 3
  race-recovery path when the racing insert lost to a concurrent
  writer — see the pseudocode's Branch 3 `catch` block for
  `users_email_active_lower_uq`.

- **Existing user + fresh Entra identity + verified → links.**
  Pre-seed users row for alice with 1 user_identities `(entra,
  oid-A)`. Send `(entra, oid-B, alice@example.com, verified=true,
  "Alice")` → returns same `userId`; DB has 2 user_identities rows
  for that user.

- **Existing identity + return sign-in → refreshes.** Pre-seed
  user + identity `(entra, oid-A)`. Send `(entra, oid-A, ...,
  "Alice New Name")` → returns userId; `LastSeenAt` on identity is
  bumped; `DisplayName` refreshed.

- **Two-tab race — same fresh oid + same fresh email → one row.**
  Parallel task: run 2 handlers concurrently with the same command.
  Assert exactly 1 users row + 1 user_identities row exist post-race;
  both return the same userId.

- **Backfill migration integration test.** Seed a schema pre-state
  (4 users with same email, various PA/membership shapes; 3 rows
  with dev-* oids that got rewritten to niroshanaks@gmail.com;
  bookings/reviews/threads/audit rows FK'd to various). Run
  migration. Assert:
  - 1 active user row per email group.
  - Survivor picked per §3.4 precedence.
  - FKs on all 7 tables rewritten to survivor.
  - `user_identities` populated (1 row per historical oid).
  - Non-survivors soft-deleted.
  - migration_audit has 8+ step rows.
  - `_pre_m13_snap` schema exists + has row counts matching pre-state.

- **Tenant picker: 0/1/N tenants.**
  - Seed user with 0 memberships → `GET /me/tenants` returns
    `{tenants: []}`.
  - 1 membership → returns 1-item list.
  - 3 memberships → returns 3 items sorted by (IsPrimary DESC, name ASC).

- **`X-Active-Tenant` validation.**
  - Valid membership → middleware stamps Items[ActiveTenantId];
    downstream `[Authorize]` + M.4 write succeeds.
  - Header missing + write attempted → 403 (from M.4 gate).
  - Header set but user is NOT a member of that tenant → stamps
    NOT happen; write → 403.
  - Header malformed (not a GUID) → stamp doesn't happen; write → 403;
    middleware logs Warning.
  - Header set + user IS a PA (bypass path) → stamp happens; PA
    bypass still fires and allows.

### §5.3 Frontend tests

- `useMyTenants` — mocked fetch → hook returns list.
- `activeTenant.ts` — get/set/clear round-trip.
- `apiFetch` — with active tenant set, header is injected on non-anonymous
  calls; NOT injected on anonymous.
- `/auth/callback` behavior for 0 / 1 / >1 tenants — smoke render tests.
- `useAuth().signOut` clears active tenant BEFORE msal.logoutRedirect
  (mock call-order assertion).

---

## §6 Rollback + safety

### §6.1 Pre-migration snapshot

Baked into the M.13.4 migration itself (step 1 in §3.4). The
`_pre_m13_snap` schema is populated inside the migration transaction
so it either lands with the collapse or rolls back with it.

Additionally, before M.13.4 deploy to staging, run:

```powershell
# Full pg_dump for extra safety — this is a manual step in the deploy
# runbook, NOT part of the migration.
az postgres flexible-server execute --name <staging-pg> --admin-user <op> --admin-password $(op secret) --database vrbook --querytext "$(cat pre-m13-dump.sql)"
# Or use az storage blob-side pg_dump if VNet blocks direct connections.
```

Documented in `docs/OPS_M_13_CLOSE_OUT.md` (M.13.8) as part of the
deploy playbook.

### §6.2 Rollback runbook

Two rollback tiers:

**Tier 1 — API rollback only (no DB rollback).** If M.13.6 (header
pipeline) has a bug but the DB is fine, revert the API deploy to
the pre-M.13.6 revision. The DB schema still supports pre-M.13
`ProvisionUserHandler` reads (the b2c_object_id column is gone at
M.13.4 — so this ONLY works if the revert is to a post-M.13.4 API
build). Practically: revert to the M.13.5 API image; the header
pipeline is unwired but the schema + provisioning are already
migrated.

**Tier 2 — Full DB restore.** If the M.13.4 backfill misattributes
FKs (e.g. a booking gets re-parented to the wrong survivor):

1. Take API offline (revoke migrator role's connection).
2. Run the SQL restore script per §3.4 rollback runbook.
3. Re-deploy the pre-M.13 API image.
4. Delete the M.13.4 migration row from `__EFMigrationsHistory` so
   future migrator runs re-apply it (once fixed).

### §6.3 Feature flag

**Decision: no feature flag for the header pipeline.** Reasoning:

- The pipeline is unified per §3.1 — a flag adds a code branch and a
  new failure mode (flag says off but header sent, or flag on but
  frontend hasn't shipped yet).
- Sub-commit sequence gates the risk instead: M.13.5 ships the
  frontend picker WITHOUT the header pipeline (so it stores the
  tenant id but the API ignores it — noop); M.13.6 wires both ends.
  If M.13.5 has a bug, the SPA is broken; if M.13.6 has a bug, the
  API 403s and we revert to M.13.5 (tier 1 rollback).
- The user's memory says "test through UI, not CLI" — a feature flag
  adds a hidden switch that can't be verified from UI.

**Decision: keep `_pre_m13_snap` schema for 30 days** as the ONE
safety net. Documented cleanup migration
`OpsM13a_Drop_PreM13_Snapshot` scheduled for +30 days from M.13.4
deploy.

---

## §7 Open questions for the user (business-scope only)

Two decisions the user already made (multi-tenant humans → yes, tenant
picker; verified-email required → yes, refuse to bind) closed the
main axes. Remaining sub-decisions surfaced during this plan:

1. **Multi-tab tenant behavior — per-tab or uniform-across-tabs?**
   Chosen default: `sessionStorage` → per-tab (each tab can be in
   a different tenant). Alternative: `localStorage` → all tabs
   share one active tenant; opening a new tab inherits.
   Impact: user managing 2 property portfolios can view both
   simultaneously in 2 tabs (per-tab) vs must sign out and back in
   to see the second (uniform).
   **Recommendation: sessionStorage (per-tab).** Matches Notion's
   power-user behavior. Confirm.

2. **On force-sign-out-all-tabs (deferred).** Sign-out in tab A does
   not force tab B out of the app until B's token expires. If we
   want instant cross-tab sign-out, add a `localStorage` epoch
   listener. **Recommendation: defer; today's Bearer + refresh
   token model already means tab B loses auth in <60 min anyway.**
   Confirm defer.

3. **Prod cutover strategy for the M.13.4 backfill (out of scope
   for this slice — flagged for future planning).** The staging
   backfill can be re-run + reversed cheaply because it's
   `niroshanaks` + a small number of test rows. Prod will have
   more rows and a real audit trail — the rollback strategy in
   §6.2 (destructive to post-migration writes) will not work.
   **Not blocking this slice.** But signal: prod cutover of M.13
   is a separate slice with its own risk plan.

---

## §8 Appendix — key file:line pointers (as-is state today)

| Concern | File | Line |
|---|---|---|
| Users row init (has `b2c_object_id`, unique) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260525190601_Init_IdentitySchema.cs` | 46, 79-84 |
| Email UNIQUE dropped (Slice 4 mistake) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260622144933_Slice4_DropEmailUnique.cs` | 11-23 |
| F11.7.6.4 soft-delete data-heal (superseded by M.13.4) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260701000000_OpsM10_2_F11_7_6_SoftDeleteDuplicateUsers.cs` | 35-55 |
| ProvisionUserHandler (to be replaced) | `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs` | 60-119 |
| `IsRealEntraOid` heuristic (to be deleted) | `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs` | 126 |
| User aggregate `B2CObjectId` (to be dropped) | `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 13-14 |
| `User.ClaimOidForExistingProfile` (to be deleted) | `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 158-183 |
| Middleware Role-claim writes (to be dropped in M.13.6) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs` | 82-93 |
| Middleware `app_tenant_id` write (to be reshaped) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs` | 95-100 |
| `HttpCurrentUser.TenantId` accessor | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` | 101-108 |
| `HttpCurrentUser.HasTenantRole` | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` | 110-127 |
| M.4 gate reading `currentUser.TenantId` | `src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs` | 96-102 |
| `ICurrentUser.TenantId` contract | `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` | 35-42 |
| MSAL config (unchanged by this slice) | `web/src/lib/auth/msalConfig.ts` | 36-79 |
| API client fetch (adds `X-Active-Tenant` in M.13.6) | `web/src/lib/api/client.ts` | 89-136 |
| Auth callback page (rewritten in M.13.5) | `web/src/app/auth/callback/page.tsx` | 14-35 |
| useAuth signOut (extended in M.13.7) | `web/src/lib/auth/useAuth.ts` | 47-49 |
| RLS carve-out for identity.users (unchanged) | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260628131731_OpsM9_Identity_RlsPolicies.cs` | 20-24 |
| Sign-out page (extended in M.13.7) | `web/src/app/auth/signout/page.tsx` | — |
| Existing `GET /me/tenant` (kept alongside new /me/tenants) | `src/VrBook.Api/Controllers/IdentityController.cs` | 54-62 |
| Tenant membership aggregate | `src/Modules/VrBook.Modules.Identity/Domain/TenantMembership.cs` | 21-116 |

### §8.2 Authoritative references

- Microsoft Learn — [Access tokens in the Microsoft identity platform](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens) — `oid` claim shape.
- Microsoft Learn — [Identity providers for external tenants](https://learn.microsoft.com/en-us/entra/external-id/customers/concept-authentication-methods-customers) — federation-through-Entra pattern (M.12 prerequisite).
- Microsoft Learn — [ID tokens](https://learn.microsoft.com/en-us/entra/identity-platform/id-tokens) — ID vs access token separation (already correctly wired per token-auth deep-dive §1.4).
- MDN — [Window.sessionStorage](https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage) — per-tab persistence semantics.
- Postgres 14 — [CREATE INDEX ... WHERE](https://www.postgresql.org/docs/current/sql-createindex.html) — partial unique index on `lower(email)`.
- Postgres 14 — [UPDATE ... FROM](https://www.postgresql.org/docs/current/sql-update.html) — cross-schema FK rewrite in M.13.4.
- Companion repo docs: [`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md`](./OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md), [`OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md`](./OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md).

---

## §9 Approval gate

The user reads §1 (plain-English) + §3.1/§3.2/§3.3 (three hardest
decisions). If approved, execution proceeds sub-commit by sub-commit
per §4. Every sub-commit ends in a §11-style close-out log entry on
the master TODO; the walk verification lands at M.13.6.

If the user pushes back on:
- Header vs cookie vs URL prefix (§3.1) — re-plan §3.1 + §3.3 + M.13.6.
- Picker page vs modal vs inline (§3.2) — re-plan §3.2 + M.13.5.
- API enforcement point (§3.3) — re-plan §3.3 + M.13.6.
- Backfill survivor rules (§3.4) — re-plan §3.4 + M.13.4.
- Verified-email guard placement (§3.5) — re-plan §3.5 + M.13.3.
- Multi-tab per-tab default (§7 Q1) — re-plan §3.1 storage layer.

Everything else in this doc is either (a) load-bearing on the user's
already-approved business decisions, or (b) mechanical detail from
the two prior reviews already signed off.
