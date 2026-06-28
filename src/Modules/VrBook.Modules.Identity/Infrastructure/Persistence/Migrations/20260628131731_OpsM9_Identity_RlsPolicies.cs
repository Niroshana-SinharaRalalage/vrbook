using Microsoft.EntityFrameworkCore.Migrations;
using VrBook.Infrastructure.Persistence;

#nullable disable

namespace VrBook.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM9_Identity_RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OPS.M.9 §3.1 row 19 + §3.4 (D9 naming) + §4.12 (D12 nullable shape).
            // identity.audit_log carries nullable tenant_id (M.4 audit log
            // captures cross-tenant rejections where TenantId is null) — use
            // the nullable-aware policy variant.
            migrationBuilder.EnableRlsTenantIsolation("identity", "audit_log", nullable: true);

            // OPS.M.9 §3.2 carve-outs: identity.users, identity.tenants,
            // identity.tenant_memberships are intentionally NOT under RLS.
            // The M.10 cross-tenant isolation test pack verifies the app
            // layer (M.4 TenantAuthorizationBehavior + M.8 PlatformAdmin
            // bypass) blocks cross-tenant access without the DB-level gate.

            // OPS.M.9 §2 — grant BYPASSRLS to the migrator role so future
            // migrations + the M.10 test seed paths can write reference
            // rows without being blocked by the policies just installed.
            // The app role (vrbook) MUST NOT have BYPASSRLS — that's
            // explicit non-grant; M.10 tests for it.
            // Idempotent: ALTER ROLE is a no-op when the attribute matches.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'vrbook_migrator') THEN
                        ALTER ROLE vrbook_migrator BYPASSRLS;
                    END IF;
                END
                $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropRlsTenantIsolation("identity", "audit_log");
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'vrbook_migrator') THEN
                        ALTER ROLE vrbook_migrator NOBYPASSRLS;
                    END IF;
                END
                $$;
            ");
        }
    }
}
