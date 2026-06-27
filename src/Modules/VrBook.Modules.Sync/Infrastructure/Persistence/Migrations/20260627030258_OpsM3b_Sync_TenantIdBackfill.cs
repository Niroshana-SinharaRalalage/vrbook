using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Sync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpsM3b_Sync_TenantIdBackfill : Migration
    {
        private const string DefaultTenantId = "00000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE sync.channel_feeds
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE sync.external_reservations
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE sync.sync_conflicts
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");

            migrationBuilder.Sql($@"
                UPDATE sync.sync_runs
                   SET tenant_id = '{DefaultTenantId}'::uuid,
                       updated_at = NOW()
                 WHERE tenant_id IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE sync.sync_runs SET tenant_id = NULL WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
            migrationBuilder.Sql($@"
                UPDATE sync.sync_conflicts SET tenant_id = NULL WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
            migrationBuilder.Sql($@"
                UPDATE sync.external_reservations SET tenant_id = NULL WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
            migrationBuilder.Sql($@"
                UPDATE sync.channel_feeds SET tenant_id = NULL WHERE tenant_id = '{DefaultTenantId}'::uuid;
            ");
        }
    }
}
