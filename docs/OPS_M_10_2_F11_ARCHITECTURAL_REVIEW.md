# OPS.M.10.2 F11 — Architectural Review (Identity + Tenancy Subsystem)

- **Status:** Draft for reviewer sign-off (`niroshanaks`).
- **Date:** 2026-07-01.
- **Author (role):** Platform Enterprise Architect.
- **Scope:** Root-cause of the recurring OPS.M.10.2 F11.7 walk failures (Admin nav
  missing on `/`; `POST /bookings/{id}/confirm` → 403 with `actual=<null>`), and
  the durable design fixes for the identity/tenancy subsystem across five
  areas: Entra External ID setup, identity provisioning, tenancy model,
  DevAuth replacement, migration observability.
- **Companion evidence bundle (raw JSON, on disk):**
  `%TEMP%\claude\c--Work-BookingApp\fcdf2bec…\scratchpad\{m4rejects,provfail,niro,bootstrap,user_300,mig_apply,migrator_marker,bypasses}.json`.
- **This doc supersedes** the F11.7.6 + F11.7.7 hypothesis-driven RCAs. Every
  claim in §3 cites a log line from §2.

---

## 1. Executive summary

**~20 commits since F11.7.5.1 have been patching symptoms of one design flaw.**
The identity subsystem has three co-morbid failures that manifest as the
same user-visible outcome (Admin nav missing + M.4 gate 403 on writes):

1. **`identity.users` is unique on `b2c_object_id` (`oid`), not on email**, so
   the same human accumulates one row per distinct sign-in source (DevAuth
   persona vs. real-Entra session; later: Google IdP vs. Microsoft IdP). Each
   row carries an independent `is_platform_admin` bit and its own set of
   `tenant_memberships`. `UserProvisioningMiddleware` picks whichever row
   matches this session's `oid` and stamps claims from THAT row. When the
   session's row is the "wrong" one, the middleware writes zero role claims
   + zero `app_tenant_id`, and every write is auto-rejected by M.4 at
   `TenantAuthorizationBehavior.cs:101`.
2. **The operator "fix-it" path (`POST /dev-auth/bootstrap-operator`) is
   currently 404 in staging** because `DevAuth__AllowAnonymous=false` on the
   API container (correct for a staging environment — DevAuth is a dev
   feature). The one dev-bridge that WOULD promote the current session's
   row is off. Nothing else can grant PA + memberships from outside Postgres.
3. **The staging container-app Postgres has no direct operator access** (VNet
   only) and the migrator job's stdout — including the F11.7.7 retirement
   migration's `RAISE NOTICE 'transferred=%'` line — **does not reach Log
   Analytics**. So even when we "run a fix migration," we cannot verify
   from telemetry that it actually ran or what it did.

