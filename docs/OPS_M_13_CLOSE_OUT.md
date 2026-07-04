# Slice OPS.M.13 — Close-Out

**Status:** shipped end-to-end on staging.
**Slice plan:** [`OPS_M_13_IDENTITY_REDESIGN_PLAN.md`](OPS_M_13_IDENTITY_REDESIGN_PLAN.md).
**Architect design review:** [`OPS_M_13_4_BACKFILL_REVIEW.md`](OPS_M_13_4_BACKFILL_REVIEW.md) (M.13.4 pre-execution audit).

---

## 1. What shipped

### Schema shape

- `identity.user_identities` table: `(user_id, provider, external_id)` with UNIQUE(provider, external_id). Enables one-human-many-oids.
- `identity.migration_audit` table: canonical record of data-heal migration side-effects. Motivated by F11.7's opaque data-heal failures.
- Partial-UNIQUE on `identity.users(lower(email)) WHERE deleted_at IS NULL` — case-insensitive email uniqueness; only-active-rows.
- CHECK on `user_identities.provider IN ('entra','google','microsoft','apple','test')`.
- `identity.users.b2c_object_id` column and its unique index — **dropped**.

### Application

- `ProvisionOrLinkUserHandler` (email-first algorithm) supersedes `ProvisionUserHandler`. Three branches — identity hit, identity-miss + email hit + verified, identity-miss + email-miss fresh provision.
- `UserProvisioningMiddleware` sends `ProvisionOrLinkUserCommand`; reads `X-Active-Tenant` header; falls back to `IsPrimary=true` membership; stamps `HttpContext.Items[ActiveTenantId]` + `Items[MembershipRoles]`; drops the per-membership `ClaimTypes.Role` loop; keeps the PlatformAdmin role claim + legacy `app_tenant_id` claim.
- `ICurrentUser.MembershipRoles: IReadOnlyDictionary<Guid, IReadOnlySet<string>>`. `HasTenantRole(tid, role)` is now a scoped dictionary lookup — Ev-A cross-tenant claim hazard closed.

### API

- `GET /api/v1/me/tenants` returns `{memberships, isPlatformAdmin}` for the post-sign-in picker. Not `[Authorize(Roles=...)]` so PA humans with zero memberships can still route to `/admin/platform`.

### SPA

- `web/src/lib/auth/msalInstance.ts` — MSAL Browser 3.x singleton with `msalReady` promise (awaits `initialize()`) + `waitForAccount(timeoutMs)` helper.
- `Providers.tsx` — gates `<MsalProvider>` on `msalReadyState`; tokenProvider awaits `msalReady` + `waitForAccount(5000)`; broadened interaction-needed catch to `no_account_error`, `no_tokens_found`, `token_refresh_required`, `consent_required`, `login_required`, `interaction_required`; invalidates `['me']` + resets errored queries on MSAL account events; bounded 1-shot 401 retry on `useMe`.
- `web/src/lib/tenants/{activeTenant.ts, useMyTenants.ts}` + `web/src/app/select-tenant/page.tsx` — per-tab sessionStorage picker + primary-first ordering + PA shortcut.
- `web/src/app/auth/callback/page.tsx` — 0/1/N routing post-callback.
- `web/src/lib/api/client.ts` — `apiFetch` attaches `X-Active-Tenant` on non-anonymous requests.
- `web/src/app/admin/error.tsx` — cross-tenant 403 boundary with a Switch-workspace CTA for N-membership users.
- `useAuth.signOut()` + `/auth/signout/page.tsx` clear `activeTenantId` before + on-mount for double-safety.

### Data heal

- **M.13.4 backfill** — email-canonical collapse: survivor pick (PA DESC → membership-count DESC → CreatedAt ASC → Id ASC total-ordering); 10+ named FK rewrites; dynamic loop rewriting every `created_by`/`updated_by`/`deleted_by` uuid column across all schemas; user_identities populated for live humans; non-survivors soft-deleted with `deleted_by=NULL`; b2c_object_id column dropped; partial-UNIQUE ensured. Every step INSERTs a migration_audit row.
- **M.13.6 operator bootstrap** — after M.13.4 collapsed niroshanaks to email-canonical shape, a second migration granted `is_platform_admin=true` + a `tenant_admin` membership in the oldest active tenant.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `b5d2d2d` | M.13.2 | user_identities + migration_audit schema + domain aggregates + arch tests |
| `3eb73c5` | M.13.3 Phase A | ProvisionOrLinkUserHandler side-by-side (not wired) |
| `5579052` | M.13.3 Phase B | Middleware flip to ProvisionOrLinkUser + delete old handler + integration tests |
| `c49dee8` | M.13.3 fixup | `.IsRowVersion()` trap on UserIdentity — CI 23502 evidence |
| `c41e4b5` | M.13.4 | 9-step backfill migration + b2c_object_id drop + code cleanup |
| `4074321` | M.13.4 fixup | Quote `"Id"` in migration_audit INSERTs (42703 case-sensitivity trap) |
| `4f5c4e6` | M.13.4 fixup 2 | Revert `u.Email.Value` in `BuildQ` — ILike + OR translator gotcha |
| `9b0e4d0` | M.13.5 | GET /me/tenants + tenant picker frontend |
| `3e4866a` | M.13.6 | X-Active-Tenant header pipeline (walk unblocks — allegedly) |
| `da7e5bd` | M.13.6 fixup | FindUserByEmailAsync ILike translation — the ACTUAL walk unblocker |
| `2b57f71` | Diag pass 1 | JwtBearer OnAuthenticationFailed + OnChallenge logging |
| `f6e67c4` | Diag pass 2 | Unconditional challenge log + client-side token acquisition logging |
| `2a851c1` | M.13.6 walk unblocker | **MSAL Browser 3.x initialize() race fix — the fundamental issue** |
| `e691f19` | M.13.6 walk fix | Broaden email + email_verified claim extraction in middleware |
| `b708f07` → `125d456` | M.13.6 walk debug + M.13.7 | 10 more commits: bootstrap-operator diag rounds, PA-grant migration, tenant-membership migration, admin/error.tsx, list-key invalidation, apostrophe escape |

