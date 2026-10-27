using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds the <c>handover:manage</c> permission (a manager approves, reassigns or rejects a pending handover) and
/// grants it to the standard roles that exist. Idempotent and data-only; conflicts with the development seed
/// (same code) are ignored.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261027110000_SeedHandoverManageAccess")]
public partial class SeedHandoverManageAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('HANDOVER_MANAGE', 'Quản lý duyệt, đổi người nhận hoặc từ chối bàn giao', 'handover', 'manage')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES ('ADMIN', 'All'), ('MANAGER', 'Own')) AS m(role_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = 'HANDOVER_MANAGE'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'HANDOVER_MANAGE');
            DELETE FROM permission WHERE code = 'HANDOVER_MANAGE';
            """);
    }
}
