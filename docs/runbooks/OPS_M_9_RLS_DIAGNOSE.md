# OPS.M.9 — RLS Diagnose Runbook

When a user reports "I can't see my data" or "I see no rows where I
expect some", or when an integration test fails with `0 rows returned`
unexpectedly, walk this checklist.

## Quick decision tree

1. Is the caller authenticated? (Check `/api/v1/me` returns 200.)
2. Does `ICurrentUser.TenantId` resolve to a non-null value?
3. Does the table that's being queried have RLS enabled?
4. Is the caller's tenant id matching the row's `tenant_id`?
5. Is this a cross-tenant read that legitimately needs the bypass?

## Step 1 — Confirm the user's `tenant_id`

Via DevAuth (local) or production Entra, hit the endpoint and inspect:

```bash
curl -i -H "Authorization: Bearer $TOKEN" \
  https://api.vrbook.com/api/v1/me/tenant | jq
```

If `tenantId` is null but the user is a known Owner / Admin, the
`tenant_memberships` row may be missing. Check:

```sql
SELECT u.id, u.email, tm.tenant_id, tm.role, tm.is_primary, tm.deleted_at
FROM identity.users u
LEFT JOIN identity.tenant_memberships tm ON tm.user_id = u.id
WHERE u.email = 'reporter@example.com';
```

## Step 2 — Confirm the table has RLS enabled

```sql
SELECT relname, relrowsecurity, relforcerowsecurity
FROM pg_class
WHERE relname IN ('properties', 'bookings', 'payment_intents')
ORDER BY relname;
```

All three columns should be `t` (true) after OpsM9 migrations. If
`relrowsecurity = f`, the migration didn't run — re-check
`__ef_migrations_history`.

## Step 3 — Inspect the policy

```sql
SELECT polname, pg_get_expr(polqual, polrelid) AS using_clause,
       pg_get_expr(polwithcheck, polrelid) AS with_check_clause
FROM pg_policy
WHERE polrelid = 'catalog.properties'::regclass;
```

The `using_clause` should reference both `current_setting('app.tenant_id', true)`
and `current_setting('app.is_platform_admin', true)`. If only one
references, an older policy is in place; consult the migration history.

## Step 4 — Simulate the query as the user

In a `psql` session, set the GUCs to match the caller's identity:

```sql
SET LOCAL app.tenant_id = '00000000-0000-0000-0000-000000000001';
SET LOCAL app.is_platform_admin = 'false';

SELECT count(*) FROM catalog.properties;
```

If this returns `0` for a tenant that you know has properties, either:
- The `tenant_id` column on the row doesn't match the GUC (data drift).
- The migration didn't apply correctly.

## Step 5 — Cross-tenant reads (PlatformAdmin path)

When a PlatformAdmin user reports "I can't see tenant X's data" on
`/admin/platform/tenants/<X>`:

```bash
# Check the user is actually PlatformAdmin
SELECT id, email, is_platform_admin FROM identity.users
WHERE email = 'operator@example.com';
```

If `is_platform_admin = true` but the page renders empty rows, check
the `RlsBypassDbContextFactory` log lines for the failed request:

```
RLS bypass open db_context=IdentityDbContext reason=get-platform-tenant
RLS GUC stamped tenant_id=<empty> is_platform_admin=true
```

Both must appear. If `is_platform_admin=false` appears, the
`RlsBypassScope` AsyncLocal didn't flow — check the handler is awaiting
the bypass factory's returned context (the most common bug is
forgetting `await using` and disposing the scope before the SELECT
runs).

## Step 6 — Webhook handler

The Stripe webhook handler runs without an `ICurrentUser`. If you see
webhook rows missing or `0 rows` returned during dispatch, check the
handler's log line:

```
RLS bypass open reason=stripe-webhook caller=HandleStripeWebhookHandler
```

If absent, the `RlsBypassScope.Enter()` call at the top of the handler
didn't fire — likely a code path that bypassed the early scope wrap.

## Step 7 — Background worker

The OPS.M.6 sync worker should log:

```
RLS bypass open db_context=SyncDbContext reason=sync-worker.list-due-feeds
... per-feed processing ...
```

If the per-feed processing runs but rows are missing per-tenant, the
`BackgroundTenantScope.Enter(feed.TenantId)` call (from the M.6
behavior) may have failed. Check the M.6 behavior's log scope output
for `tenant_id` matching the expected value.

## Manual recovery

If a legitimate read is blocked and you need to confirm row presence:

```sql
SET LOCAL app.is_platform_admin = 'true';
SELECT * FROM <schema>.<table> WHERE tenant_id = '<expected>';
RESET app.is_platform_admin;
```

NEVER leave the GUC set in a pooled connection — use a fresh
`psql` session. Production access requires the standard three-named-humans
approval per `OPS_M_8_PROMOTE_PLATFORM_ADMIN.md`.

## Forward — automated diagnostics

Slice OPS.M.10 ships the cross-tenant isolation test pack which
exercises every path in this runbook in CI. If a regression here would
have been caught by a missing M.10 test, file the gap as an M.10 issue
before patching.
