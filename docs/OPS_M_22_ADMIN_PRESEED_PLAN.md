# OPS.M.22 — Admin Pre-Seed Design Plan

- **Status:** LOCKED — §7 architect-recommended answers adopted per memory rule `feedback_technical_decisions_are_architect_call`. Ready to execute.
- **Date:** 2026-07-07.
- **Author:** Platform Enterprise Architect (agent) via M.22 planning consult.
- **Trigger:** Owner surfaced 2026-07-07 that Platform Admins + Tenant Admins MUST be pre-seeded with DB entries BEFORE first sign-in. M.12/M.13/M.14 stack shipped lazy provisioning which is right for guests, wrong for admins. Gap discovered when owner tried first sign-in against post-INFRA.1 V2 DB (0 users) and hit a 403-inducing shape.
- **Predecessors:** [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md), [`OPS_M_15_CLOSE_OUT.md`](OPS_M_15_CLOSE_OUT.md), [`OPS_M_21` amendments](adr/0014-app-roles-global-db-per-tenant.md), [`OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md`](OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md).

---

## §0 What we're doing + why now

**Owner requirement (2026-07-07):** Platform Admins and Tenant Admins MUST have an `identity.users` row that is **pre-created by an operator** BEFORE the person tries to sign in. The row carries the (provider='entra', external_id=<oid>) mapping AND, for tenant admins, the `tenant_memberships` row. Guests continue self-serve.

**Why now:** The M.12/M.13/M.14 stack shipped lazy provisioning — the row appears on first authenticated request. That is right for guests, wrong for admins. Today `niroshanaks@gmail.com` exists in Entra CIAM but has 0 rows in the DB; if he signs in successfully, `UserProvisioningMiddleware` will happily create a `identity.users` row with `is_platform_admin=false` and NO tenant memberships. Nothing distinguishes "the owner just onboarded" from "some random Entra local signup". Admin authority currently requires an out-of-band `SeedTenantMembership` call OR the SQL runbook — both assume the user has ALREADY signed in once. Chicken-and-egg for the first admin.

**The gap:**
1. No operator surface to pre-create an `identity.users` row before first sign-in.
2. Pre-seed shape must resolve the (email + Entra oid) lookup key BEFORE the user's first token arrives.
3. `SeedTenantMembership` requires an existing OID mapping → won't work until first sign-in → circular dependency.
4. `UserProvisioningMiddleware` must gate lazy provisioning: **admins must exist first; guests may be created lazily**.

**The owner-locked shape (§7-Q1, Q3, Q4 below):**
- Operator creates the Entra CIAM user via portal + operator seeds an email-only row into `identity.users` via a VrBook admin surface.
- On first sign-in via `AdminSignUpSignIn` flow → middleware finds the pre-seeded row via email match, links the arriving oid, admin authority holds.
- On first sign-in via `GuestSignUpSignIn` flow with unknown email → provisions as guest (unchanged from today).
- If someone signs in via `AdminSignUpSignIn` with an unknown email → refused 401 `admin_account_not_provisioned` with SPA rejection page.

---

## §1 Sub-commit sequence

Eight sub-commits mirroring the M.12 shape. RED-then-GREEN where guard tests bite the change.

