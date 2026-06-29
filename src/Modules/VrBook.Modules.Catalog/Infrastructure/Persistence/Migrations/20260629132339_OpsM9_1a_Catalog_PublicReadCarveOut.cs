using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Catalog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Slice OPS.M.9.1 F6b (per <c>docs/OPS_M_9_1_GUEST_RESOLVER_PLAN.md</c>
    /// §1.3 + §3) — public-read RLS carve-out for the platform marketplace.
    ///
    /// <para>OPS.M.9 shipped a closed-world <c>app.tenant_id</c> GUC; the
    /// public marketplace search (`/api/v1/properties`), detail page
    /// (`/api/v1/properties/{slug}`), and amenity/image enumeration all
    /// fail because the anonymous request resolves
    /// <c>current_setting('app.tenant_id', true)</c> to empty and the
    /// tenant-isolation policy denies every row. Audit findings #8 + #9.</para>
    ///
    /// <para>Solution: add a SECOND PERMISSIVE policy on
    /// <c>catalog.properties</c> + <c>catalog.property_images</c> that
    /// allows SELECT when the row is "platform-public" (active +
    /// not soft-deleted; for child tables, via <c>EXISTS</c> against the
    /// parent). The existing <c>rls_catalog_*_tenant_isolation</c> policy
    /// is UNCHANGED — tenant-internal callers keep seeing all their own
    /// rows. Postgres OR-combines PERMISSIVE policies so a row is visible
    /// if EITHER policy returns true.</para>
    ///
    /// <para><b>Writes are NOT affected.</b> The carve-out is
    /// <c>USING</c>-only (<c>FOR SELECT</c>); the existing tenant policy's
    /// <c>WITH CHECK</c> still gates every INSERT/UPDATE/DELETE.</para>
    ///
    /// <para>Idempotent: replay-safe because <c>CREATE POLICY</c> is
    /// additive. Migrator role has <c>BYPASSRLS</c> so the policy creation
    /// is unaffected by current GUC state.</para>
    /// </summary>
    public partial class OpsM9_1a_Catalog_PublicReadCarveOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // catalog.properties — platform-public discovery: active + not deleted.
            migrationBuilder.EnablePublicReadCarveOut(
                schema: "catalog",
                table: "properties",
                usingPredicate: "is_active = true AND deleted_at IS NULL");

            // catalog.property_images — visible iff the parent property is
            // visible. The EXISTS keeps the predicate evaluated row-by-row at
            // the PG layer; no parent column is duplicated onto the child.
            migrationBuilder.EnablePublicReadCarveOut(
                schema: "catalog",
                table: "property_images",
                usingPredicate:
                    "EXISTS (SELECT 1 FROM catalog.properties p " +
                    "WHERE p.\"Id\" = property_images.property_id " +
                    "AND p.is_active = true AND p.deleted_at IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPublicReadCarveOut("catalog", "property_images");
            migrationBuilder.DropPublicReadCarveOut("catalog", "properties");
        }
    }
}
