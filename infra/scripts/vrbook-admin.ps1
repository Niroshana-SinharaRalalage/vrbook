<#
.SYNOPSIS
    Slice OPS.M.22.5 - operator-facing tool for pre-seeding admin
    identity.users rows BEFORE first sign-in.

    Complements the POST /api/v1/admin/platform/users/seed endpoint (M.22.2):
    - The endpoint is the programmatic path used by the future admin
      wizard + any UI caller. Requires a PlatformAdmin bearer token.
    - This cmdlet is the OPERATOR path - connects to Postgres directly
      via the KV-stored connection string (Azure managed identity handles
      the auth). Works even for the very first platform admin (chicken-
      and-egg) because there's no bearer token requirement.

    All writes go through the same shape the M.22.2 handler enforces
    (pre_seeded_at NOT NULL, tenant_memberships rows for tenant admins).
    ALL SQL is idempotent - re-running for the same email is a safe no-op.

.PARAMETER Env
    Which environment's DB + KV to write to. dev | staging | prod.

.PARAMETER Action
    seed-platform-admin - insert an identity.users row + set is_platform_admin=true.
    seed-tenant-admin   - insert an identity.users row + insert a tenant_admin membership.
    list                - list all pre-seeded admins (`pre_seeded_at IS NOT NULL`).
    revoke              - set is_platform_admin=false + soft-delete tenant_memberships for the given email.

.PARAMETER Email
    Target admin's email. Required for seed-* + revoke. Lowercased before write.

.PARAMETER DisplayName
    Display name for the seeded row. Required for seed-* actions.

.PARAMETER TenantId
    Required for seed-tenant-admin: the target tenant's identity.tenants."Id" GUID.

.PARAMETER IsPrimary
    Optional flag for seed-tenant-admin. Marks the membership as the caller's
    primary tenant. Defaults to true.

