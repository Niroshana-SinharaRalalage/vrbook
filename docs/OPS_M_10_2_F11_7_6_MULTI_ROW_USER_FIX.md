# Slice OPS.M.10.2 F11.7.6 — Multi-row-per-email systemic fix

> **Author**: system-architect consult, 2026-06-30
> **Status**: Locked — ready to execute
> **Predecessor**: F11.7.5.10 (5acb5bb) — interim bootstrap-operator loops the promotion across every email-match; unblocked walk-2 but does NOT prevent future divergence.
> **Successor**: none (this closes the multi-row hazard class before F11.8).
> **Slice budget**: 6 commits, all CI-gated (one is doc-only close-out, no CI wait per `feedback_no_ci_for_doc_only_commits`).

---

## 1. Executive summary

### 1.1 Root cause (agree with the walk-2 read)

Agree exactly. `identity.users.b2c_object_id` is UNIQUE (`Init_IdentitySchema` line 84). `identity.users.email` is a NON-UNIQUE index since `Slice4_DropEmailUnique` (2026-06-22) — dropped so DevAuth personas could share `niroshanaks@gmail.com` during Slice-4 staging verification. `ProvisionUserHandler` (`ProvisionUserHandler.cs:14`) keys ONLY on B2CObjectId — every distinct `oid` creates a new row. The DevAuth-then-Entra sequence produces two rows for one human. `UserProvisioningMiddleware.cs:73-76` reads `is_platform_admin` from the row the CURRENT session's oid maps to — the fresh Entra row, which was never bootstrapped. PA bypass never fires. 403.

`SetPersonaEmail` (`SetPersonaEmailCommand.cs:52`) is a second divergence source: it writes an arbitrary email onto ANY dev-oid row, which is how `dev-guest-00000001@vrbook.local` originally became `niroshanaks@gmail.com` on the guest row while the owner row already held it. That row still exists in staging today.

### 1.2 Pick: **Candidate B (enforce uniqueness on email, backfill, upsert on collision) — with a `Trim + lower(email)` case-insensitive shape and a hard 3-step migration.**

