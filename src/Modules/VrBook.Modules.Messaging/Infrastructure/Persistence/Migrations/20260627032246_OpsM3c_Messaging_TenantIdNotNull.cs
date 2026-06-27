using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Messaging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3c_Messaging_TenantIdNotNull : Migration
    {
        // OPS.M.3c — raw SQL ALTER COLUMN SET NOT NULL; no permanent DEFAULT.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE messaging.threads ALTER COLUMN tenant_id SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE messaging.messages ALTER COLUMN tenant_id SET NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE messaging.messages ALTER COLUMN tenant_id DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE messaging.threads ALTER COLUMN tenant_id DROP NOT NULL;");
        }
    }
}
