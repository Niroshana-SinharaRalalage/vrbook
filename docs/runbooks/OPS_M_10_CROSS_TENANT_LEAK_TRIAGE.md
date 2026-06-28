# OPS.M.10 — Cross-Tenant Leak Triage

When the M.10 test pack catches a regression — typically a CI failure on
`CrossTenantEndpointMatrix`, `RlsPolicySchemaFactPack`, or
`EndpointCoverageArchTest` — work this checklist.

## Quick decision tree

1. Which test class failed?
2. Is it an endpoint (matrix), schema (RLS), or coverage (arch) failure?
3. Has any controller / migration / handler been touched in the PR?
4. Is the regression a known carve-out or a genuine leak?

## A. `EndpointCoverageArchTest` failed

A new controller action lacks an explicit access decision.

Failure message names the type:

```
VrBook.Api.Controllers.NewController.SomeAction
```

The fix is ONE of:

1. Add `[Authorize]` (or `[Authorize(Roles="...")]`) on the action /
   controller — the endpoint is gated and the M.10 matrix needs a row in
   `RouteMatrix.GetAll()`.
2. Add `[AllowAnonymous]` if the endpoint is intentionally public.
3. Add `[ExemptFromCrossTenantMatrix("reason")]` if the endpoint is
   gated but NOT tenant-scoped (e.g. a DevAuth diagnostic).

Each option is a deliberate review decision. Don't just suppress —
document the choice.

## B. `RlsPolicySchemaFactPack` failed

The Postgres-fixture test reports a table missing RLS or its policy.

```
catalog.properties must have ROW LEVEL SECURITY enabled.
```

Possible causes:
- A migration was added that creates a new tenant-scoped table without
  calling `migrationBuilder.EnableRlsTenantIsolation(schema, table)`.
- An existing migration was edited (DON'T do this — write a forward
  migration).
- The migration ran in a non-canonical order (the M.9 §13 ordering
  guarantee).

Fix:
1. Identify the new table from the test failure message.
2. Either add the policy in a new migration via
   `EnableRlsTenantIsolation`, OR add the table to the
   `RlsCarveOutSchemaFactPack.CarveOutTables()` list with a documented
   justification.
3. Re-run the test against a fresh testcontainer.

## C. `RlsCarveOutSchemaFactPack` failed

A carve-out table now HAS RLS enabled — likely from a migration that
ran `ALTER TABLE ... ENABLE ROW LEVEL SECURITY` unintentionally.

Carve-outs exist for a reason:
- `identity.users` — platform-level identity; no tenant ownership.
- `identity.tenants` — the table IS the tenant.
- `identity.tenant_memberships` — the bootstrap path that resolves the
  tenant claim BEFORE RLS can know which tenant to filter.
- `*.outbox_messages` — read by the outbox relay across modules.

If the test fails, the offending migration must be reverted or fixed.
Cross-reference [OPS_M_9_PLAN.md](../OPS_M_9_PLAN.md) §3.2 for the
full carve-out justification.

## D. `CrossTenantEndpointMatrix` failed (Owner-A → tenant B)

A row asserting Owner-A gets 403 / 404 on tenant B's data instead
returned 200. Genuine leak.

Triage:
1. Read the failing row's `Description` field. It names the route,
   persona, and expected status.
2. Run the same request manually via curl with the DevAuth persona
   cookie pointed at the staging API.
3. Identify which gate should have fired:
   - `TenantAuthorizationBehavior` (M.4) — does the command implement
     `ITenantScoped`?
   - The route's `[Authorize]` filter — does it accept the offending role?
   - The handler's defense-in-depth check — does it call
     `CallerTenantId()` or trust the URL?
   - The M.9 RLS policy — did the query stamp the wrong
     `app.tenant_id`?
4. Fix the gap. Add a regression test that fails before the fix and
   passes after.

## E. `CrossTenantRejectionAuditFactPack` failed

A cross-tenant rejection happened but `identity.audit_log` doesn't
record it. The M.4 `AuditLogBehavior` exception path didn't fire.

Check:
- Did the failed command implement `IAuditable` (or the equivalent
  marker M.4 uses)?
- Did the behavior pipeline order get scrambled? The order MUST be:
  `Validation → TenantAuthorization → AuditLog → handler`.
- Did the test seed an audit-disabled command path? Some commands
  carve out of audit for performance; check the M.4 plan.

## F. `PlatformAdminBypassFactPack` failed

A PlatformAdmin attempt to read cross-tenant data was either blocked
(no bypass) or succeeded without emitting the structured log line.

Check:
- Is the request actually carrying the PlatformAdmin claim? Check
  `/api/v1/me` returns `isPlatformAdmin: true`.
- Is the handler using `IRlsBypassDbContextFactory<T>.CreateForBypassAsync`
  OR `RlsBypassScope.Enter()`? The plan §7 inventory enumerates the
  allowed call sites.
- Is the structured log line present? Search the test's in-memory
  Serilog sink for `"RLS bypass open"` with the expected reason.

## G. `PlatformAdminPromoteRevokeSmokeTest` failed

The promote / revoke SQL doesn't flip the behavior end-to-end.

Check the `OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` runbook — the SQL there
must keep working after every M.8/M.9 migration. If a future migration
adds a column to `identity.users` that the seed test doesn't set, the
PromoteRevoke smoke fails first.

## H. `AsyncLocalLeakFactPack` failed

The M.9 `RlsBypassScope` or `BackgroundTenantScope` leaked across an
unrelated await — meaning a bypass-flagged read happened from a
caller that should not have been under bypass.

This is the most subtle failure. Check:
- Any new code that `await`s inside a `using var bypass = ...` block
  but the awaited task spawns work on another logical thread.
- Use of `Task.Run` from inside a bypass — the AsyncLocal flows there
  too, which may or may not be intended.

The fix is usually to narrow the bypass scope to just the DbContext
call rather than wrapping a broad operation.

## When the failure is a known false positive

Some test runs hit transient testcontainer issues (port collisions,
slow Docker pulls). The `Category=Integration` traits gate these to
CI's own integration job; they shouldn't run on the unit-test loop.
If you see one locally, retry once.

## Triage closure

Every M.10 failure that gets fixed in a PR MUST:
1. Reference this runbook in the PR description.
2. Add a regression-prevention fact if the gap was a new pattern not
   already covered by the matrix.
3. Update `OPS_M_10_PLAN.md` §11 deviations if the fix changed an
   M.10 invariant.
