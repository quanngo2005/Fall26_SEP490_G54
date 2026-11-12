using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds the RANKING_RECORD_READ permission (HR ranking records, docs/api/HR_RANKING_RECORD_CONTRACT.md) and grants it with
/// scope All to HR_OFFICER, BRANCH_MANAGER, ADMIN and SYSTEM_ADMIN. Idempotent and data-only.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261111000000_GrantRankingRecordReadToHr")]
public partial class GrantRankingRecordReadToHr : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('RANKING_RECORD_READ', 'Xem bản ghi xếp hạng', 'ranking_record', 'read')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r, permission p
            WHERE r.code IN ('HR_OFFICER', 'BRANCH_MANAGER', 'ADMIN', 'SYSTEM_ADMIN')
              AND p.code = 'RANKING_RECORD_READ'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'RANKING_RECORD_READ');
            DELETE FROM permission WHERE code = 'RANKING_RECORD_READ';
            """);
    }
}
