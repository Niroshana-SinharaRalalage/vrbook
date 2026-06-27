using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3b_Reviews_TenantIdBackfill : Migration
    {
        // OPS.M.3b — backfill reviews.reviews to the default tenant.
        // Idempotent via WHERE tenant_id IS NULL.

        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE reviews.reviews
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE reviews.reviews
                   SET tenant_id = NULL
                 WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
