using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.13 (M.13.2) — new schema for the identity redesign per
    /// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §2.1.
    ///
    /// <para>Creates two new tables in the <c>identity</c> schema:</para>
    /// <list type="bullet">
    ///   <item><b>user_identities</b> — (provider, external_id) → user_id
    ///     mapping. Enables one-human-many-oids: one <c>users</c> row per
    ///     human, N <c>user_identities</c> rows per provider (Entra today;
    ///     Google + Microsoft federated through Entra in OPS.M.12).
    ///     UNIQUE(provider, external_id) enforces the "same external id
    ///     never binds to two humans" invariant.</item>
    ///   <item><b>migration_audit</b> — canonical record of data-heal
    ///     migration side-effects. Motivated by the F11.7 failure mode
    ///     where <c>RAISE NOTICE</c> from container-app jobs did not
    ///     reliably reach Log Analytics; three data-heals shipped blind.
    ///     M.13.4's backfill is the first customer.</item>
    /// </list>
    ///
    /// <para>Also adds a partial-UNIQUE index on <c>lower(email)</c> for
    /// active users — reinstates uniqueness that Slice 4's
    /// <c>Slice4_DropEmailUnique</c> (20260622144933) dropped, but as a
    /// partial index so soft-deleted rows don't participate. The
    /// email-first provisioning algorithm (M.13.3) uses this as the race
    /// arbiter for two-tab-fresh-sign-in.</para>
    ///
    /// <para>Also adds a CHECK constraint on
    /// <c>user_identities.provider</c> restricting the allowed values to
    /// the enumeration defined in the design doc. Applied via raw SQL
    /// because EF Core's Fluent API doesn't emit CHECK constraints natively.</para>
    ///
    /// <para>M.13.4 is the DATA-HEAL migration that follows this schema-add
    /// step and actually collapses the multi-row-per-email state into the
    /// new shape. This migration is safe to run against any DB — no data
    /// movement occurs here.</para>
    /// </summary>
    public partial class OpsM13_UserIdentitiesAndMigrationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "migration_audit",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    migration_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    step_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    affected_count = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    executed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_audit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_identities",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_identities_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_migration_audit_migration_executed",
                schema: "identity",
                table: "migration_audit",
                columns: new[] { "migration_name", "executed_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_user_identities_user_id",
                schema: "identity",
                table: "user_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "user_identities_provider_extid_uq",
                schema: "identity",
                table: "user_identities",
                columns: new[] { "provider", "external_id" },
                unique: true);

            // CHECK constraint on provider — not natively emitted by
            // EF Fluent API. Raw SQL keeps the allowed-values list in one
            // place; adding a new provider (post-M.12) is one migration
            // that alters this constraint.
            migrationBuilder.Sql(@"
ALTER TABLE identity.user_identities
    ADD CONSTRAINT ck_user_identities_provider
    CHECK (provider IN ('entra','google','microsoft','apple','test'));
");

            // Partial-UNIQUE on lower(email) for active users. Slice 4's
            // Slice4_DropEmailUnique (20260622144933) dropped the previous
            // full-column UNIQUE for shared-inbox DevAuth testing; that
            // gap is what allowed the multi-row-per-email hazard F11.7
            // failed to close cleanly. This partial index is stricter
            // (lowercased + only-active-rows) and is the race arbiter
            // for the M.13.3 email-first provisioning algorithm.
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX users_email_active_lower_uq
    ON identity.users (lower(email))
    WHERE deleted_at IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS identity.users_email_active_lower_uq;");
            migrationBuilder.Sql("ALTER TABLE identity.user_identities DROP CONSTRAINT IF EXISTS ck_user_identities_provider;");

            migrationBuilder.DropTable(
                name: "migration_audit",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_identities",
                schema: "identity");
        }
    }
}
