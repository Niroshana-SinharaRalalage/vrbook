using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.21 (M.15 App Roles legacy cleanup follow-up A step 3)
    /// — final schema change closing the legacy Owner/Admin flag surface.
    ///
    /// <para>Drops <c>identity.users.is_owner</c> +
    /// <c>identity.users.is_admin</c> boolean columns. Pre-M.15 these flags
    /// gated <c>[Authorize(Roles="Owner,Admin")]</c> decorators + a handful
    /// of business-logic sites reading <c>ICurrentUser.IsOwner</c>/
    /// <c>IsAdmin</c>. M.15 (M.15.1–M.15.5, 2026-07-06) migrated every
    /// consumer to <c>HasTenantRole(tenantId, "tenant_admin")</c> +
    /// <c>IsPlatformAdmin</c> per ADR-0014. M.21.A.1 reshaped the SPA nav
    /// derivation to key on <c>useMyTenants</c> + <c>isPlatformAdmin</c>
    /// (no more reads of these columns via the DTO). M.21.A.2 dropped the
    /// <c>UserDto.IsOwner</c>/<c>IsAdmin</c> DTO fields + the
    /// <c>User.GrantOwner</c>/<c>RevokeOwner</c>/<c>GrantAdmin</c>/
    /// <c>RevokeAdmin</c> domain methods. This migration completes the
    /// slice by dropping the underlying storage.</para>
    ///
    /// <para><b>Rollback:</b> forward-only; Down re-adds both columns with
    /// <c>defaultValue: false</c> — any pre-drop values are lost. If
    /// rollback is required, follow
    /// <c>docs/OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md</c> for the
    /// backfill script that reconstructs the columns from
    /// <c>identity.tenant_memberships</c>.</para>
    ///
    /// <para>See <c>docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md</c> §7-Q1 for
    /// the owner-locked answer authorizing this drop.</para>
    /// </summary>
    public partial class OpsM21_Users_DropOwnerAdminColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_admin",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_owner",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_owner",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
