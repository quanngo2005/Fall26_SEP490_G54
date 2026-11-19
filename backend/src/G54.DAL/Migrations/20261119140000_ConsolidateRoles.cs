using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Screen Authorization Matrix test: the role list had grown aliases for the same actor (ADMIN / SYSTEM_ADMIN, MANAGER /
/// DEPT_MANAGER, QA / VERIFIER). Only six roles remain: EMPLOYEE, DEPT_MANAGER, BRANCH_MANAGER, HR_OFFICER, VERIFIER and
/// SYSTEM_ADMIN. The removed roles hand their accounts and grants to the surviving one (ADMIN -> SYSTEM_ADMIN, MANAGER ->
/// DEPT_MANAGER, QA -> VERIFIER) and are deleted. <c>SYSTEM_ADMIN</c> is the only role that skips permission checks.
///
/// Grants that were missing for the surviving roles, found by the matrix test, are added here as well:
/// the HR officer reads employees, the organization and evaluation results (every HR screen answered 403); the Branch Manager
/// holds <c>evaluation:finalize</c> (the contract gives it pre-close, publish and lock, and it opens SCR-EVL-03); the verifier
/// reads its own evaluation results (the app shell loads <c>GET /evaluations/periods</c> on every page).
/// </summary>
public partial class ConsolidateRoles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action) VALUES
                ('ROLE_UPDATE', 'Quản lý tài khoản và vai trò', 'role', 'update'),
                ('WORK_TYPE_MANAGE', 'Quản lý loại công việc', 'work-type', 'manage')
            ON CONFLICT DO NOTHING;

            -- A removed role whose survivor does not exist yet is renamed instead of merged.
            UPDATE role SET code = 'SYSTEM_ADMIN', name = 'System Administrator'
            WHERE code = 'ADMIN' AND NOT EXISTS (SELECT 1 FROM role WHERE code = 'SYSTEM_ADMIN');
            UPDATE role SET code = 'DEPT_MANAGER', name = 'Trưởng phòng'
            WHERE code = 'MANAGER' AND NOT EXISTS (SELECT 1 FROM role WHERE code = 'DEPT_MANAGER');
            UPDATE role SET code = 'VERIFIER', name = 'Người duyệt'
            WHERE code = 'QA' AND NOT EXISTS (SELECT 1 FROM role WHERE code = 'VERIFIER');

            -- Merge grants, then accounts, then drop the removed role.
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT n.id, rp.permission_id, rp.scope
            FROM (VALUES ('ADMIN', 'SYSTEM_ADMIN'), ('MANAGER', 'DEPT_MANAGER'), ('QA', 'VERIFIER')) AS m(old_code, new_code)
            JOIN role o ON o.code = m.old_code
            JOIN role n ON n.code = m.new_code
            JOIN role_permission rp ON rp.role_id = o.id
            JOIN permission p ON p.id = rp.permission_id AND p.code <> 'FULL_ACCESS'
            ON CONFLICT DO NOTHING;

            UPDATE user_role ur SET role_id = n.id
            FROM (VALUES ('ADMIN', 'SYSTEM_ADMIN'), ('MANAGER', 'DEPT_MANAGER'), ('QA', 'VERIFIER')) AS m(old_code, new_code),
                 role o, role n
            WHERE o.code = m.old_code AND n.code = m.new_code AND ur.role_id = o.id
              AND NOT EXISTS (SELECT 1 FROM user_role x WHERE x.account_id = ur.account_id AND x.role_id = n.id);

            DELETE FROM user_role WHERE role_id IN (SELECT id FROM role WHERE code IN ('ADMIN', 'MANAGER', 'QA'));
            DELETE FROM role_permission WHERE role_id IN (SELECT id FROM role WHERE code IN ('ADMIN', 'MANAGER', 'QA'));
            DELETE FROM role WHERE code IN ('ADMIN', 'MANAGER', 'QA');

            -- Grants the matrix test found missing.
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, g.scope
            FROM (VALUES
                ('HR_OFFICER', 'EMPLOYEE_READ', 'All'),
                ('HR_OFFICER', 'ORGANIZATION_READ', 'All'),
                ('HR_OFFICER', 'EVALUATION_READ', 'All'),
                ('BRANCH_MANAGER', 'EVALUATION_FINALIZE', 'Branch'),
                ('VERIFIER', 'EVALUATION_READ', 'Own'),
                ('SYSTEM_ADMIN', 'ROLE_UPDATE', 'All'),
                ('SYSTEM_ADMIN', 'WORK_TYPE_MANAGE', 'All')
            ) AS g(role_code, permission_code, scope)
            JOIN role r ON r.code = g.role_code
            JOIN permission p ON p.code = g.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The merged roles come back as empty aliases carrying a copy of the survivor's grants, so that the older migrations
        // (which copy grants from MANAGER / ADMIN / QA) can be re-applied. Accounts are not moved back: they cannot be told apart.
        migrationBuilder.Sql("""
            INSERT INTO role (code, name) VALUES ('ADMIN', 'Administrator'), ('MANAGER', 'Quản lý'), ('QA', 'Kiểm soát chất lượng')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT o.id, rp.permission_id, rp.scope
            FROM (VALUES ('ADMIN', 'SYSTEM_ADMIN'), ('MANAGER', 'DEPT_MANAGER'), ('QA', 'VERIFIER')) AS m(old_code, new_code)
            JOIN role o ON o.code = m.old_code
            JOIN role n ON n.code = m.new_code
            JOIN role_permission rp ON rp.role_id = n.id
            ON CONFLICT DO NOTHING;

            DELETE FROM role_permission rp
            USING role r, permission p
            WHERE rp.role_id = r.id AND rp.permission_id = p.id
              AND ((r.code = 'HR_OFFICER' AND p.code IN ('EMPLOYEE_READ', 'ORGANIZATION_READ', 'EVALUATION_READ'))
                OR (r.code = 'BRANCH_MANAGER' AND p.code = 'EVALUATION_FINALIZE')
                OR (r.code = 'VERIFIER' AND p.code = 'EVALUATION_READ')
                OR (r.code = 'SYSTEM_ADMIN' AND p.code IN ('ROLE_UPDATE', 'WORK_TYPE_MANAGE')));
            DELETE FROM permission WHERE code IN ('ROLE_UPDATE', 'WORK_TYPE_MANAGE');
            """);
    }
}