**Why B:**
1. The code already assumes it (bootstrap-operator's inline comment at `IdentityController.cs:302-311` names the multi-row shape as the hazard, and the F11.7.5.10 fix is explicitly a bandaid for it).
2. B2CObjectId being stable is a myth in this codebase — DevAuth stubs and real Entra both write to the SAME table, so a stable identity across auth paths is impossible without a normalized secondary key. Email IS the human-stable identifier per ADR-0012 (Entra External ID uses email-as-username).
3. ADR-0014 (`is_platform_admin` DB-wins, load-bearing) survives untouched IF one row per human is enforced. B and only B makes ADR-0014's "DB is the source of truth" statement operationally true.
4. Downstream consumer of `guest_user_id` in `booking.bookings` (`BookingConfiguration.cs:23`) has NO cross-schema FK constraint (verified: it's `HasIndex`, not `HasOne<User>`). Merging duplicate user rows re-points application-level references but breaks NO FK invariants. Same for messaging/reviews/notifications — all reference the id but none constrain it.

**Why not A (upsert-by-oid ∪ email):** A mutates the stable identifier (`b2c_object_id`) on collision. Any auditor reading `identity.audit_log` where `target_id = <old oid>` would find nothing that matches now. And role-address collisions (two humans, same shared inbox) become a landmine as the user noted — silent identity confusion, not a loud error.

**Why not C (bypass reads any-row-with-email):** Widens the PlatformAdmin surface to "any row with a matching email" — a DevAuth SetPersonaEmail write becomes a privilege-escalation surface. Also does not fix the tenant claim: `UserProvisioningMiddleware` still picks the per-oid row for memberships, so a bootstrapped owner would still hit "no tenant" if the current session's row has no membership.

**Why not D (something else):** Considered a shadow `identity.user_email_bindings(email PK, user_id FK)` reverse-index. Rejected: adds a second write path for every provisioning; race condition on concurrent first-login-with-two-oids; still requires the merge migration to seed. B does that work once, at migration time, and leaves normal operation with a single write path.

### 1.3 Residual risks (after this slice)

1. **Real production duplicates on cutover to Entra** — if a real user changes their Entra email address, they may temporarily hold two rows during their next sign-in. Mitigation: `ProvisionUserHandler` upsert on email-hit updates the row's B2CObjectId in-place (the ONE mutation of a "stable" identifier we accept), and raises `UserB2CObjectIdRebound` for the audit trail. See §3.1.
2. **Case-sensitivity in email address** — RFC 5321 makes local-part case-sensitive; virtually all mail systems treat it insensitive. We normalize to `lower(email)` for the unique index (partial index on the expression) but keep the stored value as-typed for display. See §3.3.
3. **DevAuth persona-email overlap** — after this slice, `SetPersonaEmail` on a persona whose target email already belongs to another row throws a `DuplicateEmailException` (409). This is intended: the staging shared-inbox trick that broke us was an anti-pattern; the operator should reset the OTHER row's email first, or Deactivate it. Runbook update in §5.
4. **ADR-0014 `is_platform_admin` DB-wins precedence** — unchanged. The bit remains on `identity.users`; there is now exactly one row per human, so "DB-wins" is now decidable. §7 arch tests lock this.

---

## 2. F11.7.6 commit sequence

All commits push to `develop`, then `gh run watch` until conclusion=success on `cd-staging-api.yml` (except doc-only F11.7.6.6). Local pre-push test filter is `dotnet test --filter "Category!=Integration"` per `feedback_use_ci_filter_locally`.

### F11.7.6.1 — arch tests + integration tests that DEMONSTRATE the bug (red)

**Scope**: TDD gate. Land the failing tests first so we can see them go green.

**Files (all NEW):**
- `tests/VrBook.Architecture.Tests/UsersTableEmailUniquenessTests.cs` — Roslyn syntax assertion: `UserConfiguration.cs` MUST call `.HasIndex(u => u.Email).IsUnique()` OR `.HasIndex(...).HasFilter("... IS NULL").IsUnique()` (partial-unique acceptable if the filter carve-out is `deleted_at IS NULL`). Absence of `.IsUnique()` is a fail. Also asserts `ProvisionUserHandler` references BOTH `GetByB2CObjectIdAsync` AND `GetByEmailAsync` (new repo method — see F11.7.6.3).
- `tests/VrBook.Api.IntegrationTests/Identity/ProvisionUserEmailCollisionTests.cs` — three scenarios (all fail today):
  - **Scenario A — new oid, existing email**: seed row `(oid=X, email=e@x)`. `ProvisionUserCommand(oid=Y, email=e@x, ...)` returns `existing.Id` (row X's id, not a new row), row X's B2CObjectId is now `Y`, audit log has `UserB2CObjectIdRebound`, no second row exists.
  - **Scenario B — new oid, new email**: `ProvisionUserCommand(oid=Z, email=new@x, ...)` creates a fresh row (regression net; this is today's normal case).
  - **Scenario C — same oid**: repeat call is idempotent (regression net for `RefreshFromLogin`).
- `tests/VrBook.Api.IntegrationTests/Identity/UserRowMergeMigrationTests.cs` — reversibility + data safety net for the migration in F11.7.6.4. Seeds a pre-migration state (2 rows, same email, different oids, one PA=true and one PA=false; one has a membership, other doesn't; one has `guest_user_id` bookings, other has an audit_log target_id) into a scratch DB, runs the migration, asserts: exactly one row remains, its `is_platform_admin=true` (highest-privilege wins), its id is the `oldest CreatedAt` row's id, membership is preserved on the surviving id, both bookings' `guest_user_id` still resolves to the surviving id, audit_log's `target_id` value is untouched (audit is append-only historical record — target_id is a string, not an FK; see §3.5).

**Local validation**: `dotnet test --filter "Category!=Integration"` — arch test fails (expected).
`dotnet test --filter "FullyQualifiedName~ProvisionUserEmailCollision"` — integration fails (expected).

**CI expectation**: **red** on `cd-staging-api.yml`. This is the TDD signal. Push with `[WIP]` prefix in the commit message so the CI red doesn't get confused with a regression.

**Rollback**: revert commit; no schema touched.

---

### F11.7.6.2 — domain: `User.RebindB2CObjectId(newOid, actorId)` + `UserB2CObjectIdRebound` event

**Scope**: give the aggregate the vocabulary to say "an existing row is being adopted by a new Entra oid" WITHOUT re-provisioning. Keep the aggregate the single source of truth for identity mutation.

**Files:**
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs`:
  - Add `public void RebindB2CObjectId(string newB2CObjectId, Guid actorId)` — validates non-empty; no-ops if unchanged; else updates the field and raises `UserB2CObjectIdRebound(Id, oldOid, newOid, actorId)`. Mirrors the shape of `GrantPlatformAdmin` (idempotent + event).
- `src/VrBook.Contracts/Events/IdentityEvents.cs` (or wherever `UserPlatformAdminGranted` lives — grep first) — add `public sealed record UserB2CObjectIdRebound(Guid UserId, string OldB2CObjectId, string NewB2CObjectId, Guid ActorId)`.

**Tests added:**
- `tests/VrBook.Domain.Tests/Identity/UserRebindTests.cs` (extend if exists) — three tests: idempotent on same oid, raises event on change, rejects empty/whitespace.

**Local validation**: `dotnet test --filter "Category!=Integration"` — new domain tests pass; arch + integration tests from F11.7.6.1 still red.

**CI expectation**: red (F11.7.6.1's tests still fail, correctly). Push with `[WIP]`.

**Rollback**: revert commit. Pure additive domain change.

---

### F11.7.6.3 — application: `IUserRepository.GetByEmailAsync` + `ProvisionUserHandler` upsert on email-hit

**Scope**: land the actual bug fix in application code. Behavior:
1. Look up by B2CObjectId → hit → refresh + save (unchanged from today).
2. Miss → look up by email (`lower(email) = lower(cmd.Email)`, `deleted_at IS NULL`).
3. Email hit → call `existing.RebindB2CObjectId(cmd.B2CObjectId, actorId: existing.Id)` + `RefreshFromLogin` + grant flags + save. Return `existing.Id`.
4. Email miss → today's fresh `User.Provision` path (unchanged).

**Files:**
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IUserRepository.cs` — add `Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)`.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserRepository.cs` — implement with `db.Users.FirstOrDefaultAsync(u => EF.Functions.ILike(((string)(object)u.Email), email))`. Match the existing `BuildQ` case-insensitive shape (`UserRepository.cs:39`).
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs` — the three-branch handler above. Log at Information on email-hit-rebind so operators see when a rebind occurs (rare event).

**Tests added:**
- The three integration scenarios from F11.7.6.1 go GREEN with this commit.
- `tests/VrBook.Api.IntegrationTests/Identity/ProvisionUserEmailCollisionTests.cs` — extend with two more scenarios:
  - Case-insensitive: seed `Niroshanaks@Gmail.com`, provision `niroshanaks@gmail.com` — treated as same row.
  - Soft-deleted collision: seed row deleted; provision with same email — DOES create fresh row (soft-deleted row is skipped by the `deleted_at IS NULL` filter). Prevents an admin's deactivation from becoming a permanent email-blocker.

**Local validation**: `dotnet test --filter "Category!=Integration"` — arch test still red (still no `.IsUnique()`); integration tests are green.

**CI expectation**: red (arch test still fails). Push with `[WIP]`. Do NOT strip WIP until F11.7.6.4 is in and the migration ran.

**Rollback**: revert commit. No schema touched.

---

### F11.7.6.4 — migration: merge duplicates + add partial-unique index on `lower(email)`

**Scope**: THE MIGRATION. This is the load-bearing step. See §3 for the full plan.

**Files:**
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/<timestamp>_F11_7_6_UsersEmailUnique.cs` — three-step Up:
  1. Merge duplicates via a raw SQL block (§3.2). Emits one row into `identity.audit_log` per merged pair for traceability.
  2. Add partial-unique index: `CREATE UNIQUE INDEX ux_users_email_active ON identity.users (lower(email)) WHERE deleted_at IS NULL`.
  3. Drop the non-unique `IX_users_email` (from `Slice4_DropEmailUnique`) — the new partial-unique covers the same lookup use cases.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs`:
  - Replace `b.HasIndex(u => u.Email)` with `b.HasIndex(u => u.Email).IsUnique().HasFilter("deleted_at IS NULL AND lower(email) IS NOT NULL").HasDatabaseName("ux_users_email_active")`.
  - **Note**: EF Core cannot express `lower(email)` in `HasIndex` directly. The runtime uniqueness is enforced by the raw migration SQL; the model snapshot carries a "close-enough" HasIndex so future migrations don't churn it. Add a comment marking the model↔DB drift as intentional.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/IdentityDbContextModelSnapshot.cs` — regenerate via `dotnet ef migrations add`.

**Tests added:**
- `tests/VrBook.Architecture.Tests/UsersTableEmailUniquenessTests.cs` — goes GREEN (the arch test asserts `.IsUnique()` on the Email HasIndex).
- `tests/VrBook.Api.IntegrationTests/Identity/UserRowMergeMigrationTests.cs` — goes GREEN (this test was authored in F11.7.6.1 against the expected post-migration shape).
- `tests/VrBook.Api.IntegrationTests/Identity/EmailUniquenessEnforcementTests.cs` (NEW) — direct SQL insert of a second row with the same email throws `PostgresException` (23505 unique violation). Locks the DB-level invariant.

**Local validation**:
```sh
# Regenerate the migration:
cd src/Modules/VrBook.Modules.Identity
dotnet ef migrations add F11_7_6_UsersEmailUnique --context IdentityDbContext -o Infrastructure/Persistence/Migrations
# Edit the migration to hand-write the merge SQL (§3.2).
dotnet build
dotnet test --filter "Category!=Integration"
# All arch tests pass. Migration test needs Category=Integration.
dotnet test --filter "FullyQualifiedName~UserRowMergeMigration"
```

**CI expectation**: **green** on `cd-staging-api.yml`. Strip `[WIP]` from the commit message. The staging DB migration runs automatically on deploy; the merge SQL is idempotent (see §3.2 no-op guard).

**Rollback**:
- Migration `Down`:
  1. Drop the partial-unique index.
  2. Recreate `IX_users_email` (non-unique).
  3. **Do NOT attempt to un-merge rows.** The merge is lossy by design (that's the point). If the migration must be reverted AFTER duplicate-recreation would be needed, the operator must restore from the pre-migration snapshot. Document this in the migration's XML docs.
- Emergency out: revert the code commit (Down migration runs), then restore the staging DB from the pre-migration snapshot. Snapshot is `staging-vrbook-preF11_7_6-YYYYMMDD.dump` — must be captured by ops BEFORE deploying this commit. See §3.6 pre-deploy checklist.

---

### F11.7.6.5 — DevAuth guard: `SetPersonaEmail` refuses duplicates

**Scope**: post-migration, the DevAuth SetPersonaEmail write can now HIT the unique constraint and throw a raw `PostgresException`. Convert to a friendly `DuplicateEmailException` (409) with an actionable message. Also block the trivial self-shadow case (setting `dev-guest-...@vrbook.local` on the guest persona to the owner's real inbox when the owner row exists).

**Files:**
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs`:
  - Before calling `user.SetEmail(...)`, `await db.Users.FirstOrDefaultAsync(u => EF.Functions.ILike(...) && u.Id != user.Id && u.DeletedAt == null)`. If found → `throw new DuplicateEmailException(newEmail, otherUserId)`.
- `src/VrBook.Contracts/Exceptions/DuplicateEmailException.cs` (NEW) — `sealed class DuplicateEmailException(string email, Guid conflictingUserId) : DomainException`. Mapped to 409 by the global `ProblemDetails` writer (verify the mapping table has an entry; add one if missing).
- `src/VrBook.Api/Controllers/IdentityController.cs`:
  - `SetPersonaEmail` action: catch `DuplicateEmailException` → return `Conflict(new { detail, conflictingUserId })` for a cleaner API surface (optional; the global mapper handles it if omitted, but the explicit conversion gives us the id in the payload for the operator UI).

**Tests added:**
- `tests/VrBook.Api.IntegrationTests/Identity/SetPersonaEmailDuplicateGuardTests.cs` (NEW) — three scenarios:
  - Set persona email to an inbox that is UNIQUE → 200.
  - Set persona email to an inbox that ALREADY BELONGS to another user → 409, payload includes `conflictingUserId`.
  - Set persona email to the SAME persona's current email → 200 no-op.

**Local validation**: `dotnet test --filter "Category!=Integration"` — arch tests all green. Then run the integration subset.

**CI expectation**: **green** on `cd-staging-api.yml`.

**Rollback**: revert commit. DB constraint is still enforced (that's F11.7.6.4's job); SetPersonaEmail would just throw a raw 500 on collision — ugly but not unsafe.

---

### F11.7.6.6 — close-out: this doc gets §11

**Scope**: §11 close-out per slice-completion policy. Document the verification walk results.

**Files:**
- `docs/OPS_M_10_2_F11_7_6_MULTI_ROW_USER_FIX.md` (this file) — append §11 with:
  - Verification commands (curl to force a rebind scenario; SQL count of pre/post rows for `niroshanaks@gmail.com` on staging).
  - The audit-log rebind rows written (count).
  - Interim from F11.7.5.10 status: keep the loop-across-emails code, mark it as `[Obsolete("kept as defense-in-depth; one row per email is now the invariant")]` OR remove it since the invariant makes it unreachable. **Decision deferred to the close-out; both are defensible.** Note the choice made in §11.
- No memory file edits.

**This is a doc-only commit. Per `feedback_no_ci_for_doc_only_commits`: do NOT `gh run watch`. The previous code-commit (F11.7.6.5) is the binding CI gate.**

---

## 3. Backfill-migration plan (Candidate B)

### 3.1 Identifying duplicates

- **Comparison shape**: `lower(trim(email))`. Trim guards against a stray trailing space in the Entra token's `emails` claim; lower guards against display-caps variance.
- **Scope**: only rows where `deleted_at IS NULL` count as "duplicate for merge purposes." Soft-deleted rows keep their historical email verbatim; the unique index's partial filter (`WHERE deleted_at IS NULL`) makes them invisible to future insertion checks.

SQL to identify:

```sql
WITH normalized AS (
  SELECT "Id", lower(trim(email)) AS n_email, b2c_object_id,
         is_platform_admin, created_at,
         (SELECT COUNT(*) FROM identity.tenant_memberships m WHERE m.user_id = u."Id" AND m.deleted_at IS NULL) AS membership_count
    FROM identity.users u
   WHERE deleted_at IS NULL
),
dupes AS (
  SELECT n_email FROM normalized GROUP BY n_email HAVING COUNT(*) > 1
)
SELECT n.* FROM normalized n JOIN dupes d ON n.n_email = d.n_email
 ORDER BY n.n_email, n.created_at;
```

Every group of ≥ 2 rows is a merge cluster.

### 3.2 Winner-selection rule (per cluster)

**Precedence** (first satisfied wins; tie → next tier):
1. `is_platform_admin = true` beats `is_platform_admin = false`.
2. **Higher active `tenant_memberships` count** wins (a row wired into the tenant graph is more expensive to reroute than a bare row).
3. **Oldest `created_at`** wins (the row the audit trail refers to for longest; minimizes audit_log target_id staleness).
4. **Lexicographically smallest `"Id"`** as a final deterministic tiebreak (so the SQL is idempotent — re-running the merge on an already-merged state is a no-op).

Rejected alternatives:
- "Newest oid wins" — favors churn; the just-created Entra row has done less than the DevAuth row it collided with. Rejected.
- "Row with the most bookings" — requires cross-schema join to `booking.bookings.guest_user_id`; adds a cross-module coupling in the migration. Rejected: booking count is highly correlated with membership count for owners; membership_count is a cheaper local signal.

### 3.3 Losers → surviving id: FK repointing

Cross-schema references to `identity.users."Id"` — none of them are Postgres FK constraints (verified by grep), so the merge is a plain UPDATE, not a `ON DELETE CASCADE` cascade. **Confirmed FK-lite references to re-point:**

| Table (schema.table) | Column | Constraint? | Action |
|---|---|---|---|
| `identity.tenant_memberships` | `user_id` | **Yes** (`HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(Cascade)`) | Repoint to surviving `"Id"`; then dedupe `(user_id, tenant_id)` pairs (partial-unique index `ux_tenant_memberships_user_tenant` fires here — prefer the row with matching role or `IsPrimary=true`, soft-delete the other). |
| `identity.audit_log` | `actor_user_id`, `target_id` (string) | No FK | Repoint `actor_user_id`; leave `target_id` (string, historical) UNTOUCHED. Audit is append-only historical record. Rebind event covers the story. |
| `booking.bookings` | `guest_user_id` | No FK (cross-schema; verified: `HasIndex(x => x.GuestUserId)` only) | Repoint. |
| `messaging.threads` | `guest_user_id` (grep to confirm exact column) | No FK | Repoint. |
| `reviews.reviews` | `guest_user_id` | No FK | Repoint. |
| `loyalty.accounts` | `user_id` | No FK | Repoint. |
| `notifications.*.recipient_user_id` | (varies) | No FK | Repoint. |

The migration hand-writes the UPDATEs for each of these tables. Per-slice architects have kept module DbContexts separate, so a single migration crossing schemas is unusual but justified here — an alternative "one migration per module" fan-out is REJECTED because it opens a partial-merge window where `booking.guest_user_id` is repointed but `messaging.guest_user_id` isn't yet, breaking joins that thread across those.

**All cross-schema UPDATEs land in `identity`'s migration** (schemas share the DB; the Identity migration can `UPDATE booking.bookings SET guest_user_id = <surviving> WHERE guest_user_id = <loser>`). Document this cross-schema write in the migration's XML doc; add an entry to `RlsBypassCallSiteAllowlistTests` if the migration runs under an RLS context (verify — likely not, since migrations run pre-app-startup, but check).

**Loser rows**: after repointing, HARD DELETE the loser rows (not soft-delete) — the row's identity is what we're eliminating, not a historical fact worth preserving. The audit_log rebind event (§3.4) is where the history lives.

### 3.4 Merge audit trail

For every merge cluster, the migration writes ONE audit_log row per loser:

```sql
INSERT INTO identity.audit_log
  (Id, occurred_at, actor_user_id, actor_role, action, target_type, target_id, before, after)
VALUES
  (gen_random_uuid(), NOW(), NULL, 'system',
   'user.merge-duplicate-email',
   'User',
   <surviving_id>,
   jsonb_build_object('loser_id', <loser_id>, 'loser_oid', <loser_oid>, 'loser_email', <loser_email>),
   jsonb_build_object('surviving_id', <surviving_id>, 'surviving_oid', <surviving_oid>));
```

`actor_user_id` is NULL because the migration runs as `system`. Grepped: `identity.audit_log.actor_user_id` is nullable — verified in `Init_IdentitySchema.cs:24`.

### 3.5 Case-sensitivity + normalization

- Stored value in `email` column stays as-typed (RFC display).
- Uniqueness enforced on `lower(email)` via a Postgres expression index: `CREATE UNIQUE INDEX ux_users_email_active ON identity.users (lower(email)) WHERE deleted_at IS NULL`.
- `ProvisionUserHandler` looks up via `EF.Functions.ILike` (already the pattern in `UserRepository.BuildQ`).
- Trim happens at write time in the handler (`cmd.Email.Trim()` before `new Email(...)`), not in the index — trim is a normalization the app owns.

### 3.6 Reversibility

**Migration Up is NOT losslessly reversible** — the merge deletes rows. Down does:
1. Drop the unique index.
2. Recreate the non-unique `IX_users_email`.

Down does NOT recreate the deleted rows. **This is called out in the migration's XML docs.** Rollback path if the Up needs to be undone with data recovery: restore the DB from the pre-migration snapshot named `staging-vrbook-preF11_7_6-YYYYMMDD.dump`.

**Pre-deploy checklist for F11.7.6.4** (operator, one-time):
1. Take a DB snapshot of staging: `az postgres flexible-server backup create --name staging-vrbook --backup-name preF11_7_6-YYYYMMDD`.
2. Verify the snapshot appears: `az postgres flexible-server backup list --name staging-vrbook`.
3. Confirm staging traffic is low (this is dev/staging; production has this migration deferred to a Production-cutover slice per §6).
4. Merge the PR + wait for CI green.
5. Watch the deploy log; assert the migration prints its "merged N duplicate rows" line.

Production cutover (deferred to a later slice, NOT F11.7.6):
- Production has never enabled DevAuth (`docs/OPS_M_0_PLAN.md`).
- Production has never invoked `SetPersonaEmail` (dev-bridge only, host-env-guarded).
- So Production `identity.users` should have zero duplicates today. But belt-and-braces: production migration adds a `SELECT COUNT(*) FROM dupes; RAISE NOTICE 'Found % duplicate email groups', ...;` check that FAILS the migration if any dupes are found — better to fail-fast in production than merge silently.

### 3.7 Idempotency

The merge SQL uses `NOT EXISTS` on the winner-selection step and re-runnability of the UPDATEs (idempotent by construction — repointing a row that's already pointing at the surviving id is a no-op). If the migration is re-run (retry after a transient failure mid-flight), it produces no additional changes.

---

## 4. Arch-test additions (catch future drift)

### 4.1 `tests/VrBook.Architecture.Tests/UsersTableEmailUniquenessTests.cs` (NEW, F11.7.6.1)

Assertions:
- `UserConfiguration.Configure` calls `.HasIndex(u => u.Email).IsUnique()` (or partial-unique with `deleted_at IS NULL` filter). Roslyn syntax walk on the source file, not reflection on the compiled model — the model builder's runtime state can be gamed; the SOURCE assertion catches the intent.
- `ProvisionUserHandler` references BOTH `GetByB2CObjectIdAsync` AND `GetByEmailAsync`. Locks the upsert shape against a future refactor that "simplifies" by dropping the email-hit branch.
- `IUserRepository` declares `GetByEmailAsync` with a `CancellationToken` parameter. Locks the method signature so tests don't need to update en masse.

### 4.2 `tests/VrBook.Architecture.Tests/UserAggregateRebindContractTests.cs` (NEW, F11.7.6.2)

Assertions:
- `User.RebindB2CObjectId` exists as a `public void` method taking `(string, Guid)`. Signature lock.
- The event `UserB2CObjectIdRebound` is a `sealed record` in the Contracts events assembly. Consumer-facing shape lock.

### 4.3 Extend `tests/VrBook.Architecture.Tests/TenantAuthorizationBackgroundScopeBypassTests.cs` (F11.7.6.3)

Add one fact: `TenantAuthorizationBehavior` does NOT reference `ICurrentUser.Email`. Locks in the rejection of Candidate C — no future refactor can quietly widen the PA bypass to "any row with matching email."

### 4.4 Extend `tests/VrBook.Architecture.Tests/PlatformAdminEndpointRoleGateTests.cs` (F11.7.6.4)

Grep-existing test; if it asserts endpoint role gates by role name, add: `BootstrapOperator`'s F11.7.5.10 "promote every email match" loop is STILL PRESENT but is now provably unreachable (the unique index makes `usersWithEmail.Count > 1` impossible for active rows). The test asserts that either the loop is present with a comment marker `// F11.7.5.10 interim: reachable only for soft-deleted rows` OR the loop has been simplified back to a single-user path. Both are OK; picking one is a F11.7.6.6 close-out decision.

---

## 5. Runbook updates (deferred to F11.7.6.6 close-out — no separate commit)

Add to `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md`:

- New section: **"Handling a `DuplicateEmailException` from `SetPersonaEmail` or Entra migration."**
  - Look up `identity.users WHERE lower(email) = lower('...')` — find both rows.
  - Decide which row is the human's "real" identity (usually the one with active memberships or platform_admin=true).
  - Options: (a) Deactivate the loser row (`user.Deactivate(...)`); (b) rebind loser to a new email if it belongs to a different human who happens to share the inbox (rare).
  - Never delete a row directly — the aggregate's `Deactivate` handles soft-delete + event.

Add to `docs/OPS_M_10_2_F11_7_6_MULTI_ROW_USER_FIX.md` §11:

- Note whether the F11.7.5.10 loop-across-emails code was kept or removed.

---

## 6. Time estimate

| Commit | Estimate |
|---|---|
| F11.7.6.1 (tests-red) | 45 min |
| F11.7.6.2 (domain: Rebind) | 30 min |
| F11.7.6.3 (handler upsert) | 30 min |
| F11.7.6.4 (migration) | 90 min (hand-written SQL + pre-deploy checklist) |
| F11.7.6.5 (DevAuth guard) | 30 min |
| F11.7.6.6 (doc close-out) | 15 min |
| **Total** | **~4 hours** + CI wait between pushes. |

---

## 7. What NOT to do (boundaries)

- **Do NOT change ADR-0014's `is_platform_admin` DB-wins contract.** Preserved as-is.
- **Do NOT widen the PlatformAdmin bypass to consult `ICurrentUser.Email`.** Rejected as Candidate C.
- **Do NOT remove the `Slice4_DropEmailUnique` migration.** That migration is history; the new F11.7.6.4 migration re-adds uniqueness with a wider filter (partial unique on `lower(email)` where `deleted_at IS NULL`).
- **Do NOT change `B2CObjectId` uniqueness.** Still `IsUnique()`. What we CAN do is UPDATE it (via `RebindB2CObjectId`) — that's a distinct operation from "another row can share it."
- **Do NOT do the Production migration in F11.7.6.** Production is duplicate-free by construction (§3.6); the same migration will run on Production as part of the next production deploy, but with the fail-fast RAISE NOTICE. Do NOT layer additional Production-specific logic; the migration is one artifact.
- **Do NOT try to fix `DevAuth:AllowAnonymous`-in-Production as part of this slice.** That's a separate hardening pass. This slice trusts the existing three-guard (host-env + AllowAnonymous + AllowStripeStub) for the dev-bridge writes.
- **Do NOT touch `UserProvisioningMiddleware.cs:73-76`.** Once the invariant is enforced, the middleware's per-oid read is CORRECT — there is exactly one row for the human, and it carries the truthful `is_platform_admin` bit.

---

## 8. Residual risk register

| Risk | Severity | Mitigation today | Follow-up |
|---|---|---|---|
| Real Entra email-change means a human transiently has two rows during migration cutover (unlikely in staging; theoretically possible in Prod). | Low. | `ProvisionUserHandler` upsert on email-hit rebinds the surviving row's B2CObjectId. `UserB2CObjectIdRebound` event fires for the audit trail. | Consider adding an `UserEmailChanged` event handler in a future slice that verifies no stray row exists post-change. |
| Case-insensitive uniqueness assumes a normalized `lower(email)` — a caller who bypasses the aggregate and inserts raw SQL could still create a duplicate. | Very low. | The partial-unique DB index catches it. Any raw-SQL path is out-of-invariant regardless. | None. |
| The F11.7.6.4 migration is not losslessly reversible. | Medium (mitigated). | Pre-deploy snapshot named in the checklist (§3.6). Down migration only removes the index, not the row deletion. | None. |
| DevAuth `SetPersonaEmail` becomes more restrictive (409 on collision instead of silent overwrite). Ops runbook must reflect the new failure mode. | Low. | Runbook update in §5. Actionable 409 error payload. | None. |
| Cross-schema UPDATEs in an Identity module migration violate the "module owns its schema" convention. | Low (documented). | Migration XML doc explains why. Alternative (per-module migration fan-out) is riskier per §3.3. | Post-slice, consider a `docs/adr` entry codifying "identity merges can cross-schema-UPDATE for lifecycle events." |

---

## 9. Open questions (for user confirmation before execution)

1. **Winner-selection rule** — is "PA=true > membership_count > oldest CreatedAt > smallest Id" the right precedence? Alternative: "PA=true > oldest > membership_count." Default: as written (membership_count second, because a row wired into the tenant graph is more expensive to reroute than a bare row).
2. **Migration Production posture** — deploy the migration to Production the same way as staging (relying on §3.6's fail-fast), OR gate it behind an explicit `MIGRATE_MERGE_ACK=true` env var for Production? Default: same as staging (fail-fast is enough; Production has no duplicates).
3. **F11.7.5.10 loop-across-emails code** — keep-as-defense or remove-as-unreachable in F11.7.6.6 close-out? Default: keep for a slice or two as belt-and-braces (the loop is unreachable for active rows post-migration; harmless overhead). Remove in the F11.8 close-out.
4. **`UserB2CObjectIdRebound` event handling** — do we need a Notifications/Loyalty-side reaction to a rebind? Default: no (the row's `Id` is unchanged; downstream references are stable). The event exists purely for the audit trail today.

---

## 10. Verification protocol (for the architect/Claude to run after F11.7.6.5 is green)

```sh
# 1. Confirm the interim from F11.7.5.10 is still visible in the response.
curl -X POST https://api.vrbook-staging.example.com/api/v1/dev-auth/bootstrap-operator \
  -b /tmp/cookies.txt -H "Content-Type: application/json" \
  -d '{"email":"niroshanaks@gmail.com","tenantId":"00000000-0000-0000-0000-000000000001"}' | jq
# Expect: usersPromoted=1 (was 2 before this slice). One row per human is the invariant.

# 2. Force a duplicate via SetPersonaEmail — should now 409.
curl -X POST 'https://api.vrbook-staging.example.com/api/v1/dev-auth/persona-email?persona=Guest&email=niroshanaks@gmail.com' -i
# Expect: 409, body includes conflictingUserId.

# 3. SQL count check.
psql -h staging-vrbook -c "SELECT lower(email), COUNT(*) FROM identity.users WHERE deleted_at IS NULL GROUP BY 1 HAVING COUNT(*) > 1;"
# Expect: 0 rows.

# 4. Full end-to-end: sign out of staging, sign back in as niroshanaks@gmail.com via Entra.
#    UserProvisioningMiddleware finds the existing row by oid (no rebind needed if oid unchanged).
#    Confirm/Reject on /admin/bookings/{id} → 200. F11.7 walk-3 unblocked.
```

UI handoff to user follows the F11.7.5 pattern (paths from `feedback_slice_completion_test_pattern`).

---

## 11. Close-out

TO BE APPENDED at F11.7.6.6 commit time.
