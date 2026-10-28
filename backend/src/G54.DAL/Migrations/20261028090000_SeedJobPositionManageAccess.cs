using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds the <c>job-position:manage</c> permission (assign or remove a work type of a job position, SCR-ORG-02) and grants it
/// to the managers only:
/// <list type="bullet">
/// <item><c>BRANCH_MANAGER</c>: <c>Branch</c></item>
/// <item><c>DEPT_MANAGER</c> and <c>MANAGER</c> (the seeded manager role): <c>Dept</c></item>
/// </list>
/// <c>SYSTEM_ADMIN</c> administers the system, not the organization's job positions, so it is deliberately not granted.
/// Idempotent and data-only (no schema change); rows that already exist, including the development seed's, are left alone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261028090000_SeedJobPositionManageAccess")]
public partial class SeedJobPositionManageAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('JOB_POSITION_MANAGE', 'Quản lý loại công việc của vị trí công việc', 'job-position', 'manage')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('BRANCH_MANAGER', 'JOB_POSITION_MANAGE', 'Branch'),
                ('DEPT_MANAGER',   'JOB_POSITION_MANAGE', 'Dept'),
                ('MANAGER',        'JOB_POSITION_MANAGE', 'Dept')
            ) AS m(role_code, permission_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = m.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'JOB_POSITION_MANAGE');
            DELETE FROM permission WHERE code = 'JOB_POSITION_MANAGE';
            """);
    }
}
