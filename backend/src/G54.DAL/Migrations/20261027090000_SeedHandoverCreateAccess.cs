using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds the <c>handover:create</c> and <c>handover:set_share</c> permissions and grants them to the standard roles that exist.
/// Idempotent and data-only; conflicts with the development seed (same codes) are ignored.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261027090000_SeedHandoverCreateAccess")]
public partial class SeedHandoverCreateAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('HANDOVER_CREATE', 'Yêu cầu bàn giao', 'handover', 'create'),
                   ('HANDOVER_SET_SHARE', 'Đặt tỷ lệ chia điểm khi bàn giao', 'handover', 'set_share')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('ADMIN', 'HANDOVER_CREATE', 'All'), ('MANAGER', 'HANDOVER_CREATE', 'Own'), ('EMPLOYEE', 'HANDOVER_CREATE', 'Own'),
                ('ADMIN', 'HANDOVER_SET_SHARE', 'All'), ('MANAGER', 'HANDOVER_SET_SHARE', 'Own')
            ) AS m(role_code, permission_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = m.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM permission WHERE code IN ('HANDOVER_CREATE', 'HANDOVER_SET_SHARE');");
    }
}