| # | Slice | Scope |
|---|---|---|
| M.22.1 | RED arch tests | Pin the new invariants: admin-flow tokens on unknown emails DO NOT provision; `UserProvisioningMiddleware` reads flow marker; `SeedAdminUserCommand` exists. Zero code impact. |
| M.22.2 | `SeedAdminUserCommand` + handler + migration | Adds `POST /api/v1/admin/platform/users/seed` for PlatformAdmin. Takes `{email, displayName, isPlatformAdmin, tenantMemberships[]}`. Adds `identity.users.pre_seeded_at` timestamp column. Idempotent on normalized email. |
| M.22.3 | Flow-marker claim reader | `HttpCurrentUser.EntraFlow` reads `tfp`/`acr`/custom claim. Config `EntraExternalId:AdminFlowName`. |
| M.22.4 | `UserProvisioningMiddleware` admin-gate | Unknown-email + admin-flow token → throw `AdminAccountNotProvisionedException` (401 `admin_account_not_provisioned`). Email-match + `pre_seeded_at IS NOT NULL` + admin-flow → link the oid. Guest-flow path unchanged. |
| M.22.5 | Bootstrap `vrbook-admin` PowerShell cmdlet | `seed-platform-admin`, `seed-tenant-admin`, `list`, `revoke`. First-admin path uses direct DB SQL via managed identity (chicken-and-egg). Deletes legacy `grant-self-admin.ps1` (post-M.15 dead — writes to defunct extension attributes). |
| M.22.6 | Bicep-declarative backfill | `Bootstrap:SeedPlatformAdmins:[{email, displayName}]` config. Migrator job runs one-time idempotent insert for pre-M.22 admin accounts. |
| M.22.7 | SPA rejection page + `AdminAuthGuard` extension | `/auth/admin-not-provisioned` counterpart to `/auth/admin-social-idp-rejected`. Copy: "Your account hasn't been provisioned. Contact your operator with your sign-in email." + sign-out CTA. |
| M.22.8 | Close-out + ADR-0017 + cross-surface arch tests | ADR `admin-preseed-required`. Cross-surface arch tests analogous to M.12.8 pattern. |

**Total: 8 sub-commits, ~3 days effort. Session budget: 2 sessions.**

---

## §2 What survives, what needs new work

### Survives from M.12 (no change)

- `AdminSocialIdpRejectionMiddleware` (Layer 2) — untouched. Admins are Entra-local by construction post-M.22, so the layer never fires on the happy path but remains defense-in-depth.
- `ProvisionOrLinkUserHandler.RefuseIfAdminSocialLinkAsync` (Layer 1) — untouched.
- Two Entra user flows (`AdminSignUpSignIn` + `GuestSignUpSignIn`). M.22 leverages this split.
- Guest lazy provisioning (`UserProvisioningMiddleware` Branch 3) — untouched for guest flow.

### Survives from M.15/M.21

- `is_platform_admin` boolean column stays authoritative for platform admin.
- `tenant_memberships` table stays authoritative for tenant admin.
- `TenantAuthorizationBehavior` — no interaction.

### Survives from M.10.2 F11.3

- `SeedTenantMembershipCommand` and endpoint STAY as-is. M.22.2 introduces a **superset** command that handles the pre-first-sign-in case; `SeedTenantMembership` handles the post-sign-in case (existing users, adding tenant memberships).

### Needs new work

- **Flow-marker claim plumbing** (M.22.3).
- **Middleware admin-gate** (M.22.4).
- **Pre-first-sign-in seeding** — `SeedAdminUserCommand` + `pre_seeded_at` column (M.22.2).
- **Bootstrap PowerShell** — new `vrbook-admin` cmdlet; legacy `grant-self-admin.ps1` deleted (M.22.5).
- **Backfill** — `SeedPlatformAdmins` config array + Migrator-time seeding (M.22.6).

---

## §3 Migration surface + new API endpoints

### Endpoint

| Endpoint | Method | Auth | Body | Response |
|---|---|---|---|---|
| `/api/v1/admin/platform/users/seed` | POST | `[Authorize(Roles="PlatformAdmin")]` | `SeedAdminUserRequest { email, displayName, isPlatformAdmin, tenantMemberships[] { tenantId, role, isPrimary } }` | `SeedAdminUserResult { userId, created, membershipsCreated[] }` |

Sits alongside `TenantsPlatformController.SeedMembership` (which STAYS). Idempotency: if email exists → return existing user id + honour merge on new `tenantMemberships[]`; if email doesn't exist → create fresh row with `pre_seeded_at=NOW()`.

The **first-admin bootstrap** (chicken-and-egg): endpoint requires PlatformAdmin. For the very first platform admin, PowerShell talks to DB directly via managed identity connection (§6).

