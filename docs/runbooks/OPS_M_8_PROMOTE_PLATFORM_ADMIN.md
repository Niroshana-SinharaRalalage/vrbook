# OPS.M.8 — Promote a User to Platform Admin

**Scope:** Slice OPS.M.8 ships the `is_platform_admin` DB flag as the
authoritative source of truth for the operator bypass. There is no web
UI for granting the flag (per §3.8 D8 — promotion is an out-of-band ops
action). This runbook documents the manual SQL until the
`vrbook-admin promote` PowerShell cmdlet ships in a follow-up slice.

## Pre-flight

1. Confirm the user has already signed in at least once (so a row
   exists in `identity.users`):
   ```sql
   SELECT id, email, display_name, b2c_object_id, is_owner, is_admin, is_platform_admin
   FROM identity.users
   WHERE email = 'niroshanaks@gmail.com';
   ```
   - If the row is missing: ask the user to sign in once, then re-check.
   - If `is_platform_admin = true` already: skip the grant.

2. Three-named-humans rule (audit policy): get explicit Slack
   approval from at least two other VrBook engineers before running the
   grant in production. Capture the approvals in the
   `#ops-platform-admin` channel; the grant SQL records `actor_user_id`
   for the audit log.

## Grant

```sql
BEGIN;
UPDATE identity.users
SET    is_platform_admin = true,
       updated_at = NOW(),
       updated_by = (SELECT id FROM identity.users WHERE email = 'YOUR.OWN@email.com')
WHERE  email = 'TARGET@email.com';
COMMIT;
```

The middleware re-reads the column on the user's next authenticated
request; no app restart needed.

## Verify

1. Re-run the SELECT above; confirm `is_platform_admin = true`.
2. Ask the user to refresh their browser. They should now see the
   **Platform** group in the admin sidebar (under their normal nav).
3. Have them open `/admin/platform/tenants` and confirm the page loads
   (returns 200; should show the full tenant list, not the empty 403
   error surface).

## Revoke

```sql
BEGIN;
UPDATE identity.users
SET    is_platform_admin = false,
       updated_at = NOW(),
       updated_by = (SELECT id FROM identity.users WHERE email = 'YOUR.OWN@email.com')
WHERE  email = 'TARGET@email.com';
COMMIT;
```

Same middleware re-read on next request invalidates the bypass.

## DevAuth path

For local DevAuth walkthroughs, after `Slice5b_DevAuth_Default_Tenant_Membership`
seeds the Admin persona, the operator's local DB can be promoted with:

```sql
UPDATE identity.users
SET    is_platform_admin = true
WHERE  email = 'admin.persona@vrbook.local';
```

The DevAuth Admin persona then automatically clears the
`TenantAuthorizationBehavior` PlatformAdmin bypass on every request,
and the `/admin/platform/tenants` route loads.

## Future swap

When the `vrbook-admin promote` PowerShell cmdlet ships
(Slice OPS.M.10 + Slice OPS.7 infrastructure pass), it wraps the same
SQL with:

```powershell
vrbook-admin promote --env prod --email target@email.com --actor your@email.com
vrbook-admin revoke  --env prod --email target@email.com --actor your@email.com
vrbook-admin list    --env prod
```

The cmdlet also captures the operator's identity via Entra App-Only
sign-in so the `updated_by` field is the cmdlet caller's user id, not a
hard-coded value. Delete this runbook on cmdlet ship.
