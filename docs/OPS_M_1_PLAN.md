# OPS.M.1 — Tenant aggregate + memberships schema (Plan)

**Status**: **Approved 2026-06-26** — executing. User decisions on open questions: migration name = `Slice5_Tenant_Membership_Schema`, ADR-0014 written in this slice, `is_primary` enforced app-level only, default seed uses USD + UTC.
**Author**: Plan agent (architect) consult, 2026-06-26.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row 6 ("OPS.M.1 — Tenant aggregate + memberships").
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §10 row OPS.M.1 + §1 (tenant attributes).
**Roles-architecture reference**: `docs/identity/roles-architecture.md` §3.2 (membership shape) + §4.3 (forward-look middleware enrichment).
**Predecessor**: OPS.M.0 closed 2026-06-26 on staging — App Roles primary, DB-backed per-tenant role secondary.
**Sequence**: After OPS.M.0; before OPS.M.2 (claim wiring + ICurrentUser reshape).
**Estimate**: 2 days. Single-engineer; backend-only.

This plan is the contract. OPS.M.1 is **schema-and-shape work only** — no app code consumes the new tables; no middleware change; no controller change. The migration runs cleanly on staging.

---

## 1. Scope summary

OPS.M.1 produces:
1. A real `Tenant` aggregate (extending the Slice 3 placeholder at `src/Modules/VrBook.Modules.Identity/Domain/Tenant.cs`) with the column set agreed in `MULTI_TENANCY_OPS_PLAN.md` §1.
2. A new `TenantMembership` aggregate at `src/Modules/VrBook.Modules.Identity/Domain/TenantMembership.cs` matching the shape reserved in `roles-architecture.md` §3.2.
3. EF configurations + one migration `Slice5_Tenant_Membership_Schema` (Slice numbering deliberate — see §6) that extends `identity.tenants` with the new columns and creates `identity.tenant_memberships`.
4. A backfill SQL block embedded in the migration that seeds a single `"VrBook Default"` tenant row (the OPS.M.3b cutover destination — see §5).
5. Domain unit tests for `Tenant` + `TenantMembership` invariants.
6. One EF migration round-trip test asserting the migration applies and rolls back cleanly on a fresh Postgres.

OPS.M.1 does **not** touch:
- `UserProvisioningMiddleware` (deferred to OPS.M.2 — roles-architecture.md §4.3).
- `ICurrentUser` shape (deferred to OPS.M.2).
- Any controller `[Authorize]` attribute (deferred to OPS.M.4 — see §2 below).
- Any per-module table (deferred to OPS.M.3 — bulk `tenant_id` column rollout).
- App Role values on Entra (`Owner`/`Admin` stay; see §2).
- The `users.tenant_id` column — see the load-bearing decision in §4.4.
- ADR-0014 (DB-backed per-tenant roles) — see §2.

---

## 2. Up-front decisions

### 2.1 Role-name rename (`Owner` → `tenant_admin`) — **defer to OPS.M.4**

`MULTI_TENANCY_OPS_PLAN.md` §1 ratified role names as `super_admin / tenant_admin / tenant_member / guest`. OPS.M.0 shipped Entra App Roles named `Owner` and `Admin` because every controller in the repo carries `[Authorize(Roles="Owner,Admin")]` and renaming them was out of scope for the OPS.M.0 close-out.