.EXAMPLE
    # Very first platform admin bootstrap (chicken-and-egg - no PA exists yet).
    .\vrbook-admin.ps1 -Env staging -Action seed-platform-admin `
        -Email 'niroshanaks@gmail.com' -DisplayName 'Niroshana'

.EXAMPLE
    # Seed a tenant admin for the "VrBook Default" tenant.
    .\vrbook-admin.ps1 -Env staging -Action seed-tenant-admin `
        -Email 'niroshhh@gmail.com' -DisplayName 'Niroshana Guest' `
        -TenantId '00000000-0000-0000-0000-000000000001'

.EXAMPLE
    .\vrbook-admin.ps1 -Env staging -Action list

.EXAMPLE
    .\vrbook-admin.ps1 -Env staging -Action revoke -Email 'stale-admin@example.com'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('dev', 'staging', 'prod')][string]$Env,
    [Parameter(Mandatory = $true)][ValidateSet('seed-platform-admin', 'seed-tenant-admin', 'list', 'revoke')][string]$Action,
    [string]$Email,
    [string]$DisplayName,
    [string]$TenantId,
    [bool]$IsPrimary = $true
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

# ---- Preflight: psql present ----
$psqlBin = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlBin) {
    Write-Fail "psql not found on PATH. Install PostgreSQL client tools:"
    Write-Host "  Windows: winget install --id PostgreSQL.PostgreSQL"
    Write-Host "  macOS:   brew install libpq && brew link --force libpq"
    Write-Host "  Linux:   apt-get install postgresql-client (or distro equivalent)"
    throw "psql required for vrbook-admin. Retry after install."
}

# ---- Preflight: az login ----
$acct = az account show -o json 2>$null | ConvertFrom-Json
if (-not $acct) {
    Write-Fail "Not signed in to Azure. Run 'az login' first."
    throw "az login required."
}
Write-Ok "Signed in as $($acct.user.name)"

$names = Get-ResourceNames -Env $Env
$kvName = $names.KeyVault

# ---- Fetch connection string ----
Write-Step "Fetching postgres-cs from $kvName"
$connStr = az keyvault secret show --vault-name $kvName --name 'postgres-cs' --query value -o tsv 2>$null
if (-not $connStr -or $connStr -eq 'pending-bicep-deploy' -or $connStr.StartsWith('pending-')) {
    throw "postgres-cs in $kvName is a placeholder ('$connStr'). Post-Bicep-deploy secret write hasn't run. See infra/scripts/10-store-secrets.ps1 §postgres-cs."
}
if (-not ($connStr -match 'Database=vrbook')) {
    throw "postgres-cs targets '$($connStr)'. Refusing to write - MUST include 'Database=vrbook' per CLAUDE.md staging notes (INFRA.1 followup A11)."
}

# Parse Npgsql-style connection string → psql PG* env vars.
function ConvertTo-PsqlEnv {
    param([string]$Cs)
    $kv = @{}
    foreach ($pair in $Cs -split ';') {
        if ($pair -match '^\s*([^=]+)=(.*)$') {
            $kv[$Matches[1].Trim().ToLowerInvariant()] = $Matches[2].Trim()
        }
    }
    @{
        PGHOST     = $kv['host']
        PGPORT     = if ($kv['port']) { $kv['port'] } else { '5432' }
        PGDATABASE = $kv['database']
        PGUSER     = $kv['username']
        PGPASSWORD = $kv['password']
        PGSSLMODE  = if ($kv['ssl mode']) { $kv['ssl mode'].ToLowerInvariant() } else { 'require' }
    }
}

$pgEnv = ConvertTo-PsqlEnv -Cs $connStr

# Every SQL invocation carries its args as heredoc through stdin so credentials
# stay off the process arg list (visible in ps / audit logs).
function Invoke-Psql {
    param([Parameter(Mandatory = $true)][string]$Sql)
    $prev = @{}
    foreach ($k in $pgEnv.Keys) {
        $prev[$k] = [Environment]::GetEnvironmentVariable($k)
        [Environment]::SetEnvironmentVariable($k, $pgEnv[$k])
    }
    try {
        # -X: skip .psqlrc. -A: unaligned. -t: tuples-only. -F "`t": tab separator.
        # -v ON_ERROR_STOP=1: any failure bubbles a non-zero exit.
        $result = $Sql | psql -X -A -t -F "`t" -v ON_ERROR_STOP=1 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "psql exited $LASTEXITCODE. Output: $result"
        }
        return $result
    }
    finally {
        foreach ($k in $prev.Keys) {
            [Environment]::SetEnvironmentVariable($k, $prev[$k])
        }
    }
}

