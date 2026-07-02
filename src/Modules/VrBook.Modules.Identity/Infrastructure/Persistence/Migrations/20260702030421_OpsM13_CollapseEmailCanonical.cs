using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.13.4 — backfill data-heal migration per
    /// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.4 as amended by
    /// <c>docs/OPS_M_13_4_BACKFILL_REVIEW.md</c>.
    ///
    /// <para>Runs inside EF Core's default per-migration Postgres transaction.
    /// Any step failure rolls back the entire migration atomically —
    /// including the <c>_pre_m13_snap</c> schema. Fully idempotent on retry.</para>
    ///
    /// <para>Migrator role has <c>BYPASSRLS</c> (OpsM9); cross-schema UPDATEs
    /// against <c>booking.*</c>, <c>reviews.*</c>, <c>messaging.*</c>,
    /// <c>notifications.*</c>, <c>catalog.*</c> succeed without an
    /// <c>app.tenant_id</c> GUC.</para>
    ///
    /// <para>Every step emits an <c>identity.migration_audit</c> row so
    /// post-run diagnostics don't depend on container-app job stdout
    /// reaching Log Analytics (F11.7's opaque-data-heal failure mode).</para>
    ///
    /// <para>Steps:
    /// <list type="number">
    ///   <item>Snapshot to <c>_pre_m13_snap</c> schema (users + every FK-holder).</item>
    ///   <item>Compute <c>_work_survivor_map</c> via a total-ordering ROW_NUMBER
    ///     (PA DESC → membership-count DESC → CreatedAt ASC → Id ASC).</item>
    ///   <item>Rewrite named data FKs across 10 columns.</item>
    ///   <item>Rewrite all uuid audit columns (created_by / updated_by / deleted_by)
    ///     across every table in the DB via a dynamic loop. Rows are only
    ///     touched when the column value matches a non-survivor id.</item>
    ///   <item>Populate <c>user_identities</c> for live humans (survivors)
    ///     from the legacy <c>b2c_object_id</c>. Excludes M.13.3 placeholder
    ///     oids. Splits into 4a (survivors) + 4b (non-survivor-mapped-to-survivor)
    ///     so all rows land pointing at the survivor.</item>
    ///   <item>Soft-delete non-survivor <c>identity.users</c> rows with
    ///     <c>deleted_by = NULL</c> (system-initiated marker).</item>
    ///   <item>Drop <c>b2c_object_id</c> column + old unique index.</item>
    ///   <item>Ensure partial-UNIQUE on <c>lower(email) WHERE deleted_at IS NULL</c>
    ///     (already shipped by M.13.2; IF NOT EXISTS keeps this migration
    ///     safe on any DB state).</item>
    ///   <item>Emit final <c>complete</c> audit row.</item>
    /// </list></para>
    /// </summary>
    public partial class OpsM13_CollapseEmailCanonical : Migration
    {
        private const string MigrationName = "OpsM13_CollapseEmailCanonical";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Snapshot to _pre_m13_snap ---------------------------------------
            migrationBuilder.Sql(@"
CREATE SCHEMA IF NOT EXISTS _pre_m13_snap;

CREATE TABLE _pre_m13_snap.users             AS TABLE identity.users;
CREATE TABLE _pre_m13_snap.tenant_memberships AS TABLE identity.tenant_memberships;
CREATE TABLE _pre_m13_snap.audit_log          AS TABLE identity.audit_log;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='catalog' AND table_name='properties')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.catalog_properties AS TABLE catalog.properties'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='booking' AND table_name='bookings')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.booking_bookings AS TABLE booking.bookings'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='reviews' AND table_name='reviews')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.reviews_reviews AS TABLE reviews.reviews'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='messaging' AND table_name='threads')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.messaging_threads AS TABLE messaging.threads'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='messaging' AND table_name='messages')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.messaging_messages AS TABLE messaging.messages'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='notifications' AND table_name='notification_log')
    THEN EXECUTE 'CREATE TABLE _pre_m13_snap.notifications_notification_log AS TABLE notifications.notification_log'; END IF;
END $$;

INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'snapshot',
       (SELECT COUNT(*) FROM _pre_m13_snap.users), NULL, NOW();