**Decision**: continue to defer the rename. OPS.M.1 introduces `tenant_admin` and `tenant_member` as **new** values inside `tenant_memberships.role` (per `roles-architecture.md` §3.2's CHECK constraint). The existing global App Roles `Owner` (== "I'm a property owner anywhere") and `Admin` (== "platform super admin") remain unchanged. The "Owner App Role → tenant_admin DB membership" semantic shift is precisely what OPS.M.4's `TenantAuthorizationBehavior` is for: that's where every controller gate gets rewritten from `IsInRole("Owner")` to `tenant_id == claim`.

Doing the rename here would mean (a) editing every controller attribute in this slice, (b) updating Entra App Role values via Graph, (c) bumping the App Role assignment of every existing user in staging — none of which has a dependency on OPS.M.1's schema work. The schema decouples cleanly: `tenant_memberships.role` carries the new vocabulary; the App Role carries the legacy one; OPS.M.4 retires the legacy.

### 2.2 ADR-0014 — **write now, in OPS.M.1**

`roles-architecture.md` §1 reserved ADR-0014 ("App Roles for global roles, DB for per-tenant roles") with the note "to be written when OPS.M.1's middleware change actually lands — not now." Two readings of that:

- Reading A — write with the middleware change (OPS.M.2). Argument: the ADR is "about" the middleware enrichment.
- Reading B — write now (OPS.M.1). Argument: the schema **is** the ADR's central claim. ADR-0014 says "per-tenant roles live in a DB table"; OPS.M.1 creates the table. Without the table the ADR's decision is unimplementable; once the table exists the ADR's decision is materially shipped (even if middleware reads from it next slice).

**Decision**: write ADR-0014 in OPS.M.1, *Status: Accepted*. The schema is the load-bearing artifact. Middleware enrichment in OPS.M.2 is a non-trivial 30-line consumer; that's a regular implementation step that cites the ADR, not a re-decision. Writing the ADR now also documents the OPS.M.0 reversal (extension claims → App Roles + DB) at the moment the supporting evidence is freshest, and gives OPS.M.2/M.4/M.8 a single citation.

If reviewer disagrees, the ADR shape stays the same; only the file date moves.

### 2.3 `users.tenant_id` column — **defer to OPS.M.3**

The User aggregate stays unmodified in OPS.M.1. Rationale: `users` is many-to-many with `tenants` via `tenant_memberships`. A `users.tenant_id` column would either (a) duplicate the `is_primary=true` membership (denormalization with a sync burden) or (b) represent something else like "home tenant" — but there's no such concept; the primary-membership flag is what serves the "which tenant am I currently acting as" question per `MULTI_TENANCY_OPS_PLAN.md` §2.

OPS.M.3 still owns the bulk `tenant_id` rollout to all module tables. It does NOT add a column to `users` — that was never in §3's table list (re-read: Catalog, Booking, Sync, Payment, Pricing, Messaging, Reviews, Notifications, Identity.audit_log — no `users`).

### 2.4 Backfill of the "VrBook Default" tenant — **seed in OPS.M.1, not OPS.M.3b**

`MULTI_TENANCY_OPS_PLAN.md` §10 says OPS.M.3b is where the backfill lands. That's about backfilling `tenant_id` onto module rows. But the **default tenant row itself** is a single INSERT into `identity.tenants` — it has zero dependencies, and shipping it in OPS.M.1 means OPS.M.2's middleware change can already resolve a tenant id at request time (it just won't matter for app behavior until OPS.M.3c flips the NOT NULL).

OPS.M.1 inserts one tenant row:
```sql
INSERT INTO identity.tenants (
  "Id", slug, display_name, status, default_currency, default_timezone,
  support_email, platform_fee_bps, created_at, updated_at, row_version)
VALUES (
  '00000000-0000-0000-0000-000000000001',
  'default', 'VrBook Default', 'Active', 'USD', 'UTC',
  'support@vrbook.example.com', 1500,
  NOW(), NOW(), 0)
ON CONFLICT ("Id") DO NOTHING;
```

The deterministic UUID is on purpose: it makes the OPS.M.3b update `SET tenant_id = '00000000-...-1'` an explicit literal, easy to grep and easy to roll back. Reviewer may prefer a runtime-generated UUID written to `infra/.state/<env>.json` instead — I argue against because (a) staging and prod want the same constant for migration scripts to be portable, (b) UUID-as-identifier-of-singleton-row is a known pattern (`00000000-...-1` reads as "row #1, the default").

### 2.5 `tenants.status` shape — **text + CHECK, not enum**

`MULTI_TENANCY_OPS_PLAN.md` §1 lifecycle is `PendingOnboarding → Active → Suspended → Closed → Deleted`. Postgres native enums require an `ALTER TYPE` to add new values which doesn't play well with EF migrations (Npgsql models them but they're sticky). `text NOT NULL CHECK (status IN ('PendingOnboarding','Active','Suspended','Closed'))` is the lower-blast-radius shape; `Deleted` is represented by `deleted_at IS NOT NULL` (already on `AggregateRoot`), so it's intentionally absent from the CHECK list.

---

## 3. Schema (ratified)

### 3.1 `identity.tenants` — extend the Slice 3 placeholder

Current columns from `20260613003433_Slice3_TenantsPlaceholder.cs`: `Id, slug, display_name, row_version, created_at, created_by, updated_at, updated_by, deleted_at, deleted_by`.

Added in OPS.M.1:

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| `status` | `text` | NOT NULL | `'PendingOnboarding'` | CHECK in 4 values per §2.5 |
| `default_currency` | `char(3)` | NOT NULL | `'USD'` | ISO 4217. USA-focused marketplace per user direction 2026-06-26 (originally `'EUR'` in the architect draft). Tenants in other regions can override at onboarding (OPS.M.7). |
| `default_timezone` | `text` | NOT NULL | `'UTC'` | IANA. **Fallback only** — actual display timezones are derived from each property's location (catalog enhancement, likely Slice 6/7). UTC is the safe canonical for admin reports when no property context is available. Originally `'Europe/Dublin'` in the architect draft; corrected per user direction. |
| `support_email` | `varchar(320)` | NOT NULL | `'support@vrbook.example.com'` | Per §8 of MTOP, separate from auth-principal email |
| `platform_fee_bps` | `int` | NOT NULL | `1500` | Basis points; 1500 = 15%; Super-Admin overridable later |
| `stripe_account_id` | `varchar(64)` | NULL | — | Set in OPS.M.5 |
| `stripe_account_status` | `text` | NULL | — | Mirrors `account.charges_enabled / payouts_enabled`; OPS.M.5 sets |
| `suspended_reason` | `varchar(500)` | NULL | — | Filled when status transitions to Suspended |

CHECK: `status IN ('PendingOnboarding','Active','Suspended','Closed')`.

No new indexes here — `slug` already has the unique index from Slice 3 (`IX_tenants_slug`). `stripe_account_id` will get an index in OPS.M.5 when webhook routing needs it; not now.

### 3.2 `identity.tenant_memberships` — new

Locked to the shape reserved in `roles-architecture.md` §3.2. Reproduced here as the source of truth:

| Column | Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK. PascalCase — see §3.4 |
| `user_id` | `uuid` | NOT NULL | FK → `identity.users("Id")` ON DELETE CASCADE |
| `tenant_id` | `uuid` | NOT NULL | FK → `identity.tenants("Id")` ON DELETE CASCADE |
| `role` | `text` | NOT NULL | CHECK in `('tenant_admin','tenant_member')` |
| `is_primary` | `bool` | NOT NULL | DEFAULT `false` |
| `row_version` | `bigint` | NOT NULL | Standard AggregateRoot concurrency token |
| `created_at` | `timestamptz` | NOT NULL | |
| `created_by` | `uuid` | NULL | |
| `updated_at` | `timestamptz` | NOT NULL | |
| `updated_by` | `uuid` | NULL | |
| `deleted_at` | `timestamptz` | NULL | |
| `deleted_by` | `uuid` | NULL | |

Indexes:
- `ux_tenant_memberships_user_tenant` — UNIQUE on `(user_id, tenant_id) WHERE deleted_at IS NULL`. Prevents the same user from being added twice to the same tenant; allows a soft-deleted membership to coexist with a fresh re-add.
- `ix_tenant_memberships_user` — non-unique on `(user_id) WHERE deleted_at IS NULL`. The middleware-enrichment path in OPS.M.2 reads memberships by `user_id` on every authenticated request — this is the hot path index.

FK behaviors locked:
- `user_id ON DELETE CASCADE` — when a user is hard-deleted, their memberships vanish. (Today the User aggregate is soft-deleted via `Deactivate`; hard delete is not implemented. The cascade is correctness-belt, not a code path we exercise yet.)
- `tenant_id ON DELETE CASCADE` — same rationale. The `tenants` row hard-delete path is also not implemented today; the cascade prevents orphaned memberships if it ever is.

Note the divergence from how the existing Slice3 forward-compat FKs use `ON DELETE RESTRICT` for `tenant_id`. Restrict is right for cross-schema FKs *from* module tables *into* `tenants` — those are the rows OPS.M.3 must not orphan. CASCADE is right *within* the identity schema between two co-managed tables.

`is_primary` is **not** UNIQUE-per-user in OPS.M.1 — enforcement (at most one `is_primary=true` per `user_id` where not deleted) is application-level for now; OPS.M.2 + the `TenantMembership.MakePrimary()` aggregate method handles it. A partial unique index (`WHERE is_primary AND deleted_at IS NULL`) could be added later if app-level enforcement proves leaky; deferred because every test today seeds one membership at a time and the constraint is overkill for the empty-table state OPS.M.1 ships.

### 3.3 Backfill row

One INSERT per §2.4. No backfill of memberships — at OPS.M.1 ship time, zero users have a membership row. OPS.M.2 introduces the first writes (a thin admin-seed command, or the persona-bootstrap script).

### 3.4 PK casing — `"Id"` is intentional

Every existing cross-schema FK that points at `identity.tenants` (right now just `booking.availability_blocks.tenant_id` from Slice 3) uses `REFERENCES identity.tenants ("Id")` with quoted PascalCase. This is because EF didn't set `HasColumnName("id")` on the Tenant aggregate's `Id` property — see `IdentityDbContextModelSnapshot.cs:149-151`. **OPS.M.1 keeps PascalCase for both `tenants."Id"` and `tenant_memberships."Id"`** to stay consistent with the existing FK in Slice 3 and avoid a coordinated migration. OPS.M.3 will use the same convention when adding `tenant_id` columns to module tables.

If reviewer wants snake_case across the board, that's a separate refactor that touches:
- The Slice 3 `availability_blocks` FK declaration.
- The `Tenant` + `User` + `AuditLogEntry` EF configurations.
- A coordinated migration that renames `"Id"` → `id` on three tables.

I argue against the refactor — it's mechanical cleanup with no operational win, and the OPS.M.3 rollout is more aggressive without it on the table.

---

## 4. Step-by-step plan (3 steps; one session each for #1 + #2)

### Step 1 — Extend `Tenant` aggregate + add `TenantMembership` aggregate (S, ~2h)

**File (edit)**: `src/Modules/VrBook.Modules.Identity/Domain/Tenant.cs`
- Add properties: `Status` (string), `DefaultCurrency` (string), `DefaultTimezone` (string), `SupportEmail` (Email VO), `PlatformFeeBps` (int), `StripeAccountId` (string?), `StripeAccountStatus` (string?), `SuspendedReason` (string?).
- Replace `Create(slug, displayName)` signature with `Create(slug, displayName, supportEmail, defaultCurrency = "EUR", defaultTimezone = "Europe/Dublin", platformFeeBps = 1500)`. Status defaults to `"PendingOnboarding"`. Raise a `TenantCreated` domain event.
- Add transition methods: `Activate()` (PendingOnboarding → Active; raises `TenantActivated`), `Suspend(string reason, Guid actorId)` (Active → Suspended; raises `TenantSuspended` from `MULTI_TENANCY_OPS_PLAN.md` §9), `Reactivate()` (Suspended → Active), `Close()` (any → Closed). Each method enforces the legal source state and throws `InvalidOperationException` otherwise.
- Add `SetStripeAccount(string accountId)` + `UpdateStripeAccountStatus(string status)` — Stripe wiring methods that OPS.M.5 will call. Land them now to lock the shape; they're trivial setters.
- Add `SetPlatformFeeBps(int bps)` with `0 ≤ bps ≤ 10_000` invariant (Super Admin override path; called from OPS.M.8).

**File (new)**: `src/Modules/VrBook.Modules.Identity/Domain/TenantMembership.cs`
- `AggregateRoot` subclass with `UserId, TenantId, Role, IsPrimary` properties.
- Static `Create(Guid userId, Guid tenantId, string role, bool isPrimary)` factory with CHECK on role value (must be `"tenant_admin"` or `"tenant_member"`).
- `MakePrimary()` method (sets `IsPrimary=true`).
- `ClearPrimary()` method (sets `IsPrimary=false`).
- `ChangeRole(string newRole)` method with same CHECK.
- `SoftDelete(Guid actorId)` (aliases `Deactivate`, exists on `AggregateRoot` pattern).
- Raise `TenantMembershipCreated`, `TenantMembershipRoleChanged`, `TenantMembershipRevoked` events.

**File (new)**: `src/VrBook.Contracts/Events/TenantEvents.cs` (or extend an existing file in that namespace if there's a tenant-events file already — there isn't from what I read).
- `record TenantCreated(Guid TenantId, string Slug, string DisplayName) : IDomainEvent;`
- `record TenantActivated(Guid TenantId) : IDomainEvent;`
- `record TenantSuspended(Guid TenantId, string Reason, Guid ActorId) : IDomainEvent;` — referenced in `MULTI_TENANCY_OPS_PLAN.md` §9.
- `record TenantMembershipCreated(Guid MembershipId, Guid UserId, Guid TenantId, string Role) : IDomainEvent;`
- `record TenantMembershipRoleChanged(Guid MembershipId, string OldRole, string NewRole) : IDomainEvent;`
- `record TenantMembershipRevoked(Guid MembershipId, Guid UserId, Guid TenantId) : IDomainEvent;`

**Tests (new)**: `tests/VrBook.Api.IntegrationTests/Domain/TenantAggregateTests.cs` + `TenantMembershipAggregateTests.cs` (mirror `AvailabilityBlockAggregateTests.cs` shape — `[Trait("Category","Unit")]`, FluentAssertions).

Test cases — Tenant:
- `Create` succeeds with valid args, raises `TenantCreated`, status is `"PendingOnboarding"`, defaults applied.
- `Create` rejects null/whitespace slug, displayName, supportEmail.
- `Create` rejects negative `platformFeeBps` (currently default param so test must construct explicitly).
- `Activate()` from `PendingOnboarding` succeeds, raises `TenantActivated`; from any other status throws.
- `Suspend(reason, actorId)` from `Active` succeeds, raises `TenantSuspended`, persists reason; from any other status throws.
- `Reactivate()` from `Suspended` succeeds; from any other throws.
- `SetPlatformFeeBps(bps)` rejects bps < 0 or > 10_000.

Test cases — TenantMembership:
- `Create` succeeds with `"tenant_admin"` and `"tenant_member"`; rejects any other value.
- `MakePrimary` / `ClearPrimary` toggle correctly.
- `ChangeRole` enforces CHECK list.

**Acceptance**: `dotnet test --filter Category=Unit` green. No new packages. Aggregate types compile against existing `AggregateRoot` + `IDomainEvent` contracts.

This step is **safe to ship before Step 2** — the new types are present but no migration exists yet, so EF's pending-model-changes detection will warn but won't fail. Step 2 closes the loop.

### Step 2 — EF configuration + migration + tenant seed (M, ~3h)

**File (edit)**: `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/TenantConfiguration.cs`
- Add property mappings for the eight new Tenant columns (per §3.1). `Status` as `text` with CHECK is declared via `b.ToTable(t => t.HasCheckConstraint("ck_tenants_status", "status IN ('PendingOnboarding','Active','Suspended','Closed')"))`. `SupportEmail` uses the same `HasConversion` pattern as `User.Email`. `PlatformFeeBps` `int NOT NULL`. Defaults match §3.1 for the seeded row (and as DB DEFAULTs so backfilled rows in OPS.M.3b don't need every column set).

**File (new)**: `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/TenantMembershipConfiguration.cs`
- ToTable `tenant_memberships` in schema `identity`.
- `HasKey(m => m.Id)` — `Id` left PascalCase per §3.4.
- `Property(m => m.UserId).HasColumnName("user_id")` + `Property(m => m.TenantId).HasColumnName("tenant_id")`.
- `Property(m => m.Role).HasColumnName("role").HasMaxLength(32).IsRequired()`. Add CHECK via `b.ToTable(t => t.HasCheckConstraint("ck_tenant_memberships_role", "role IN ('tenant_admin','tenant_member')"))`.
- `Property(m => m.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false)`.
- Audit columns (`created_at/by`, `updated_at/by`, `deleted_at/by`, `row_version`).
- `HasIndex(m => new { m.UserId, m.TenantId })` UNIQUE with `HasFilter("\"deleted_at\" IS NULL")` (or `deleted_at IS NULL` — verify EF emits the right syntax against Postgres).
- `HasIndex(m => m.UserId).HasFilter("\"deleted_at\" IS NULL").HasDatabaseName("ix_tenant_memberships_user")`.
- **No EF nav property** to `User` or `Tenant` — they're managed by the same DbContext and we *could* add navs, but consistency with the cross-module FK pattern (declared in SQL, no navs) is cleaner. The middleware will load memberships via `db.Set<TenantMembership>().Where(m => m.UserId == userId)` in OPS.M.2.

**File (edit)**: `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IdentityDbContext.cs`
- Add `public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();`. Configuration is auto-discovered via `ApplyConfigurationsFromAssembly`.

**Migration**: generate via `dotnet ef migrations add Slice5_Tenant_Membership_Schema --project src/Modules/VrBook.Modules.Identity --context IdentityDbContext`.

The generated migration must include:
1. `AlterColumn`/`AddColumn` for the eight new `tenants` columns.
2. The CHECK constraint on `tenants.status`.
3. `CreateTable` for `tenant_memberships` with all columns + PK.
4. Two FK constraints (`user_id` + `tenant_id`) **with ON DELETE CASCADE**.
5. Two indexes (`ux_tenant_memberships_user_tenant` filtered unique; `ix_tenant_memberships_user` filtered).
6. The CHECK constraint on `tenant_memberships.role`.
7. **Hand-edited block at the bottom**: the seed INSERT from §3.3, wrapped in an `ON CONFLICT DO NOTHING` so re-runs are idempotent. The `Down()` method removes the seeded row + all columns + the new table.

Migration name follows the existing pattern (`Slice4_DropEmailUnique`, `Slice3_TenantsPlaceholder`). I propose `Slice5_Tenant_Membership_Schema` because Slice 5 is the active phase context in `MASTER_PLAN.md` §1 — though OPS.M.1 itself is Phase 1.5. Reviewer may prefer `OpsM1_Tenant_Membership_Schema`. Mechanically equivalent; document the convention you pick in the commit message.

**Acceptance**:
- `dotnet ef migrations script <prev> Slice5_Tenant_Membership_Schema` produces SQL that runs cleanly on a fresh Postgres 16.
- `dotnet ef database update` against a staging DB clone applies clean (no data loss on existing `identity.tenants` placeholder rows — there are none today, but if any exist they get the defaults).
- `dotnet ef database update <prev>` rolls back clean (drops table, drops columns, drops seed row).
- A `git diff` against `IdentityDbContextModelSnapshot.cs` shows the expected diff (new TenantMembership entity, extended Tenant entity, no spurious churn on Users or AuditLog).

**This step is the OPS.M.1 deliverable.** The migration is the artifact that goes to staging. Everything after is verification + docs.

### Step 3 — Migration round-trip test + ADR-0014 + plan close-out (S, ~1h)

**File (new)**: `tests/VrBook.Api.IntegrationTests/Identity/TenantSchemaMigrationTests.cs`
- `[Collection(nameof(IdentityApiCollection))]` to share the Postgres container.
- One test: `Migration_creates_tenants_extended_columns_and_membership_table`. Boot the fixture (already applies migrations); query `information_schema.columns` for `identity.tenants` and assert the eight new column names + types exist; query `information_schema.tables` for `identity.tenant_memberships` and assert it exists with the expected columns; query the CHECK constraints from `information_schema.check_constraints` and assert the two CHECKs exist; query `identity.tenants` for the seeded `slug='default'` row and assert it exists with `display_name='VrBook Default'`.
- One test: `Membership_unique_index_blocks_duplicate_active_rows`. Insert one membership (user A, tenant Default, tenant_admin, is_primary=true). Insert a second membership with the same `(user_id, tenant_id)` — expect a Postgres unique-violation. Soft-delete the first (set `deleted_at = NOW()`) and re-insert — expect success (proving the partial-index `WHERE deleted_at IS NULL` is correct).
- One test: `Role_check_constraint_rejects_unknown_role`. Insert a membership with `role='hacker'` — expect a CHECK violation.

These three tests are the regression net. They run in the existing Postgres-backed integration test pack. They do not require any controller, MediatR pipeline, or middleware change — they're pure schema assertions. Total runtime ~2s once the Postgres container is warm.

**File (new)**: `docs/adr/0014-app-roles-global-db-per-tenant.md`
- Standard ADR template (`docs/adr/0013-single-tenant-staging-and-prod.md` is the latest neighbor — mirror its shape: Status, Date, Context, Decision, Consequences, Alternatives considered, References).
- Status: Accepted, Date: 2026-06-26.
- Context: the OPS.M.0 close-out's empirical finding that Entra extension claims don't emit reliably in External-tenant CIAM access tokens; the schema being introduced in OPS.M.1 as the per-tenant role substrate.
- Decision: per `docs/identity/roles-architecture.md` §1 — App Roles carry `Owner`/`Admin`; `tenant_memberships.role` carries per-tenant scope. Token never carries tenant id; resolution is request-time via DB lookup.
- Consequences: per `docs/identity/roles-architecture.md` §2 (the constraint-satisfaction table).
- Alternatives considered: extension claims (rejected on emission empirically); Custom Authentication Extension webhook (rejected on deploy surface); DB-backed everything (rejected for global Owner/Admin where App Roles is the platform-native fit).
- References: `roles-architecture.md`, `MULTI_TENANCY_OPS_PLAN.md`, ADR-0012.

**Files (edit)** — small footer notes:
- `docs/MULTI_TENANCY_OPS_PLAN.md` — append a one-line note to §10 row OPS.M.1: "Shipped <commit-hash> 2026-06-XX. See `docs/OPS_M_1_PLAN.md`."
- `docs/MASTER_PLAN.md` §1 — flip the OPS.M.1 row to ✅ with commit range + verified.
- `docs/identity/roles-architecture.md` — strike the "(to be written alongside this doc)" note next to ADR-0014; replace with link.

**Acceptance**: ADR-0014 lints; cross-links resolve; `dotnet test --filter "FullyQualifiedName~TenantSchemaMigration"` green.

---

## 5. Migration concerns (staging)

Staging today has data in `users`, `properties`, and the Slice 3 tables (`availability_blocks` with nullable `tenant_id`). The OPS.M.1 migration's blast radius on existing data:

1. **`identity.tenants` extended columns** — the placeholder table is empty in staging today (the Slice 3 migration created it but no row was ever inserted). Adding NOT NULL columns to an empty table is trivial. The seeded row in §3.3 fills the table with one row that has every column set. **No data-loss risk.**
2. **`identity.tenant_memberships`** — new table; empty at ship time. Nothing to migrate.
3. **`users` table** — untouched. No new column, no new constraint. Existing rows unaffected.
4. **`availability_blocks.tenant_id`** — already exists, nullable, FK to `identity.tenants("Id")` with `ON DELETE RESTRICT`. The seeded "default" tenant becomes the row OPS.M.3b will set this column to. The seed in OPS.M.1 means OPS.M.3b is a UPDATE-with-known-constant rather than a "find or insert" with race conditions.
5. **Rollback** — `Down()` drops the seed row, drops the new columns, drops the membership table. Reversible. The only soft hazard: if anyone has manually inserted a `tenants` row between deploy and rollback, the NOT NULL columns being dropped is fine, but seed-row deletion would fail if a downstream module FK now references it. None do today.

**Sharp edge (staging only)**: if OPS.M.1 ships before OPS.M.2's middleware enrichment lands, the `default_currency`/`default_timezone` columns are unused by app code. No risk; they're just dormant. The reverse — OPS.M.2 enrichment shipping before OPS.M.1 schema — would fail at startup. **OPS.M.1 ships first, full stop**, which is what `MASTER_PLAN.md` §2's ordering already says.

**Prod**: prod cutover is gated by `entra-prod-cutover-prerequisites.md`. OPS.M.1's migration runs whenever prod cutover proceeds; it has zero dependency on Entra readiness. The migration is safe to deploy ahead of prod cutover if the deploy pipeline ever gets there before OPS.M.0's prod gap closes.

---

## 6. Test strategy summary

| Layer | What's covered | Where |
|---|---|---|
| Domain unit | Tenant aggregate invariants (status transitions, fee bps bounds, factory args) | `tests/VrBook.Api.IntegrationTests/Domain/TenantAggregateTests.cs` |
| Domain unit | TenantMembership aggregate invariants (role CHECK, primary toggling) | `tests/VrBook.Api.IntegrationTests/Domain/TenantMembershipAggregateTests.cs` |
| Migration round-trip | Schema applies; columns + indexes + CHECKs exist; seed row exists | `tests/VrBook.Api.IntegrationTests/Identity/TenantSchemaMigrationTests.cs` |
| Migration round-trip | Unique partial index works (soft-delete-aware) | same file |
| Migration round-trip | CHECK constraint rejects bad role | same file |

**What is explicitly NOT tested in OPS.M.1**:
- Handler integration tests for any tenant CRUD — there are no handlers yet (deferred to OPS.M.2 + OPS.M.8).
- Authorization integration tests for `tenant_admin` claim resolution — no middleware change yet (OPS.M.2).
- End-to-end two-tenant isolation — `MULTI_TENANCY_OPS_PLAN.md` §10 OPS.M.10's `MultiTenancyIsolationTests.cs` is the home for that, not OPS.M.1.

CI runs the existing `Category=Unit` + the new schema tests under the existing `IdentityApiCollection`. No new pipeline configuration.

---

## 7. Non-goals (explicitly OPS.M.2 / M.3 / M.4 work)

| Item | Owner slice | Why deferred |
|---|---|---|
| `UserProvisioningMiddleware` enrichment — load memberships, stamp `ClaimTypes.Role`+`app_tenant_id` | OPS.M.2 | Requires `tenant_memberships` to exist (OPS.M.1 deliverable) — sequence not coupling |
| `ICurrentUser.TenantId` property + `HasTenantRole(tenantId, role)` method | OPS.M.2 | Same as above |
| `tenant_id` column on Catalog / Booking / Sync / Payment / Pricing / Messaging / Reviews / Notifications tables | OPS.M.3a | Bulk parallel work; isolated from OPS.M.1 |
| Backfill `tenant_id = 'default'` on existing module rows | OPS.M.3b | Per `MULTI_TENANCY_OPS_PLAN.md` §10; requires app code to be ready to read it |
| Tighten `tenant_id` to NOT NULL | OPS.M.3c | Gated on OPS.M.3b backfill completion |
| `TenantAuthorizationBehavior` MediatR pipeline behavior | OPS.M.4 | Requires `ITenantScoped` marker + per-module aggregate `TenantId` properties |
| Drop `[Authorize(Roles="Owner,Admin")]` on controllers in favor of behavior | OPS.M.4 | Same |
| Stripe Connect Express integration (PaymentIntent `transfer_data`, AccountLink, webhook routing) | OPS.M.5 | `tenants.stripe_account_id` column ships in OPS.M.1, but it's unread |
| iCal poller `tenant_id` filter | OPS.M.6 | Requires `channel_feeds.tenant_id` from OPS.M.3 |
| Tenant onboarding wizard UI | OPS.M.7 | Frontend slice; depends on OPS.M.5 |
| Super Admin console | OPS.M.8 | Depends on OPS.M.4 |
| RLS policies + `app.tenant_id` per-connection setting | OPS.M.9 | Defense-in-depth; explicitly after app-level checks proven in OPS.M.4 |
| Cross-tenant isolation test pack (`MultiTenancyIsolationTests.cs`) | OPS.M.10 | Requires OPS.M.4 + OPS.M.5 |
| Rename `Owner` App Role → `tenant_admin` everywhere | OPS.M.4 | See §2.1 — bundled with the controller-attribute rewrite |
| `users.tenant_id` column | Never | Memberships table replaces it; see §2.3 |
| `infra/scripts/grant-self-admin.ps1` rewrite or retire | OPS.M.4 or never | The App Role bootstrap in `entra-external-id-setup.md` §7.2 replaced it for staging; the script can stay inert |

---

## 8. Out of scope (future phases)

Per `MULTI_TENANCY_OPS_PLAN.md` §11 — all of these stay out of OPS.M.1 (and out of Phase 1.5 entirely):
- Self-serve tenant sign-up.
- Per-tenant Entra tenants.
- Tenant subdomain routing.
- Per-tenant ACS resources / DKIM.
- AirBnB Channel Manager API push.
- `tenant_member` UI surface (schema supports it; UI is Phase 2 per `MASTER_PLAN.md` §6).

---

## 9. Scope-cut order (drop top first if 2-day budget bites)

1. **`TenantMembershipRoleChanged` / `TenantMembershipRevoked` events.** Trim to just `TenantMembershipCreated`. OPS.M.8's audit log path needs the events but it's a one-line add when it lands; not a Phase-1.5 critical path.
2. **`SetPlatformFeeBps` validation method.** Ship the column; defer the aggregate method. OPS.M.8 adds it when the Super Admin console needs it.
3. **`stripe_account_id` + `stripe_account_status` + `suspended_reason` columns.** Drop from OPS.M.1; add in OPS.M.5 when Stripe integration actually needs them. Saves three column additions + three new aggregate setters. **Not recommended** — they're table-shape decisions that benefit from being in the same migration as the Tenant aggregate broadening (one migration per logical schema unit).
4. **The migration round-trip test (Step 3).** Replace with a manual smoke test (apply migration, query schema, eyeball). Saves ~45 min. **Not recommended** — this test is the foundation for OPS.M.3's much bigger migration roundtrip suite.
5. **ADR-0014.** Defer to OPS.M.2. The plan still ships; the ADR is a documentation artifact and is reviewer-blockable on its own thread.

Never falls: Step 1 (the Tenant + TenantMembership aggregates) and Step 2 (the migration). Without those there is no OPS.M.1.

---

## 10. Open questions for reviewer

1. **Migration name**: `Slice5_Tenant_Membership_Schema` (aligns with current dev-phase context) vs `OpsM1_Tenant_Membership_Schema` (aligns with OPS.M lineage). Pick one as the convention going forward — OPS.M.3 will produce one migration per module, and the convention is going to repeat a dozen times.
2. **`tenants.default_timezone` initial value**: **Decided 2026-06-26** — `'UTC'`. Per-property timezones from location data are the right answer; the tenant column is a fallback only. App is USA-focused (originally proposed `Europe/Dublin` which was wrong). Same flow for `default_currency` — settled on `'USD'`, not `'EUR'`.
3. **`tenants.support_email` initial value for the seed row**: `'support@vrbook.example.com'` is a placeholder. Should the seed value be your real support inbox so OPS.M.5+ bounce alerts go somewhere live? Email is configurable post-seed via `Tenant.UpdateSupportEmail()` (which I should also add to the aggregate — currently the plan doesn't include it; flag in review).
4. **`is_primary` enforcement**: app-level only in OPS.M.1, or do we ship the partial unique index (`WHERE is_primary AND deleted_at IS NULL`) now? I lean app-level. Reviewer call.
5. **Confirm Step 1 commit can ship before Step 2** even though the migration isn't there yet (EF pending-changes warning vs hard fail). I believe warning-only on `dotnet ef migrations has-pending-model-changes`. Worth a 30s check before committing the split.
6. **PK casing on `tenant_memberships."Id"`** — see §3.4. PascalCase mirrors the existing `tenants."Id"`. Snake_case requires the broader refactor flagged. Confirm we stay with PascalCase.

---

## 11. What gets approved by this document

If you approve:
1. The plan is committed as `docs/OPS_M_1_PLAN.md`.
2. Step 1 ships as a single commit (`OPS.M.1 — Tenant aggregate broadening + TenantMembership`).
3. Step 2 ships as a single commit (`OPS.M.1 — EF schema + Slice5 migration + default tenant seed`). Migration is generated, hand-edited for the seed, run against a staging DB clone before merge.
4. Step 3 ships as a single commit (`OPS.M.1 — ADR-0014 + migration round-trip tests + plan close-out`).
5. After merge: staging deploy applies the migration. Verification = `SELECT slug, display_name, status FROM identity.tenants;` returns the default row.

If you reject or want changes: point at the specific §2 decision or the specific Step in §4; I revise this document and re-submit.