# ---- Action dispatch ----
switch ($Action) {
    'seed-platform-admin' {
        if (-not $Email) { throw '-Email required for seed-platform-admin.' }
        if (-not $DisplayName) { throw '-DisplayName required for seed-platform-admin.' }
        $normalizedEmail = $Email.Trim().ToLowerInvariant()
        Write-Step "Seeding platform admin $normalizedEmail"

        # Idempotent: INSERT ... ON CONFLICT would work but the partial-unique
        # index is on `WHERE deleted_at IS NULL` - Postgres won't use it for
        # ON CONFLICT. Do the read-then-write dance in a single transaction.
        $sql = @"
BEGIN;

-- Check whether an active row exists for this email.
WITH existing AS (
    SELECT "Id", pre_seeded_at, is_platform_admin
      FROM identity.users
     WHERE deleted_at IS NULL
       AND lower(email) = '$normalizedEmail'
     FOR UPDATE
),
-- Insert if no existing row.
inserted AS (
    INSERT INTO identity.users
        ("Id", email, display_name, phone, is_platform_admin, email_verified,
         row_version, created_at, updated_at, pre_seeded_at)
    SELECT gen_random_uuid(),
           '$normalizedEmail',
           '$($DisplayName -replace "'", "''")',
           '',
           TRUE,
           FALSE,
           0,
           NOW(),
           NOW(),
           NOW()
    WHERE NOT EXISTS (SELECT 1 FROM existing)
    RETURNING "Id"
),
-- Idempotent grant on an existing pre-seeded row.
grants AS (
    UPDATE identity.users
       SET is_platform_admin = TRUE,
           updated_at = NOW()
     WHERE lower(email) = '$normalizedEmail'
       AND deleted_at IS NULL
       AND pre_seeded_at IS NOT NULL
       AND is_platform_admin = FALSE
    RETURNING "Id"
),
-- Audit trail via migration_audit (same table M.13 uses).
audit AS (
    INSERT INTO identity.migration_audit
        ("Id", migration_name, step_name, affected_count, notes, executed_at)
    SELECT gen_random_uuid(),
           'OpsM22_VrBookAdminScript',
           'seed-platform-admin',
           COALESCE((SELECT COUNT(*)::int FROM inserted), 0)
           + COALESCE((SELECT COUNT(*)::int FROM grants), 0),
           'operator=$($acct.user.name) email=$normalizedEmail displayName="$($DisplayName -replace "'", "''")" first=n/a',
           NOW()
    RETURNING 1
)
SELECT "Id" FROM inserted
UNION ALL
SELECT "Id" FROM existing;

COMMIT;
"@
        $row = Invoke-Psql -Sql $sql
        Write-Ok "Platform admin row present. Id: $row"
    }

    'seed-tenant-admin' {
        if (-not $Email) { throw '-Email required for seed-tenant-admin.' }
        if (-not $DisplayName) { throw '-DisplayName required for seed-tenant-admin.' }
        if (-not $TenantId) { throw '-TenantId required for seed-tenant-admin.' }
        $normalizedEmail = $Email.Trim().ToLowerInvariant()
        $isPrimarySql = if ($IsPrimary) { 'TRUE' } else { 'FALSE' }
        Write-Step "Seeding tenant admin $normalizedEmail on tenant $TenantId"

        $sql = @"
BEGIN;

-- Validate the tenant exists (fail-fast; nothing written otherwise).
DO `$check`$
DECLARE
    tenant_exists boolean;
BEGIN
    SELECT EXISTS (SELECT 1 FROM identity.tenants WHERE "Id" = '$TenantId'::uuid AND deleted_at IS NULL) INTO tenant_exists;
    IF NOT tenant_exists THEN
        RAISE EXCEPTION 'Tenant % not found. Aborting.', '$TenantId';
    END IF;
END
`$check`$;

-- Ensure the users row exists (pre-seeded shape).
INSERT INTO identity.users
    ("Id", email, display_name, phone, is_platform_admin, email_verified,
     row_version, created_at, updated_at, pre_seeded_at)
SELECT gen_random_uuid(),
       '$normalizedEmail',
       '$($DisplayName -replace "'", "''")',
       '',
       FALSE,
       FALSE,
       0,
       NOW(),
       NOW(),
       NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM identity.users
     WHERE lower(email) = '$normalizedEmail' AND deleted_at IS NULL
);

-- Insert the tenant_admin membership. Idempotent on (user, tenant) pair.
INSERT INTO identity.tenant_memberships
    ("Id", user_id, tenant_id, role, is_primary, row_version, created_at, updated_at)
SELECT gen_random_uuid(),
       u."Id",
       '$TenantId'::uuid,
       'tenant_admin',
       $isPrimarySql,
       0,
       NOW(),
       NOW()
  FROM identity.users u
 WHERE lower(u.email) = '$normalizedEmail'
   AND u.deleted_at IS NULL
   AND NOT EXISTS (
       SELECT 1 FROM identity.tenant_memberships m
        WHERE m.user_id = u."Id"
          AND m.tenant_id = '$TenantId'::uuid
          AND m.deleted_at IS NULL);

-- Audit.
INSERT INTO identity.migration_audit
    ("Id", migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(),
        'OpsM22_VrBookAdminScript',
        'seed-tenant-admin',
        1,
        'operator=$($acct.user.name) email=$normalizedEmail tenant=$TenantId',
        NOW());

COMMIT;

-- Emit the resolved user + membership ids for confirmation.
SELECT u."Id" AS user_id, m."Id" AS membership_id
  FROM identity.users u
  JOIN identity.tenant_memberships m ON m.user_id = u."Id"
 WHERE lower(u.email) = '$normalizedEmail'
   AND u.deleted_at IS NULL
   AND m.tenant_id = '$TenantId'::uuid
   AND m.deleted_at IS NULL;
"@
        $row = Invoke-Psql -Sql $sql
        Write-Ok "Tenant admin membership present."
        Write-Host "  $row"
    }

    'list' {
        Write-Step "Listing pre-seeded admins"
        $sql = @"
SELECT u."Id" AS user_id,
       u.email,
       u.display_name,
       u.is_platform_admin,
       u.pre_seeded_at,
       (SELECT COUNT(*) FROM identity.tenant_memberships m
         WHERE m.user_id = u."Id" AND m.deleted_at IS NULL AND m.role = 'tenant_admin') AS tenant_admin_count
  FROM identity.users u
 WHERE u.deleted_at IS NULL
   AND u.pre_seeded_at IS NOT NULL
 ORDER BY u.pre_seeded_at;
"@
        $rows = Invoke-Psql -Sql $sql
        if (-not $rows) {
            Write-Host "  (no pre-seeded admins yet - either the environment is fresh OR every admin row was created via lazy provisioning pre-M.22)"
        }
        else {
            Write-Host ("  {0,-38}  {1,-30}  {2,-25}  {3,-5}  {4,-30}  {5}" -f 'user_id', 'email', 'display_name', 'PA', 'pre_seeded_at', 'ta_ct')
            Write-Host ("  {0}" -f ('-' * 130))
            $rows | ForEach-Object {
                $c = $_ -split "`t"
                Write-Host ("  {0,-38}  {1,-30}  {2,-25}  {3,-5}  {4,-30}  {5}" -f $c[0], $c[1], $c[2], $c[3], $c[4], $c[5])
            }
        }
    }

    'revoke' {
        if (-not $Email) { throw '-Email required for revoke.' }
        $normalizedEmail = $Email.Trim().ToLowerInvariant()
        Write-Step "Revoking admin authority for $normalizedEmail"

        $sql = @"
BEGIN;

-- Read the target row for the audit trail.
WITH target AS (
    SELECT "Id" AS user_id, is_platform_admin
      FROM identity.users
     WHERE lower(email) = '$normalizedEmail'
       AND deleted_at IS NULL
),
-- Drop platform-admin flag.
pa_drop AS (
    UPDATE identity.users
       SET is_platform_admin = FALSE,
           updated_at = NOW()
     WHERE lower(email) = '$normalizedEmail'
       AND deleted_at IS NULL
       AND is_platform_admin = TRUE
    RETURNING "Id"
),
-- Soft-delete every active tenant_admin membership.
tm_drop AS (
    UPDATE identity.tenant_memberships
       SET deleted_at = NOW(),
           updated_at = NOW()
     WHERE user_id IN (SELECT user_id FROM target)
       AND role = 'tenant_admin'
       AND deleted_at IS NULL
    RETURNING "Id"
),
audit AS (
    INSERT INTO identity.migration_audit
        ("Id", migration_name, step_name, affected_count, notes, executed_at)
    SELECT gen_random_uuid(),
           'OpsM22_VrBookAdminScript',
           'revoke',
           COALESCE((SELECT COUNT(*)::int FROM pa_drop), 0)
           + COALESCE((SELECT COUNT(*)::int FROM tm_drop), 0),
           'operator=$($acct.user.name) email=$normalizedEmail',
           NOW()
    RETURNING 1
)
SELECT COUNT(*) AS pa_dropped FROM pa_drop
UNION ALL
SELECT COUNT(*) AS tm_dropped FROM tm_drop;

COMMIT;
"@
        $counts = Invoke-Psql -Sql $sql
        Write-Ok "Revoke complete: platform_admin/tenant_memberships counts=$counts"
    }
}

Write-Step "Done."