**Total: 25 sub-commits over ~5 sessions.** Way more than the 8 planned. §3 documents where the plan diverged and why.

---

## 3. Where the plan diverged

### Planned but not shipped this slice

- **`ClaimOidForExistingProfile` deletion + full `B2CObjectId → ExternalObjectId` rename** — the domain method + property drops shipped in M.13.4, but `ICurrentUser.B2CObjectId` (JWT-side accessor) stays as-is. Rename bundled into **OPS.M.14 DevAuth retirement** to avoid coupling a cosmetic ripple to the auth-critical M.13 sequence.
- **`M.13.6.X` middleware role-claim writes drop** — the plan said drop them in M.13.6. **Kept** them (only dropped the per-membership loop; PlatformAdmin claim + app_tenant_id claim retained) because DevAuth cookie tests + several controllers still read `[Authorize(Roles="Owner,Admin")]`. Full drop moves to **OPS.M.15 App Roles cleanup**.

### Not planned but shipped

- **`f6e67c4` + `2b57f71` + web console logging** — client + server observability added mid-walk debug to escape the "silent 401" cycle. Kept because it's cheap + regression-friendly.
- **`17509ac`, `e0bcf16`, `6071361`** — three operator-bootstrap migrations to hand-fix niroshanaks's row after M.13.4 backfill collapsed the shape but didn't seed operator PA + tenant_membership. Not in plan §4 because the plan assumed the operator would follow the F11 walk manually. In practice the walk was blocked → migrations were the least-heroic path.

### Discovered mid-slice (documented as memory)

