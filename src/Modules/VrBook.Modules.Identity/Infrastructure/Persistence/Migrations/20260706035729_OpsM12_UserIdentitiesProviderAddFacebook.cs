using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Slice OPS.M.12.3 — widen the CHECK constraint on
    /// <c>identity.user_identities.provider</c> to include <c>'facebook'</c>.
    ///
    /// <para>M.13 shipped the constraint as
    /// <c>('entra','google','microsoft','apple','test')</c>. M.12 policy
    /// (owner-locked 2026-07-05) adds Facebook as a shipping social IdP
    /// alongside Google/Microsoft/Apple. Without this migration, the
    /// classifier's <c>"facebook"</c> output would fail with SQLSTATE 23514
    /// on the first Facebook-federated sign-in in production.</para>
    ///
    /// <para>Down: revert to the M.13 shape. Safe because no <c>'facebook'</c>
    /// rows can exist pre-M.12.</para>
    /// </summary>
    public partial class OpsM12_UserIdentitiesProviderAddFacebook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE identity.user_identities
    DROP CONSTRAINT IF EXISTS ck_user_identities_provider;

ALTER TABLE identity.user_identities
    ADD CONSTRAINT ck_user_identities_provider
    CHECK (provider IN ('entra','google','microsoft','apple','facebook','test'));
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE identity.user_identities
    DROP CONSTRAINT IF EXISTS ck_user_identities_provider;

ALTER TABLE identity.user_identities
    ADD CONSTRAINT ck_user_identities_provider
    CHECK (provider IN ('entra','google','microsoft','apple','test'));
");
        }
    }
}
