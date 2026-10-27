using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Access control for the organization screens (SCR-ORG-01 departments, SCR-ORG-02 job positions): adds the
/// <c>organization:read</c> permission, the <c>SYSTEM_ADMIN</c>, <c>BRANCH_MANAGER</c> and <c>DEPT_MANAGER</c> roles, and
/// grants the permission to exactly the roles allowed to open those screens.
/// <list type="bullet">
/// <item><c>SYSTEM_ADMIN</c> and <c>ADMIN</c>: <c>All</c></item>
/// <item><c>BRANCH_MANAGER</c>: <c>Branch</c></item>
/// <item><c>DEPT_MANAGER</c> and <c>MANAGER</c> (the seeded manager role): <c>Dept</c></item>
/// </list>
/// Every other role (employee, verifier, HR, QA, ...) is deliberately not granted anything, so it is rejected.
/// Idempotent and data-only (no schema change); rows that already exist, including the development seed's, are left alone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261027120000_SeedOrganizationReadAccess")]
public partial class SeedOrganizationReadAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role (code, name, description)
            VALUES ('SYSTEM_ADMIN',   'System Administrator', 'Quản trị hệ thống'),
                   ('BRANCH_MANAGER', 'Giám đốc chi nhánh',   'Quản lý chi nhánh và các phòng ban trực thuộc'),
                   ('DEPT_MANAGER',   'Trưởng phòng',         'Quản lý phòng ban của mình')
            ON CONFLICT (code) DO NOTHING;

            INSERT INTO permission (code, name, resource, action)
            VALUES ('ORGANIZATION_READ', 'Xem tổ chức (phòng ban, vị trí công việc)', 'organization', 'read')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('SYSTEM_ADMIN',   'ORGANIZATION_READ', 'All'),
                ('ADMIN',          'ORGANIZATION_READ', 'All'),
                ('BRANCH_MANAGER', 'ORGANIZATION_READ', 'Branch'),
                ('DEPT_MANAGER',   'ORGANIZATION_READ', 'Dept'),
                ('MANAGER',        'ORGANIZATION_READ', 'Dept')
            ) AS m(role_code, permission_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = m.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // role_permission rows go with the permission (ON DELETE CASCADE). The three roles are removed only when no
        // account holds them, so rolling back never strips an assignment that was made after this migration ran.
        migrationBuilder.Sql("""
            DELETE FROM permission WHERE code = 'ORGANIZATION_READ';

            DELETE FROM role r
            WHERE r.code IN ('SYSTEM_ADMIN', 'BRANCH_MANAGER', 'DEPT_MANAGER')
              AND NOT EXISTS (SELECT 1 FROM user_role ur WHERE ur.role_id = r.id);
            """);
    }
}