### DB Migration

- `OpsM22_UsersAddPreSeededAtColumn` — nullable `pre_seeded_at timestamptz`. NULL for guests (created lazily). Non-null for admin rows created by seed. Kept as audit trail after linking. `Down` drops the column.

### Config keys (new)

- `EntraExternalId:AdminFlowName` — the exact `tfp`/`acr` value that identifies the admin user flow (e.g. `B2C_1A_AdminSignUpSignIn`).
- `EntraExternalId:GuestFlowName` — the guest flow value (defensive validation only).
- `Bootstrap:SeedPlatformAdmins:[{email, displayName}]` — Bicep-provided array read at Migrator run for backfill.

---

## §4 Risks + mitigations

| # | Risk | L | I | Mitigation |
|---|---|---|---|---|
| 1 | Flow-marker claim name varies by Entra CIAM config | H | H | M.22.3 reads config-configurable claim name AND falls back through ordered list. Runbook documents portal config. Integration test locks against staging token payload. |
| 2 | First-admin bootstrap has no PlatformAdmin caller (chicken-and-egg) | Certainty | M | `vrbook-admin seed-platform-admin --first` writes SQL directly via managed identity DB connection. Audit log records `updated_by=system:bootstrap`. |
| 3 | Operator creates Entra user with mismatched email vs seed request | M | M | Endpoint validates caller supplies email; on first sign-in, middleware Branch 2 (email hit) does the oid capture; mismatch → 500 "seed mismatch" with structured log. |
| 4 | Pre-seed rejected admin has valid Entra token → gets 401 loop | H | L | Rejection returns 401 with problem type `admin_account_not_provisioned` + copy "contact operator". SPA rejection page (M.22.7) offers sign-out CTA. |
| 5 | Guest signs up, gets promoted to tenant admin, retains social identity row | M | M | Existing M.12.4 REFUSE-AT-PROVISIONING handles sign-in-time refusal. Promote flow (future slice) must refuse if any social user_identities row exists; owner-locked in M.12 §11-Q4. |
| 6 | Backfill list drifts vs actual Entra admins | L | M | Backfill idempotent; missing entries surface as `admin_account_not_provisioned` on next sign-in; operator adds via endpoint. |
| 7 | Operator seeds email X but Entra CIAM user has different email → oid mismatch on sign-in | M | H | Same as #3. Explicit 500 with structured diagnostic. Manual reconciliation via `vrbook-admin` cmdlet. |

---

## §5 Owner-lock questions (all locked to architect recommendation per memory rule)

### Q1 — [POLICY] Operator UX shape

- **(a)** Two-step: operator creates Entra user via portal → runs `vrbook-admin seed-platform-admin --email X`. Requires portal access + one CLI call.
- **(b)** VrBook backend calls Microsoft Graph via app-only credentials to create BOTH the Entra user AND the DB row in one endpoint. Requires Graph API secret in KV + `User.ReadWrite.All` scope.

**Locked: (a).** Rationale: (b) needs Graph API integration surface + secret rotation + higher-risk permissions; (a) is 1-day-done. Owner already operating in mode (a).

### Q2 — [POLICY] Force-password-change on first sign-in

**Locked: enforce via Entra CIAM policy.** Runbook update, no VrBook code.

### Q3 — [TECHNICAL — architect resolves] Pre-seed shape: email-only vs email+oid

- **Email-only** (recommended): Operator provides `{email, displayName, isPlatformAdmin, memberships[]}`. DB stores shell row with `pre_seeded_at=NOW()`. First admin-flow sign-in: middleware finds email match, links arriving oid to fresh `user_identities` row.
- **Email+oid**: Operator provides both. Fully atomic seed. Requires operator to read oid from Entra portal.

**Locked: email-only.** UX simplicity beats atomic robustness. `pre_seeded_at` timestamp column distinguishes "operator vouched" from "random signup".