");

            // Step 2: Survivor pick with total ordering ---------------------------------
            migrationBuilder.Sql(@"
CREATE TEMP TABLE _work_survivor_map ON COMMIT DROP AS
WITH ranked AS (
    SELECT
        u.""Id"" AS user_id,
        lower(u.email) AS email_key,
        ROW_NUMBER() OVER (
            PARTITION BY lower(u.email)
            ORDER BY u.is_platform_admin DESC,
                     (SELECT COUNT(*) FROM identity.tenant_memberships tm
                        WHERE tm.user_id = u.""Id"" AND tm.deleted_at IS NULL) DESC,
                     u.created_at ASC,
                     u.""Id"" ASC
        ) AS rn
    FROM identity.users u
    WHERE u.deleted_at IS NULL
),
survivors AS (SELECT user_id, email_key FROM ranked WHERE rn = 1)
SELECT r.user_id AS non_survivor_id, s.user_id AS survivor_id
  FROM ranked r
  JOIN survivors s ON s.email_key = r.email_key
 WHERE r.rn > 1;

INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'survivor_pick',
       (SELECT COUNT(*)::int FROM _work_survivor_map),
       'total-ordering PA DESC → membership-count DESC → CreatedAt ASC → Id ASC',
       NOW();
");

            // Step 3: Rewrite named data FK columns ------------------------------------
            // Each UPDATE is wrapped in WITH RETURNING + INSERT INTO migration_audit
            // so the affected_count is captured atomically per step.
            EmitFkRewrite(migrationBuilder, "identity", "tenant_memberships", "user_id",
                extraWhere: @" AND NOT EXISTS (
                    SELECT 1 FROM identity.tenant_memberships existing
                     WHERE existing.user_id = m.survivor_id
                       AND existing.tenant_id = t.tenant_id
                       AND existing.deleted_at IS NULL
                       AND existing.""Id"" <> t.""Id"")");
            EmitFkRewrite(migrationBuilder, "identity", "audit_log", "actor_user_id");
            EmitFkRewrite(migrationBuilder, "catalog", "properties", "owner_user_id");
            EmitFkRewrite(migrationBuilder, "booking", "bookings", "guest_user_id");
            EmitFkRewrite(migrationBuilder, "reviews", "reviews", "guest_user_id");
            EmitFkRewrite(migrationBuilder, "messaging", "threads", "guest_user_id");
            EmitFkRewrite(migrationBuilder, "messaging", "threads", "owner_user_id");
            EmitFkRewrite(migrationBuilder, "messaging", "messages", "sender_user_id");
            EmitFkRewrite(migrationBuilder, "messaging", "messages", "recipient_user_id");
            EmitFkRewrite(migrationBuilder, "notifications", "notification_log", "recipient_user_id");

            // Step 4: Rewrite ALL uuid audit columns (created_by / updated_by / deleted_by)
            // across every table in every schema. This closes the "orphan audit Guid"
            // gap flagged as F1 in the review. The dynamic loop touches only rows where
            // the column value matches a non-survivor id — no over-rewriting risk.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    r RECORD;
    step_count INTEGER;
BEGIN
    FOR r IN
        SELECT c.table_schema, c.table_name, c.column_name
          FROM information_schema.columns c
         WHERE c.column_name IN ('created_by', 'updated_by', 'deleted_by')
           AND c.data_type = 'uuid'
           AND c.table_schema NOT IN ('pg_catalog', 'information_schema', '_pre_m13_snap')
           -- Skip identity.users itself — audit columns on the row being
           -- soft-deleted are not something we want to rewrite.
           AND NOT (c.table_schema = 'identity' AND c.table_name = 'users')
    LOOP
        EXECUTE format($f$
            WITH updated AS (
                UPDATE %I.%I t SET %I = m.survivor_id
                  FROM _work_survivor_map m
                 WHERE t.%I = m.non_survivor_id
                RETURNING 1
            )
            SELECT COUNT(*)::int FROM updated
        $f$, r.table_schema, r.table_name, r.column_name, r.column_name)
        INTO step_count;

        INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
        VALUES (gen_random_uuid(),
                'OpsM13_CollapseEmailCanonical',
                format('rewrite_audit_%s.%s.%s', r.table_schema, r.table_name, r.column_name),
                step_count, NULL, NOW());
    END LOOP;
END $$;
");

            // Step 5: Populate user_identities from legacy b2c_object_id ---------------
            // Excludes M.13.3 placeholder oids (they were never real).
            migrationBuilder.Sql(@"
-- Step 5a — one user_identities row per SURVIVOR (rows that keep living).
WITH inserted_5a AS (
    INSERT INTO identity.user_identities
        (""Id"", user_id, provider, external_id, first_seen_at, last_seen_at,
         row_version, created_at, updated_at)
    SELECT gen_random_uuid(), u.""Id"",
           CASE WHEN u.b2c_object_id ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                THEN 'entra' ELSE 'test' END,
           u.b2c_object_id,
           COALESCE(u.last_login_at, u.created_at), COALESCE(u.last_login_at, u.created_at),
           0, NOW(), NOW()
      FROM identity.users u
     WHERE u.deleted_at IS NULL
       AND u.b2c_object_id NOT LIKE 'm13-placeholder-%'
       AND NOT EXISTS (SELECT 1 FROM _work_survivor_map m WHERE m.non_survivor_id = u.""Id"")
    ON CONFLICT (provider, external_id) DO NOTHING
    RETURNING 1
)
INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'populate_identities_survivors',
       (SELECT COUNT(*)::int FROM inserted_5a), NULL, NOW();

-- Step 5b — one user_identities row per NON-SURVIVOR, mapped to their survivor.
WITH inserted_5b AS (
    INSERT INTO identity.user_identities
        (""Id"", user_id, provider, external_id, first_seen_at, last_seen_at,
         row_version, created_at, updated_at)
    SELECT gen_random_uuid(), m.survivor_id,
           CASE WHEN u.b2c_object_id ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                THEN 'entra' ELSE 'test' END,
           u.b2c_object_id,
           COALESCE(u.last_login_at, u.created_at), COALESCE(u.last_login_at, u.created_at),
           0, NOW(), NOW()
      FROM identity.users u
      JOIN _work_survivor_map m ON m.non_survivor_id = u.""Id""
     WHERE u.b2c_object_id NOT LIKE 'm13-placeholder-%'
    ON CONFLICT (provider, external_id) DO NOTHING
    RETURNING 1
)
INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'populate_identities_nonsurvivors_linked',
       (SELECT COUNT(*)::int FROM inserted_5b), NULL, NOW();
");

            // Step 6: Soft-delete non-survivor users -----------------------------------
            migrationBuilder.Sql(@"
WITH deleted AS (
    UPDATE identity.users u
       SET deleted_at = NOW(),
           deleted_by = NULL,           -- system-initiated; never a real user Guid
           updated_at = NOW()
      FROM _work_survivor_map m
     WHERE u.""Id"" = m.non_survivor_id
    RETURNING 1
)
INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'soft_delete_nonsurvivors',
       (SELECT COUNT(*)::int FROM deleted), NULL, NOW();
");

            // Step 7: Drop the legacy b2c_object_id column + old index -----------------
            // EF Fluent scaffolded these but re-emitting in raw SQL keeps the
            // migration_audit trail complete + tolerant of index name drift.
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS identity.""IX_users_b2c_object_id"";
DROP INDEX IF EXISTS identity.""IX_users_email"";
ALTER TABLE identity.users DROP COLUMN IF EXISTS b2c_object_id;

INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'drop_b2c_object_id_column', 1, NULL, NOW());
");

            // Step 8: Ensure partial-UNIQUE on lower(email) ---------------------------
            // M.13.2 already shipped this. IF NOT EXISTS keeps this migration safe on
            // any DB state (e.g., a rolled-back M.13.2 that left no index).
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX IF NOT EXISTS users_email_active_lower_uq
    ON identity.users (lower(email))
    WHERE deleted_at IS NULL;

INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
VALUES (gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'ensure_partial_unique_email', 1, NULL, NOW());
");

            // Step 9: Final audit row -------------------------------------------------
            migrationBuilder.Sql(@"
INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
SELECT gen_random_uuid(), 'OpsM13_CollapseEmailCanonical', 'complete',
       (SELECT COUNT(*)::int FROM identity.users WHERE deleted_at IS NULL),
       'final active users count', NOW();
");
        }

        /// <summary>
        /// Emit an FK-rewrite UPDATE for a single (schema.table.column) with
        /// an <c>IF EXISTS</c> schema guard (fresh-DB safety in testcontainers)
        /// + a <c>WITH RETURNING</c> wrapper that atomically captures the
        /// affected count into <c>identity.migration_audit</c>.
        /// </summary>
        private static void EmitFkRewrite(
            MigrationBuilder mb, string schema, string table, string column, string extraWhere = "")
        {
            mb.Sql($@"
DO $$
DECLARE
    step_count INTEGER := 0;
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables
                WHERE table_schema='{schema}' AND table_name='{table}') THEN
        WITH updated AS (
            UPDATE {schema}.{table} t
               SET {column} = m.survivor_id
              FROM _work_survivor_map m
             WHERE t.{column} = m.non_survivor_id{extraWhere}
            RETURNING 1
        )
        SELECT COUNT(*)::int INTO step_count FROM updated;
    END IF;
    INSERT INTO identity.migration_audit (id, migration_name, step_name, affected_count, notes, executed_at)
    VALUES (gen_random_uuid(),
            'OpsM13_CollapseEmailCanonical',
            'rewrite_{schema}_{table}.{column}',
            step_count, NULL, NOW());
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down() intentionally only restores the schema shape — data cannot be
            // re-materialized without _pre_m13_snap (which lives outside the
            // migration transaction after Up() commits). If a full rollback is
            // needed, follow docs/OPS_M_13_4_BACKFILL_REVIEW.md §7.6.
            migrationBuilder.Sql("DROP INDEX IF EXISTS identity.users_email_active_lower_uq;");

            migrationBuilder.AddColumn<string>(
                name: "b2c_object_id",
                schema: "identity",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_users_b2c_object_id",
                schema: "identity",
                table: "users",
                column: "b2c_object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                schema: "identity",
                table: "users",
                column: "email");
        }
    }
}