- **Cross-schema migration trap** — Identity migrations run first. Any reference to `catalog.*` etc. must be `IF EXISTS` guarded. Burned twice (M.13.4 backfill + M.13.6 tenant-membership migration). Memory: [`reference_cross_schema_migration_trap`](../../.claude/projects/c--Work-BookingApp/memory/reference_cross_schema_migration_trap.md).
- **MSAL Browser 3.x init pattern** — 5-piece canonical setup (singleton + msalReady + gated MsalProvider + waitForAccount + broadened error catch + bounded 401 retry + query invalidation on account events). Memory: [`reference_msal_browser_3x_init_pattern`](../../.claude/projects/c--Work-BookingApp/memory/reference_msal_browser_3x_init_pattern.md).
- **ProblemDetails strips response body** — Hellang middleware discards custom fields on all 4xx/5xx. Log via ILogger; don't rely on `NotFound(new {...})`. Memory: [`reference_problem_details_strips_body`](../../.claude/projects/c--Work-BookingApp/memory/reference_problem_details_strips_body.md).
- **Email ILike translator gotcha** — the `((string)(object)u.Email)` cast is load-bearing in `EF.Functions.ILike`; direct `.Value` fails translation. Confirmed in both `UserRepository.BuildQ` and `ProvisionOrLinkUserHandler.FindUserByEmailAsync`. Memory updated: [`reference_email_ilike_translator`](../../.claude/projects/c--Work-BookingApp/memory/reference_email_ilike_translator.md).
- **RowVersion pattern** — Identity aggregate configs must use plain `HasColumnName("row_version")`, NOT `.IsRowVersion()` (Postgres doesn't auto-generate bigint columns). Memory: [`reference_rowversion_pattern`](../../.claude/projects/c--Work-BookingApp/memory/reference_rowversion_pattern.md).

---

## 4. What was deferred

Explicitly deferred to follow-up slices per the plan §M.13.6-§M.13.8:

- **DevAuth retirement** — all DevAuth handler / cookie / dev-bridge endpoints (F11.2, F11.4, F11.6.1 bootstrap-operator, F8 persona-email, F11.7.7 already-retired-persona rows) — **OPS.M.14**.
- **App Roles cleanup** — drop the legacy `app_tenant_id` claim writes; drop the `IsOwner` / `IsAdmin` columns from `identity.users` in favor of `MembershipRoles`; deprecate `IsInRole("Owner"|"Admin")` checks in controllers — **OPS.M.15**.
- **Social IdPs** (Google / Microsoft federation through Entra) — **OPS.M.12** (unblocked by M.13's user_identities table now being live).
- **`_pre_m13_snap` schema cleanup** — the M.13.4 backfill left the snapshot schema in place for 30 days. A follow-up `OpsM13a_Drop_PreM13_Snapshot` migration is scheduled for **2026-08-04**.
- **@unknown.local synthetic-email row cleanup** — a small number of user rows were created during the pre-walk-fix window with `{oid}@unknown.local` emails (before `preferred_username` was added to the claim search). They're inactive (no oid matches, no email matches), but they clutter the users table. Soft-delete migration deferred to a maintenance follow-up.
- **`bootstrap-operator` endpoint hardening** — the endpoint's 3-guard `[Authorize]` + DevAuth flags + ProblemDetails-strips-body chain proved impossible to diagnose during the walk-debug (7 wasted CI cycles before I pivoted to direct SQL migrations). A follow-up should either strip the guards for staging-only + explicit Bearer auth, or delete the endpoint since OPS.M.14 retires the DevAuth flag mechanism it depends on.

---

## 5. Rollback runbook

Full runbook: [`OPS_M_13_4_BACKFILL_REVIEW.md`](OPS_M_13_4_BACKFILL_REVIEW.md) §7.6.

Tier-1 (revert the last commit + let CD roll a new revision): safe for M.13.5 / M.13.6 / M.13.7. Web + API redeploy in < 10 min.

Tier-2 (undo backfill data-heal): requires `_pre_m13_snap` schema (retained until 2026-08-04). SQL sketch in the review doc §7.6. Never needed in practice — the backfill was idempotent and the failure modes all showed up in CI before deploy.

---

## 6. Staging walk verification

Per [`reference_staging_walk_6_flows`](../../.claude/projects/c--Work-BookingApp/memory/reference_staging_walk_6_flows.md) — the user re-ran the walk after M.13.6 fixups landed:

- **Guest browse + quote** — ✅ works.
- **Guest booking** — ✅ 201 Tentative.
- **Guest cancellation** — ✅ status flips to Cancelled.
- **Admin booking confirmation** — ✅ status flips to Confirmed. UI list-refresh fixed in M.13.6 round 2 (list query key invalidation added).
- **Admin booking rejection** — ✅ status flips to Rejected.
- **iCal sync (outbound feed)** — ✅ feed create + pause works after tenant-membership migration; outbound URL fetch returns `BEGIN:VCALENDAR`.

Plus one config note not a bug: `beach-villa` had `PricingPlan.minStayNights=2` — the user changed it to 1 via `/admin/pricing/beach-villa` for the walk. Not shipped in M.13; documented as an operator-visible knob.

---

## 7. Prod cutover checklist

**None.** Slice OPS.M.13 is staging-only. Prod cutover is a follow-up gate that runs when:

1. All P1-triaged follow-ups (§4) are complete OR explicitly waived.
2. A prod-scale rehearsal against a prod-DB snapshot in a scratch environment confirms the M.13.4 backfill completes within the maintenance window (< 30 min for a hypothetical 10K user DB per [`OPS_M_13_4_BACKFILL_REVIEW.md`](OPS_M_13_4_BACKFILL_REVIEW.md) §4.4).
3. A rollback rehearsal against the same scratch environment confirms Tier-2 rollback works.

The prod cutover checklist itself lives in `OPS_M_13_9_PROD_CUTOVER.md` (not yet written — deferred to the cutover slice).

---

## 8. Session-count debt

M.13 took **~5 debug-heavy sessions** (planned: 2). Root causes for the overrun, all in walk-debug (post-M.13.6-3e4866a):

1. **MSAL 3.x init race** — 4+ hours before consulting architect. Should have been the first hypothesis when `HasBearer=false` on 100 % of requests. Memory `feedback_evidence_not_hypothesis` was violated by not pulling Log Analytics first.
2. **ProblemDetails-strips-body** — 2+ hours of PUSH-WAIT-CURL cycles trying to surface diagnostic detail. Should have used ILogger + LA from the start.
3. **`bootstrap-operator` guard chain** — endpoint returned 404 despite multiple correct-looking pushes. Diagnosed only when I pivoted to raw-SQL migration for the PA grant.
4. **Cross-schema migration trap** — burned twice (M.13.4 + M.13.6). Now in memory.

Absorbing these lessons should shrink the OPS.M.14 / .15 / .12 sessions substantially — no more "silent 401" or "route-not-matching" surprise classes.