### Q4 — [POLICY] Existing pre-M.22 admins (owner)

**Locked: (a) declarative Bicep.** `Bootstrap:SeedPlatformAdmins:[{email: "niroshanaks@gmail.com", displayName: "Niroshana"}]` in Bicep. Migrator backfill runs on next deploy. First-admin PowerShell bootstrap remains as escape hatch.

### Q5 — [POLICY/SCOPE] Tenant-admin invite flow (post-M.7)

**Locked: DEFER to a follow-up slice (OPS.M.22.9 or a dedicated OPS.M.7.1).** M.22 scope: PlatformAdmin-driven seeding lands + proves out. Tenant-admin-mediated invite endpoint is a thin wrapper on `SeedAdminUserCommand` — belongs to a subsequent slice.

---

## §6 Technical answers I resolve directly (architect's call per memory rule)

- **Middleware admin-gate refusal HTTP code:** 401 (not 403). Token valid, account not provisioned. Problem type `admin_account_not_provisioned`. Whitelist `/api/v1/me` + `/api/v1/me/tenants` (M.12 Layer 2 pattern).
- **Guest flow bypass:** middleware reads `EntraFlow`; guest flow → Branch 3 fires normally. Only admin flow triggers pre-seed check.
- **`pre_seeded_at` semantics:** nullable timestamptz. NULL for guests. Non-null for admin rows created by seed. Kept as audit trail after linking.
- **`ProvisionOrLinkUserHandler` Branch 2 email-hit-identity-miss:** post-M.22, admin-flow tokens on email-hit path only link IFF `pre_seeded_at IS NOT NULL`. Guest-flow tokens on email-hit path work as today.
- **First-admin bootstrap SQL path:** `vrbook-admin seed-platform-admin --first` opens direct psql via Container Apps managed identity, executes same DML as handler, wrapped in transaction with actor_user_id `00000000-0000-0000-0000-000000000000` sentinel.
- **Idempotency:** endpoint returns 200 with `created=false` if same email already exists; 201 with `created=true` on fresh insert. Email conflict on different oid → 409.
- **Tenant existence validation:** endpoint validates all `tenantMemberships[].tenantId` against `identity.tenants` before writing (single transaction; validation fails → nothing written).
- **`AdminSocialIdpRejectionMiddleware` interaction:** unchanged. M.22 is orthogonal to M.12.
- **`SeedTenantMembership` deprecation:** keep. Handles "user exists post-first-sign-in, add tenant membership" case. Two commands compose cleanly.
- **`grant-self-admin.ps1` removal:** safe. Post-M.15 dead code (writes to defunct extension attributes).

---

## §7 Close-out checklist

- [ ] `SeedAdminUserCommand` + handler + 8+ integration tests.
- [ ] `OpsM22_UsersAddPreSeededAtColumn` migration idempotent + Down-safe.
- [ ] `HttpCurrentUser.EntraFlow` accessor + 4 shape unit tests.
- [ ] `UserProvisioningMiddleware` admin-gate: 5 integration tests.
- [ ] Bicep + `10-store-secrets.ps1` seed new config keys.
- [ ] `Bootstrap:SeedPlatformAdmins` backfill idempotent.
- [ ] `vrbook-admin` PowerShell cmdlet + `--first` mode; `grant-self-admin.ps1` deleted.
- [ ] SPA `/auth/admin-not-provisioned` page + `AdminAuthGuard` extension.
- [ ] ADR-0017 committed.
- [ ] MASTER_PLAN row flipped ✅.
- [ ] Close-out doc `OPS_M_22_CLOSE_OUT.md`.
- [ ] Cross-surface arch tests pinning 5 M.22 invariants.
- [ ] Staging walk: owner seeds himself via `vrbook-admin --first`, signs in via admin flow, hits `/admin/platform/tenants` → 200.
- [ ] Prod cutover: backfill config lists owner + team leads BEFORE first prod deploy.

---

Ready to execute Slice OPS.M.22.1.
