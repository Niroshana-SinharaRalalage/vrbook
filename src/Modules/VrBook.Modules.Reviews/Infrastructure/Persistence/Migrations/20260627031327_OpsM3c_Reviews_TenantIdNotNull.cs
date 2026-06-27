using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Reviews_TenantIdNotNull : Migration
    {
        // OPS.M.3c — raw SQL ALTER COLUMN SET NOT NULL; no permanent DEFAULT.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reviews.reviews ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE reviews.reviews ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