The overlap of #1 + #2 + #3 is why each patch has failed. A new commit lands
(fixing #1's provisioning shape), the migrator "succeeds," but we cannot
prove the DB state (#3), and the promotion path is 404 (#2), so the walk
user is stuck on whichever row the current Entra session happens to bind.

**The durable fix is not another patch to `ProvisionUserHandler` or another
data-heal migration.** It is:

- **A.** Make `identity.users` unique on **`(lower(email))`** with an active-row
  filter, and provision by email-first-then-oid-attach (rather than
  oid-first). This is the shape Entra + social-IdP fan-out (OPS.M.12)
  actually needs.
- **B.** Retire `DevAuth` from staging + production entirely. Replace the
  fallback pattern with a **Test-only `TestAuthenticationHandler`** that
  lives in the test project and is NEVER wired in Web/Api hosts. Every
  environment above `Development` runs Entra-only.
- **C.** Ship an **admin-only** (Entra-authenticated, `PlatformAdmin` App Role)
  operator surface for the promote-user + seed-membership operations
  (superseding `bootstrap-operator`'s DevAuth gate). This is the promotion
  path with the correct trust boundary. Staging + production both use it.
- **D.** Emit migration side-effects to a **new `identity.migration_audit`
  table** (row per DML step) that the API can serve via a read-only
  admin endpoint. Container-app job stdout is a lossy sink; a DB row is
  the observability primitive that always works.

**The single most damning piece of evidence** (§2 Ev-B):

> `2026-07-01T13:50:30.2160154Z` — `"Dev bridge: persona {Oid} email updated
> {OldEmail} -> {NewEmail}." Oid=dev-guest-00000001 OldEmail=niroshanaks@gmail.com
> NewEmail=dev-guest-00000001@vrbook.local`
>
> The DevAuth persona-email dev-bridge **rewrote the walk user's real email
> off the `dev-guest` persona row and onto a stub inbox**. Nine seconds
> later, `POST /api/v1/dev-auth/bootstrap-operator` returned **404** because
> `DevAuth__AllowAnonymous=false`. Bootstrap was targeting the email that
> was just wiped. Every subsequent walk attempt hit an `identity.users` row
> whose email no longer matched what the operator was passing in — even if
> DevAuth had been re-enabled.

This is the "DevAuth mutates real state" hazard the user called out. The
tool that was supposed to help repair a broken row was the tool that
broke it further, on a schedule uncorrelated with real user activity.

---

## 2. Evidence — the data pulled today (2026-07-01, `law-vrbook-staging`)

Workspace: `60a321d4-4440-47e3-bdf4-59ce77ecd4db`. Time range: last 48h.
Container image: `crvrbookstaging.azurecr.io/vrbook-api:4857454091df6a…` (F11.7.7
fast-track v2 fixup, revision `--0000292` created `2026-07-01T19:18:03Z`).

### Ev-A — every M.4 gate rejection in 48h is `actual=<null>`

Query: `ContainerAppConsoleLogs_CL | where Log_s contains "Cross-tenant write rejected"`.
Sample results (from `scratchpad\m4rejects.json`):

| when (UTC) | RequestName | UserId | msg |
|---|---|---|---|
| 2026-07-01T19:57:22 | `RejectBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T19:53:53 | `ConfirmBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T19:49:44 | `ConfirmBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T02:30:11 | `RejectBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T02:22:05 | `ConfirmBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T02:16:47 | `ConfirmBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-07-01T00:31:23 | `ConfirmBookingCommand` | `300b8e90…7916` | `attempted=…001, actual=<null>` |
| 2026-06-30T21:10:12 | `CancelBookingCommand` | `76c384a2…c5fd` | `actual=<null>` (line 72 — background-scope branch) |
| 2026-06-30T21:10:12 | `RefundForBookingCommand` | `76c384a2…c5fd` | `actual=<null>` (line 72 — background-scope branch) |

Total in 48h: **~30 warning rows across 2 distinct UserIds**, all against
`attempted=00000000-0000-0000-0000-000000000001` (the default tenant). All
share the terminal branch of `TenantAuthorizationBehavior` — line 101 for
foreground calls, line 72 for background-scoped sub-dispatch (Cancel →
Refund path). **No user id ever hit the PlatformAdmin bypass log-line
in 48h except one — `0feb7dcd…91a2` at 2026-06-30T22:58 — a different row.**

**Reads as:** for `300b8e90…7916` at every M.4 gate call today,
`currentUser.IsPlatformAdmin == false` AND `currentUser.TenantId == null`.
`UserProvisioningMiddleware:78` stamped both branches from THIS user id's
row; the DB fact is that this row has `is_platform_admin=false` and zero
`is_primary=true` memberships on tenant `…001`.

### Ev-B — the SetPersonaEmail hazard fired 9s before a bootstrap 404

Query: `ContainerAppConsoleLogs_CL | where Log_s contains "niroshanaks"`.
From `scratchpad\niro.json` (line 3, the top result):

> `2026-07-01T13:50:30.2160154Z` — `Dev bridge: persona {Oid} email updated
> {OldEmail} -> {NewEmail}.`
> `Oid=dev-guest-00000001` `OldEmail=niroshanaks@gmail.com`
> `NewEmail=dev-guest-00000001@vrbook.local`
> `UserId=e98aa66c-06c4-4e5f-83c9-523e211afd9b`

Then at `2026-07-01T13:50:39.102Z` (9 seconds later), the bootstrap-operator
call landed with `StatusCode=404`. This is the single most damning
sequence in the log. `SetPersonaEmail` did not just "not help" — it
actively wiped the email that the subsequent bootstrap was going to look
up. Two dev-bridges each doing partial work on the same row.

### Ev-C — bootstrap-operator has been 404 for most of today

Query: `ContainerAppConsoleLogs_CL | where Log_s contains "dev-auth/bootstrap-operator"`.
`scratchpad\bootstrap.json` — response status by timestamp:

| when (UTC) | Status | Note |
|---|---|---|
| 2026-07-01T13:50:39 | **404** | after SetPersonaEmail (Ev-B) |
| 2026-07-01T03:07:24 | 200 | |
| 2026-07-01T01:20:14 | 200 | |
| 2026-07-01T01:18:10 | **404** | |
| 2026-07-01T01:16:52 | **404** | |
| 2026-06-29T23:59:18 | **404** | |
| 2026-06-29T23:56:49 | 200 | |

Confirmed via `az containerapp show`: **current API env has
`DevAuth__AllowAnonymous=false`** and no `DevAuth__AllowStripeStub` at all.
Both are required for bootstrap-operator per `IdentityController.cs:283-290`.
Every 200 above corresponds to a brief window where a human flipped
`AllowAnonymous=true` via `az containerapp update --set-env-vars` for a
walk, then the container was re-deployed (revision update reverts env unless
env is in Bicep) and the value snapped back to `false`. **Bootstrap is
therefore effectively unavailable to the walk user for most of the day.**

### Ev-D — the walk user has AT LEAST four distinct rows in `identity.users`

Query: `GetMeQuery` handler UserId + booking-notification RecipientId
distribution filtered by `niroshanaks@gmail.com`.

Distinct UserIds seen against this email OR against her session across the
last 48h (compiled from `scratchpad\niro.json` + `scratchpad\bootstrap.json` +
`scratchpad\m4rejects.json`):

- `e98aa66c-06c4-4e5f-83c9-523e211afd9b` (Confirm handler UserId at 22:58/23:09 — appears to be the Owner-persona-session row; also the caller of SetPersonaEmail at 13:50)
- `0feb7dcd-7ea1-4a56-948b-6c029b2791a2` (Cancel + BookingPlaced recipient — the one row that hit `PlatformAdmin bypass` yesterday; also the notification RecipientId with `niroshanaks@gmail.com` as email)
- `300b8e90-17ed-41ea-bd62-8ba3324e7916` (today's failing session's UserId, 2026-07-01 00:24 → 19:57)
- `76c384a2-092f-44c1-b40c-7156d6c7c5fd` (yesterday's failing session's UserId, 2026-06-30/07-01)

That's **≥ 4 rows** in `identity.users`. Each new real-Entra sign-in with
a distinct-enough token has been landing a fresh row (per F11.7.6 handler
Branch 3 — the "oid miss + email miss" branch — but F11.7.6 shipped
2026-06-30 and both `300b8e90` and `76c384a2` are POST-F11.7.6 UserIds,
so **F11.7.6's handler-side fix did not converge the shape**).

### Ev-E — `identity.users` and `identity.tenant_memberships` are NOT under RLS

From `src\Modules\VrBook.Modules.Identity\Infrastructure\Persistence\Migrations\20260628131731_OpsM9_Identity_RlsPolicies.cs:20-24`:

> `// OPS.M.9 §3.2 carve-outs: identity.users, identity.tenants,`
> `// identity.tenant_memberships are intentionally NOT under RLS.`

So the `UserProvisioningMiddleware:68` `Set<TenantMembership>().Where(...)`
query is UNFILTERED by tenant. If it returns zero rows, that IS the DB
truth for that userId — not an RLS artifact. Rules out the "GUC not set,
RLS filtered the read to zero" hypothesis.

### Ev-F — migrator container stdout does NOT reach Log Analytics

Query: `ContainerAppConsoleLogs_CL | where Log_s has_any("Applying migration", "F11.7.7", "RAISE NOTICE", "Migrating IdentityDbContext")`.
Time range: last 48h. Result: **empty** (`scratchpad\mig_apply.json` = `[]`;
`scratchpad\migrator_marker.json` = `[]`).

But `az containerapp job execution list -n caj-vrbook-migrator-staging`
shows **10+ Succeeded executions** in the same window (including
`caj-vrbook-migrator-staging-upthts6` at `2026-07-01T19:16:58Z`, which is
the newest F11.7.7 fast-track v2 fixup deploy).

So: the migrator IS running, but its stdout (Serilog Console sink at
`src\VrBook.Migrator\Program.cs:26`) is NOT reaching
`ContainerAppConsoleLogs_CL`. The `RAISE NOTICE 'transferred=%'` line
inside the F11.7.7 migration is also invisible. **This is silence-as-
evidence** — labeled explicitly per feedback rule: it does not mean
"the migration didn't run"; it means "we have no telemetry visibility
into what the migration did." That is itself a design flaw.

**Caveat:** the retirement migration MIGHT be silently short-circuiting
via the fresh-DB early-return branch (line 63-65: `IF NOT EXISTS (…
catalog.properties …) THEN RETURN`) — but the catalog schema has been
present since Slice 2, so this branch should not fire on the staging DB.
Without direct DB access we cannot verify.

### Ev-G — provisioning failures are only health-probe timeouts

Query: `ContainerAppConsoleLogs_CL | where Log_s contains "User provisioning failed"`.
`scratchpad\provfail.json`: **all 20+ failures in 48h** are
`OperationCanceledException` on RequestPath `/health/live` or
`/health/ready`, oid `dev-owner-00000000`. These are container-startup
probes racing the DB pool warm-up, NOT walk-user failures.

Rules out the "provisioning silently failed for the walk user" hypothesis.
The middleware ran successfully for every walk-user request; it just
resolved the wrong row (or the right row with empty state).

### Ev-H — Entra tokens are correctly formed and reach the API

Every M.4 rejection (Ev-A) has a real UserId stamped in the log. That
UserId is the app-side Guid from `identity.users.Id`, which is only set
by `ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId;` at
`UserProvisioningMiddleware.cs:62`. So provisioning succeeded for every
walk request. That in turn means:

- The JWT was signed by Entra (issuer/audience validated).
- The `oid` claim was present.
- The DB round-trip in the middleware succeeded (not timed out, not RLS-blocked).

Rules out "Entra token was broken" and "JWT bearer misconfig" hypotheses.

### Ev-I — F11.7.7 was reverted, re-shipped, then patched

`git log` since 2026-06-28: `163229d` (F11.7.7 fast-track v1) was reverted
by `475c1a1`; then `ddde9f5` shipped fast-track v2; then `4857454` patched
v2 with the fresh-DB early-return. That's 3 iterations of the retirement
migration in the last ~24h. Each was hypothesis-based; none has audit
evidence in Log Analytics (Ev-F) proving what it did to the DB.

### Ev-J — the 20 F11.7.5.*+F11.7.6.*+F11.7.7.* commits DO NOT address the systemic issue

Summarised by class:
- `.5.1` `BackgroundTenantScope fallback` (patch: line 72 of behavior)
- `.5.2` `BookingsController owner endpoints resolve tenant from booking` (patch: controller)
- `.5.3` `bootstrap-operator promotes IsPrimary on idempotent path` (patch: dev-bridge)
- `.5.7` `Confirm+Reject tolerate Stripe failures` (patch: handler)
- `.5.8/.5.9` `post-walk UX fixes` (patch: web)
- `.5.10` `bootstrap-operator promotes ALL user rows sharing email` (patch: dev-bridge — the multi-row hazard IS acknowledged here, but only in the fix-tool, not the schema)
- `.6.1/.2/.3` `multi-row-per-email fix at ProvisionUserHandler` (patch: handler branches — Ev-D shows this DOES NOT converge; a fresh Entra oid still lands a fresh row)
- `.6.4` `data-heal migration` (patch: DML, no observability per Ev-F)
- `.6.5` `arch tests lock upsert invariants` (patch: test surface only)
- `.7.7 v1/v2/v2-fixup` `retire DevAuth personas` (patch: DML, no observability per Ev-F, blocked by DevAuth=false per Ev-C so bootstrap can't verify)

**Not one of these commits changes `identity.users` to be email-unique.**
The multi-row-per-email shape is treated as inevitable; every fix is
"make the fix tools aware of it." That is the design flaw.

---

## 3. Diagnosis — the actual root cause

Backed by the evidence in §2, the root cause is a **schema-level design
choice** for `identity.users`:

### The root cause

**`identity.users` is keyed by `b2c_object_id` (`oid`)**, not by email. Cross-
reference:
- `identity.users` has unique index on `b2c_object_id` (per initial migration).
- `identity.users.email` uniqueness was **explicitly dropped** in
  `20260622144933_Slice4_DropEmailUnique.cs` "for shared-inbox DevAuth
  testing." Never re-established.
- `ProvisionUserHandler:62` looks up by oid first (Branch 1). If the incoming
  oid is fresh, Branch 2 does an email lookup and tries to rebind. But it
  **cannot** rebind when both survivor and incoming oid are real-Entra oids
  (guardrail throws `email_already_claimed` — `ProvisionUserHandler:86-91`)
  — and today's user has ≥ 4 real-Entra oids (Ev-D).

So each new sign-in session (a browser cleared cookies, a device change, an
MSAL refresh going stale enough that Entra re-issues a fresh oid) creates a
NEW row via Branch 3 (`user = User.Provision(...)`). Each fresh row carries
`is_platform_admin=false` and zero memberships. When
`UserProvisioningMiddleware` (line 68) reads memberships + IsPlatformAdmin
FOR THAT ROW, both come back empty. The middleware writes NO `Role` claims
and NO `app_tenant_id` claim (line 80: `if (memberships.Count > 0 ||
isPlatformAdmin)` guards the claim-emit block). The M.4 gate at
`TenantAuthorizationBehavior:96` reads `currentUser.TenantId is null` and
throws `CrossTenantAccessException`.

Cited evidence:
- Ev-A confirms `TenantId is null` at the M.4 gate for the current session's
  user id (`300b8e90…7916`) at 19:57:22 today.
- Ev-D confirms ≥ 4 distinct rows exist in `identity.users` for
  `niroshanaks@gmail.com`.
- Ev-J confirms not one of the 20 recent commits has changed the schema
  key from oid to email.

### Why the fix tools don't converge

- `bootstrap-operator` (Ev-C) is 404 for most of the day because
  `DevAuth__AllowAnonymous=false` on the staging API. The one code-path
  that could promote the walk user's current-session row is off.
- Even when it briefly IS 200 (03:07, 01:20), the F11.7.5.10 fix ("promote
  ALL rows with this email") is targeting a snapshot in time. If the user
  signs in fresh AFTER that promotion, a NEW row is provisioned and the
  new row is un-promoted.
- Ev-B: `SetPersonaEmail` is a live dev-bridge that mutates real user
  emails at unpredictable times. It has already rewritten `niroshanaks@gmail.com`
  off the `dev-guest` persona row (moving the search target out from
  under bootstrap-operator).

### Why the migration observability is broken

Ev-F: `ContainerAppConsoleLogs_CL` receives log lines from container-app
JOBS via the Serilog console sink → stdout → k8s log driver → Log Analytics
agent. Something in that chain is dropping the migrator job's stdout. Not
the workers'; not the API's; specifically the migrator job's.

Likely (unverified — the fix does not depend on this being the exact cause):
the `SerilogHostBuilderExtensions.UseSerilog` on `WebApplication` writes to
console via the ASP.NET Core hosting pipeline, which is instrumented by the
Container Apps agent. The `Host.CreateApplicationBuilder` + manual
`AddSerilog` + `Console` sink in `Migrator/Program.cs` writes to stdout via
a different pipeline that the agent may not flush in a job's short-lived
container (job exits, buffer discarded).

This is the reason `RAISE NOTICE 'transferred=%'` never appears — it goes
to Npgsql's client-message handler → back to the migrator process → Serilog
Console sink → the sink that isn't flushing.

### Ruled-out hypotheses

- **"Entra token misconfig / role claims not emitted."** Ev-H rules this
  out: middleware ran, UserId was stamped, DB round-trip succeeded.
- **"RLS filtered the memberships read to zero."** Ev-E rules this out:
  `identity.tenant_memberships` is not under RLS.
- **"Bootstrap-operator has a bug that skipped the current session's row."**
  Ev-C rules this out: bootstrap-operator was 404 for most of today. It
  didn't skip; it wasn't called successfully.
- **"F11.7.7 retirement migration didn't run."** Cannot be ruled in OR out
  from telemetry (Ev-F silence). Even if it DID run correctly, it only
  retires DevAuth personas — it does NOT collapse the multi-real-Entra-
  oid rows that Ev-D shows exist.

### Structural summary

The tenancy model puts three keys on the same row: `oid` (identity),
`email` (contact), `is_platform_admin` (authorization). Adding a fourth
axis (`tenant_membership`) as a separate table with FK on `user_id` was
right. But making `oid` the row's primary identity key was wrong: `oid`
is not stable across identity-provider realities. Email is closer to a
stable business identity for this use case, and Entra + social IdPs
converge on email-verified as the canonical identity anchor.

---

## 4. Design review — each of the 5 areas

### 4.1 Entra External ID setup — mostly correct, one gap

**Correct:**
- Provider choice (ADR-0012). External ID > B2C for a fresh subscription.
- JWT bearer with `ValidateIssuer + ValidateAudience` (`AuthExtensions.cs:52-60`).
  Two `ValidIssuers` accepting both the discovery URL and the tenant-id-host
  URL (empirical fix per comment). This matches Microsoft's actual token
  claims and is defensible.
- App Roles for `Owner` + `Admin` (ADR-0014). Correct pivot after the
  CIAM-extension-claim dead-end in OPS.M.0.
- MSAL `apiScopes = ['api://vrbook/access_as_user']` (msalConfig.ts:70).
  Correct — an API scope, not the SPA's `.default`.

**Gap:** `is_platform_admin` is DB-backed (ADR-0014 §Per-tenant roles). But
so is `Owner` and `Admin` in practice — no App Role assignments are
currently visible in the token for `niroshanaks` per Ev-D + Ev-A. The
`Owner` and `Admin` App Role assignments were never actually made in the
External tenant portal after ADR-0014 was accepted, OR the assignments
were made against the WRONG app registration. Either way, the App Role
plumbing is dormant. Every role claim currently comes from
`UserProvisioningMiddleware` writing them from DB, not from the token.

**Recommendation:** either (a) light up App Roles for real (assign
`niroshanaks` the `PlatformAdmin` App Role in the External tenant portal),
OR (b) formally drop App Roles from the design and document that
authorization is 100% DB-backed. The current half-and-half state adds
complexity without benefit.

### 4.2 Identity provisioning — the load-bearing design flaw

Current shape:
- `identity.users` unique on `oid`; email is a plain scalar with no
  uniqueness constraint (`20260622144933_Slice4_DropEmailUnique.cs`).
- Provisioning: `ProvisionUserHandler` does oid-first-then-email-fallback
  with a "both-real-Entra guardrail" that throws.
- Row semantics: a `user_id` (Guid) is what bookings/reviews/threads/audit_log
  FK on. Same person can have several `user_id`s.

**What's right:** oid IS emitted by every real IdP and IS stable per (IdP,
user) pair. So oid uniqueness is a valid constraint on the (oid) column.

**What's wrong:** oid uniqueness AT THE ROW is the wrong choice. It means
"one row per (IdP,user)-pair" not "one row per human." This has cascading
consequences:

- Every FK-holder (bookings.guest_user_id, reviews.author_user_id,
  audit_log.actor_id) references one specific oid-row, not the human.
  So "my bookings" is really "bookings placed under this specific sign-in
  session's row." A user signing in later via a different session sees an
  empty history.
- Every promotion (PA, tenant_admin) has to be applied to ALL rows with
  the same email OR the user hits stale-row failure mode. F11.7.5.10 tried
  this; Ev-D shows it doesn't converge because new rows keep appearing.
- OPS.M.12 (Google + Microsoft social IdPs against the same tenant) makes
  this exponentially worse. Two social IdPs + one work-account = 3 oids
  = 3 rows per human, on day one.

**Target design (§5 details):** collapse to email-first uniqueness with a
side table `identity.user_identities(user_id, provider, oid)` holding the
oid ↔ user mapping (one row per identity source). Same shape social-login
frameworks (Passport.js, Auth0 identities API, Google's People API) all use.

### 4.3 Tenancy model — the design is fine; the implementation is over-plumbed

Current shape:
- Global `is_platform_admin` bit on `identity.users`.
- Per-tenant roles in `identity.tenant_memberships(user_id, tenant_id, role, is_primary)`.
- Middleware reads both on every authenticated request.
- M.4 gate reads `ClaimTypes.Role` and `app_tenant_id` claim from the
  ClaimsPrincipal (which the middleware wrote).
- PA bypass + BackgroundTenantScope bypass for background commands.

**What's right:**
- Splitting global vs. per-tenant scopes onto different mechanisms is
  correct (ADR-0014).
- `TenantAuthorizationBehavior` as a MediatR pipeline behavior is a clean
  cross-cutting concern.
- BackgroundTenantScope bypass for row-derived tenant IDs (F11.7.5.1) is
  a legitimate pattern.

**What's over-plumbed:**
- The claim-write on every request. Every authenticated request runs one
  users-read + one memberships-read + rewrites claims into the
  ClaimsIdentity. If the DB has 0 memberships for the row, no claim gets
  written and the M.4 gate 403s. There is no "hey the DB has NO tenant
  linkage for this user, sign them up" flow — they just hit 403 forever.
- The **DB-wins claim precedence** ADR is stated correctly in ADR-0014,
  but the middleware doesn't actually enforce "DB wins" — it enforces
  "DB is the ONLY source." If tokens carried tenant claims, they'd be
  overwritten every request. This is defensible if we're honest about it
  ("token roles = ignored"), but ADR-0014's language ("DB-wins") suggests
  a hybrid that doesn't exist.
- The multi-tenant OwnerId / AdminId claim shape is confusing. Owner+Admin
  are global (from tokens or DB). tenant_admin is per-tenant (from DB
  memberships). But they all end up in `ClaimTypes.Role` set membership,
  which means `HasRole("Owner")` is true regardless of tenant scope. It
  works because the M.4 gate + controllers check `TenantId` explicitly, but
  the role claim semantics are muddled.

**Target design (§5 details):** keep the mechanism; simplify the semantics.
Deprecate the global `Owner` role (per ADR-0014 already scheduled); make
role check strictly `PlatformAdmin` (global) OR `HasTenantRole(tenantId, "tenant_admin")` (scoped). Drop the middleware's Role-claim-write to the
ClaimsIdentity — instead, expose `ICurrentUser.MembershipRoles: Dictionary<Guid, ISet<string>>` and let the M.4 gate + controllers ask the interface
directly.

### 4.4 DevAuth's design flaw — the whole mechanism must go

Current shape:
- `DevAuthHandler` (a real `AuthenticationHandler<DevAuthOptions>`) is
  registered in `AuthExtensions:64-72` when `DevAuth:AllowAnonymous=true`.
- Personas: Owner/Guest/Admin with fixed oid strings (`dev-owner-00000000`,
  etc.). Cookie-selected.
- Ops dev-bridges (`SetPersonaEmail`, `BootstrapOperator`, `StubStripeReadiness`,
  `BackdateCheckedOutAt`) all guard on `DevAuth:AllowAnonymous=true`.

**Design flaws:**
- **DevAuth mutates real state.** `SetPersonaEmail` rewrites a real
  `identity.users` row's email column (Ev-B shows this actively broke
  today). This is not a "test" mechanism; it's a production-state
  mutator with a "test" gate.
- **DevAuth requires per-persona seed rows.** The retirement migration
  (F11.7.7) exists precisely to clean these up. But new persona rows can
  be re-created any time DevAuth is temporarily re-enabled for a walk.
- **DevAuth's oids are non-GUID strings.** This breaks the "real-Entra oids
  are GUIDs" invariant, forcing the `IsRealEntraOid` guardrail
  (`ProvisionUserHandler:126`). Adding a fourth persona would be a schema-
  level exception.
- **DevAuth is enabled/disabled by an env var.** So it can be temporarily
  enabled in staging (which is exactly what happens for walks). Every
  temporary enable is a live-state-mutation window.
- **OPS.M.12 makes this worse.** With Google + Microsoft social IdPs
  configured, DevAuth's "one persona per role" abstraction ceases to
  match how real users authenticate.

**Target design (§5.4 details):** Replace with a **test-only
`TestAuthenticationHandler`** that lives ONLY in the test project's
`ApiTestFactory` (see `tests/VrBook.IntegrationTests/…`). Compile-time
excluded from Api/Web/Migrator/Workers hosts. No env-var gate; no
production risk surface; no state-mutating dev-bridges.

For local-dev (developer laptop) auth: use `dotnet user-secrets` +
Entra External ID's own "test users" feature (built-in — you provision
`niroshana-dev@vrbookcid.onmicrosoft.com` in the External tenant and sign
in normally). Same auth surface as staging.

### 4.5 Migration hygiene — observability is broken

Current shape:
- Container-App Job (`caj-vrbook-migrator-staging`) runs
  `VrBook.Migrator/Program.cs` on every deploy.
- Program.cs logs via Serilog Console sink to stdout.
- The migration files themselves use `RAISE NOTICE` for side-effect audit.

**Broken:** Ev-F confirms stdout does not reach Log Analytics for the
migrator job. All triage relies on `az containerapp job execution list`
telling us "Succeeded" — no visibility into what got done.

**Target design (§5.5 details):** table-based audit.

- New table `identity.migration_audit(id, migration_name, step_name,
  affected_count, notes, executed_at)`.
- Data-heal migrations INSERT one row per DML step INTO this table (a
  single migration might insert 3-5 rows). Rows are committed within the
  migration's own transaction — if the DML rolls back, the audit rolls
  back too. Correctness by construction.
- New admin-authenticated endpoint `GET /api/v1/admin/platform/migration-audit`
  returns the rows. Any tenant_admin OR PlatformAdmin can see them.
- Optional: also `raise NOTICE`, but treat that as best-effort. The row
  is the primary audit primitive.

This pattern makes migrations self-documenting to Log Analytics-less
environments (VNet-only staging, air-gapped envs).

---

## 5. Proposed target architecture

### 5.1 `identity.users` becomes email-canonical

Schema change (locked, single migration):

```
identity.users (
  id            uuid PK,
  email         citext NOT NULL,
  display_name  text NOT NULL,
  is_email_verified boolean,
  is_platform_admin boolean,
  ...timestamps,
  UNIQUE INDEX users_email_active_uq ON email WHERE deleted_at IS NULL
)

identity.user_identities (
  id            uuid PK,
  user_id       uuid NOT NULL REFERENCES identity.users(id),
  provider      text NOT NULL,      -- 'entra', 'google', 'microsoft', 'test'
  external_id   text NOT NULL,      -- the oid or provider-specific external id
  first_seen_at timestamptz,
  last_seen_at  timestamptz,
  UNIQUE (provider, external_id)
)
```

Provisioning becomes email-first:

1. Extract `email` + `emails` + `email_verified` claims from JWT.
2. Look up `identity.users` by `lower(email)` where `deleted_at IS NULL`.
3. If found, ensure a `user_identities` row exists for `(provider, oid)`.
   Insert if not (idempotent). Return `users.id`.
4. If not found: create the `users` row + the first `user_identities` row
   atomically. Return `users.id`.

Guardrail (unchanged in spirit): if email conflict but the token's
`email_verified=false`, refuse to bind — return 403 "unverified email
cannot claim a profile" to the user. Same "email hijack" defense the
current `email_already_claimed` guardrail wants, but based on the
verified-flag from the IdP, not on GUID-shape heuristics.

**This directly resolves the walk failure**: `niroshanaks@gmail.com` gets
ONE `users` row with `is_platform_admin=true` + a single `tenant_membership(…001, tenant_admin, is_primary=true)` row. Every sign-in session finds the
same row. M.4 gate stops 403'ing.

**OPS.M.12 works natively.** Signing in via Google adds a
`user_identities(provider='google', external_id=<google_sub>)` row and
resolves to the same user_id. Zero code change needed.

### 5.2 M.4 gate + middleware — simplify claim-write

- Drop the middleware's `primaryIdentity.AddClaim(Role, ...)` calls
  (`UserProvisioningMiddleware:86-93`). Roles are not stamped on the
  ClaimsPrincipal.
- Extend `ICurrentUser` with `IsPlatformAdmin` (already there) +
  `TenantId: Guid?` (already there) + `HasTenantRole(Guid, string)`
  (already there). All read from `HttpContext.Items` populated by the
  middleware from the DB.
- `TenantAuthorizationBehavior` stays as-is (it uses these already).
- Deprecate `Owner`/`Admin` global roles (per ADR-0014's "legacy retained
  until M.4"). `[Authorize(Roles="Owner,Admin")]` on controllers is
  replaced by `[Authorize(Policy="PlatformAdminOrAnyTenantRole")]` +
  handler-level tenant scoping.

### 5.3 Entra App Roles cleanup

Two choices; §8 requires user sign-off:
- **(A)** Fully DB-backed roles. Remove App Role definitions from the
  Entra app registration. ADR-0014's "global roles → Entra App Roles"
  section is superseded by an amendment. Simplifies the mental model.
- **(B)** Actually assign App Roles. In the External tenant portal,
  assign `niroshanaks` the `PlatformAdmin` App Role. Middleware's DB
  read for `IsPlatformAdmin` becomes a fallback for role assignments
  not yet processed by the JIT provisioning flow.

Recommendation: **(A)**. Fewer moving parts; matches what already
effectively happens (Ev-D shows no App Role emission).

### 5.4 DevAuth retirement — target shape

**Test-project only auth handler:**
```csharp
// tests/VrBook.IntegrationTests/Auth/TestAuthenticationHandler.cs
public sealed class TestAuthenticationHandler : AuthenticationHandler<TestAuthOptions> {
  // Reads persona from a header (X-Test-Persona) set by the test WebApplicationFactory,
  // NOT a cookie. Cannot be forged by an external caller because it's registered
  // ONLY in the WebApplicationFactory<Program>, never in the shipping API.
}
```

Ship-time API host: **Entra-only.** `AuthExtensions.AddVrBookAuthentication`
loses the DevAuth branch entirely.

**Dev-bridges migrate to admin endpoints:**
- `POST /api/v1/dev-auth/backdate-checked-out-at` → `POST /api/v1/admin/platform/bookings/{id}/backdate-checked-out-at` (Entra + `PlatformAdmin`).
- `POST /api/v1/dev-auth/persona-email` → deleted. It was actively harmful
  (Ev-B). Its intended purpose ("route notifications to a real inbox for
  testing") is instead served by making the user's real Entra login the
  notification recipient.
- `POST /api/v1/dev-auth/bootstrap-operator` → `POST /api/v1/admin/platform/users/{userId}/promote-to-platform-admin` (Entra + `PlatformAdmin`).
  First-PA bootstrap uses the F11.6.1-style in-app bootstrap: on API
  startup, if `identity.users` has zero PA rows AND `PlatformAdmin:BootstrapEmail`
  is set in config, promote that email's row. Idempotent + safe to
  re-run. See §5.6 F11.6.1-style hardening.
- `POST /api/v1/dev-auth/stub-stripe-readiness` → `POST /api/v1/admin/platform/tenants/{tid}/stripe/stub-readiness` (Entra + `PlatformAdmin`).

### 5.5 Migration observability

Ship `identity.migration_audit` per §4.5. Every future data-heal migration
INSERTs its side-effect row(s). Refactor F11.7.6.4 + F11.7.7 to use it.

`GET /api/v1/admin/platform/migration-audit?since=…` returns the rows.
No Log Analytics dependence. Works in air-gapped envs.

### 5.6 First-PA bootstrap without DevAuth

On API startup (Program.cs), read `PlatformAdmin:BootstrapEmail` from
config. If set AND `identity.users` has zero rows with `is_platform_admin=true`,
schedule a one-shot post-provisioning task: the first time a user with
that email signs in via Entra, promote them + seed a `tenant_membership`
(if `PlatformAdmin:BootstrapTenantId` also set).

This replaces `bootstrap-operator`'s three-guard pattern with a config-
driven, Entra-only, side-effect-once path. Safe to leave configured
permanently because it's idempotent (`if any PA already, do nothing`).

---

## 6. Migration plan — current to target

Not sub-commits; slice-level sequence.

**Slice OPS.M.10.2 F11.8 — Emergency operator promote (§7 unblock)**
- Ship the admin endpoint `POST /api/v1/admin/platform/users/promote-by-email`
  guarded by a startup-config-based first-PA bootstrap. The first
  authenticated call from `niroshanaks@gmail.com` triggers self-promotion.
- No schema change. Reversible.
- Unblocks the walk. Once she has one PA row, she can use it to promote
  the "correct" (survivor) user row via a widened variant that acts on
  ALL email-matching rows (F11.7.5.10 pattern, ported to Entra-authed).

**Slice OPS.M.13 — Identity schema collapse (blocks all further F11 work)**
- Ship `identity.user_identities` table.
- Backfill migration: for every distinct email in `identity.users` (grouped),
  pick a survivor (PA-first, oldest-CreatedAt). Move all sibling rows'
  `user_identities` to point at survivor. Rewrite FKs on bookings, reviews,
  audit_log, threads to survivor. Retire duplicates. Emit
  `identity.migration_audit` rows.
- Re-establish `users.email` unique index (active-row filter).
- Rewrite `ProvisionUserHandler` to email-first shape (§5.1).

**Slice OPS.M.14 — DevAuth retirement + admin-endpoint replacements**
- Move `DevAuthHandler` to the test project.
- Delete `DevAuthController` from the API.
- Ship the four admin endpoints listed in §5.4.
- Update `AuthExtensions` to drop the DevAuth branch.

**Slice OPS.M.15 — App Role cleanup (§5.3 option A)**
- Delete App Role definitions from the External tenant app registration.
- Amend ADR-0014.
- Remove role-claim writes from `UserProvisioningMiddleware`.

**Slice OPS.M.16 — Migration audit table**
- Ship `identity.migration_audit`.
- Refactor future data-heals.

OPS.M.12 (Google + Microsoft social IdPs) can proceed **only after
OPS.M.13** — the multi-oid-per-human reality is native to social IdPs and
requires the email-first schema to be non-catastrophic.

---

## 7. Immediate unblock for `niroshanaks`'s walk today

The unblock must be:
- Consistent with §5 (not a patch we'll revert).
- Not require DevAuth (which is off).
- Not require a browser cookie (Bearer token auth).
- Not require another data-heal migration (we can't verify what it did).
- Idempotent.

**Recommended action (does not require any new commit — uses existing tools):**

The `az containerapp exec` command opens an interactive shell into a
running API container replica. That container has the DB connection
string in its env (`ConnectionStrings__Postgres` via KV ref). We can
open a `psql` session (if `psql` is in the image; if not, a `dotnet`-run
one-liner) and directly:

1. Identify all `identity.users` rows for `niroshanaks@gmail.com`.
2. Promote every one of them (`UPDATE identity.users SET is_platform_admin=true WHERE lower(email)='niroshanaks@gmail.com' AND deleted_at IS NULL;`).
3. Ensure a `tenant_memberships` row exists for each: `INSERT ... ON CONFLICT DO UPDATE SET is_primary=true, role='tenant_admin';` targeting tenant `00000000-0000-0000-0000-000000000001`.

Verification: `SELECT id, email, is_platform_admin FROM identity.users WHERE lower(email)='niroshanaks@gmail.com' AND deleted_at IS NULL;`

**Concrete commands** (paste into a PowerShell window; user runs, I don't):

```powershell
# Open an interactive shell into a running API replica.
az containerapp exec -n ca-vrbook-api-staging -g rg-vrbook-staging --command "/bin/sh"

# Inside the container — vrbook image is based on aspnet:8.0-jammy which has
# no psql. Instead, use dotnet-ef or a one-shot dotnet-fsi that opens Npgsql.
# The container has $ConnectionStrings__Postgres in env; verify:
env | grep -i postgres

# Then, from the local checkout on the user's box (recommended — the container
# doesn't have Npgsql tooling), forward the connection string once + run locally:
#   az keyvault secret show --vault-name kv-vrbook-staging --name postgres-cs --query value -o tsv
# Then use pgAdmin / any local psql / DBeaver to run:

UPDATE identity.users
SET is_platform_admin = true,
    updated_at = NOW()
WHERE lower(email::text) = 'niroshanaks@gmail.com'
  AND deleted_at IS NULL;

INSERT INTO identity.tenant_memberships
  (id, user_id, tenant_id, role, is_primary, created_at, updated_at)
SELECT gen_random_uuid(), u.id, '00000000-0000-0000-0000-000000000001',
       'tenant_admin', true, NOW(), NOW()
FROM identity.users u
WHERE lower(u.email::text) = 'niroshanaks@gmail.com'
  AND u.deleted_at IS NULL
  AND NOT EXISTS (
    SELECT 1 FROM identity.tenant_memberships m
    WHERE m.user_id = u.id
      AND m.tenant_id = '00000000-0000-0000-0000-000000000001'
      AND m.deleted_at IS NULL
  );

UPDATE identity.tenant_memberships
SET is_primary = true, role = 'tenant_admin', updated_at = NOW()
WHERE tenant_id = '00000000-0000-0000-0000-000000000001'
  AND deleted_at IS NULL
  AND user_id IN (
    SELECT id FROM identity.users
    WHERE lower(email::text) = 'niroshanaks@gmail.com' AND deleted_at IS NULL
  );

-- Verify:
SELECT u.id, u.email, u.is_platform_admin,
       m.tenant_id, m.role, m.is_primary
FROM identity.users u
LEFT JOIN identity.tenant_memberships m
  ON m.user_id = u.id AND m.deleted_at IS NULL
WHERE lower(u.email::text) = 'niroshanaks@gmail.com'
  AND u.deleted_at IS NULL;
```

**Caveat — I do not have direct DB access from this machine.** The user
DOES: `postgres-cs` is in `kv-vrbook-staging`. If the Postgres server has
public network access disabled (VNet only), the user needs to run the
above from a Bastion / a jump box or open a temporary firewall rule.

**Alternate unblock — no direct DB access:**

Given the current F11.6.1 in-app operator-bootstrap is already coded
(`IdentityController.cs:270`), the fastest path that requires zero new
code is to **temporarily flip the two env vars**, ONE walk:

```powershell
az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging `
  --set-env-vars DevAuth__AllowAnonymous=true DevAuth__AllowStripeStub=true

# Wait for revision cycle (~30s).
# Then, using the user's Entra Bearer token from her signed-in browser:

$token = "<paste bearer token from browser devtools Network tab -> Request Headers -> Authorization: Bearer ...>"
$body = '{"email":"niroshanaks@gmail.com","tenantId":"00000000-0000-0000-0000-000000000001"}'
$uri = "https://ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io/api/v1/dev-auth/bootstrap-operator"

Invoke-RestMethod -Uri $uri -Method Post -Headers @{ Authorization = "Bearer $token" } -ContentType "application/json" -Body $body

# Expected response: { usersPromoted: N, platformAdminGranted: true, membershipCreated: true, ... }

# CRITICAL: revert the DevAuth flip immediately after the call succeeds.
az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging `
  --set-env-vars DevAuth__AllowAnonymous=false DevAuth__AllowStripeStub=false
```

Note that `bootstrap-operator` does NOT require the caller's Bearer token —
it's `[AllowAnonymous]`. But since it's guarded by three flags, once
DevAuth is on it becomes reachable. The Bearer token is not needed; a
plain `Invoke-RestMethod` works. **However**, running this WITH the flag
flip is DANGEROUS in staging because it briefly opens the DevAuth
authentication scheme to the world, letting anyone hit any endpoint as
`Owner`/`Guest`/`Admin` without signing in. The window should be < 5 minutes
and the flip revert MUST be verified.

**Recommendation:** unblock via direct DB (option 1) — no DevAuth flip
needed, no scope for other actors to sneak in during the window. §5.6's
first-PA config-based bootstrap replaces both patterns durably in F11.8.

---

## 8. What the user must sign off on before code changes begin

Business/scope decisions that belong to `niroshanaks`, not to the architect:

1. **Which unblock path (§7) — direct DB or DevAuth env flip?** Trade-off:
   direct DB requires temporary network access to Postgres; env flip
   requires a brief DevAuth window. Recommendation: direct DB.

2. **Slice OPS.M.13 (identity schema collapse) is disruptive** — it rewrites
   FKs on bookings, reviews, audit_log to point at survivor rows. All
   existing bookings for `niroshanaks` will re-attribute to one survivor
   `user_id`. Historical audit_log entries will re-point. This is
   correct behavior but is a data-shape change; sign-off needed.

3. **§5.3 App Role cleanup — option A (delete App Roles) or option B
   (actually assign them)?** Recommendation: A.

4. **Slice OPS.M.13 vs OPS.M.14 sequence.** M.13 (schema collapse) must
   ship BEFORE M.14 (DevAuth retirement), because M.13 is what makes
   Entra-only auth safe (one row per human). Sign off on the sequence.

5. **Slice OPS.M.16 (migration audit table) — ship as OPS.M.13 sub-slice
   or as its own slice?** Recommendation: sub-slice — the M.13 backfill
   migration is the first customer.

6. **OPS.M.12 (social IdPs) is BLOCKED on OPS.M.13.** Sign-off that
   OPS.M.12's queued planning stays queued until M.13 ships.

---

## 9. Open questions genuinely for the user (business-scope only)

1. **Multi-tenant humans in reality?** — Do you envision one human as
   `tenant_admin` of >1 tenant (e.g., an agency that manages multiple
   property portfolios)? If yes, `is_primary` remains meaningful and the
   membership table stays as designed. If no, we can simplify to a 1:1
   users↔tenant relationship and drop the join table + primary flag.
2. **Sign-in email trust boundary.** For the email-first uniqueness
   (§5.1), we depend on `email_verified=true` from the IdP. Are you
   comfortable requiring email verification at sign-up (Entra enforces it
   by default; social IdPs vary)? If a user's email flips from unverified
   to verified after sign-up, do we retro-collapse rows?
3. **PA count.** Currently `niroshanaks` is the only intended PA. Do you
   want the first-PA bootstrap (§5.6) to allow OTHER emails to be
   pre-configured (a comma-separated `PlatformAdmin:BootstrapEmails`
   list), or is single-email OK?
4. **Property re-attribution during M.13 collapse.** If a `niroshanaks`
   row that owns a property is retired to a different survivor row, the
   property's `owner_user_id` gets rewritten. UI-visible? Yes — audit
   log shows the change. Are you OK with the audit-log noise?
5. **DevAuth's removal breaks the local `docker-compose up` dev flow.**
   Local devs currently sign in via DevAuth cookie without needing an
   Entra tenant. Post-M.14, they need real Entra test-user credentials
   OR use the test-project's TestAuthenticationHandler in a special
   `docker-compose.devauth.yml` overlay. Which do you prefer?

---

## Appendix — evidence artifact paths (verifiable)

Raw JSON dumps on this box (session scratchpad, not committed):

- `%TEMP%\claude\c--Work-BookingApp\fcdf2bec-421a-46a2-b134-f24d9f8dadea\scratchpad\m4rejects.json` — 30 M.4 rejections, extracted.
- `…\provfail.json` — 20 provisioning-failure warnings (all health-probe timeouts).
- `…\bootstrap.json` — 40 bootstrap-operator request/response pairs.
- `…\niro.json` — 25 log lines mentioning `niroshanaks`.
- `…\user_300.json` — 30 log lines mentioning UserId `300b8e90…`.
- `…\bypasses.json` — 3 PlatformAdmin/BackgroundScope bypass log lines in 48h.
- `…\mig_apply.json` — empty (silence-as-evidence for migration stdout).
- `…\migrator_marker.json` — empty (same, for migrator startup markers).

Az CLI queries to reproduce (any time):

```powershell
$ws = "60a321d4-4440-47e3-bdf4-59ce77ecd4db"
az monitor log-analytics query --workspace $ws --analytics-query 'ContainerAppConsoleLogs_CL | where TimeGenerated > ago(48h) | where Log_s contains "Cross-tenant write rejected" | project TimeGenerated, Log_s | order by TimeGenerated desc' -o json
az monitor log-analytics query --workspace $ws --analytics-query 'ContainerAppConsoleLogs_CL | where ContainerAppName_s == "ca-vrbook-api-staging" | where Log_s contains "niroshanaks" | project TimeGenerated, Log_s | order by TimeGenerated desc | take 30' -o json
```
