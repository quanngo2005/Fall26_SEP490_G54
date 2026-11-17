using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds <c>reconciliation:read</c> for the reconciliation screens (SCR-REV-03): QA and administrators read every department,
/// a department manager their department, a branch manager their branch. The verifier is the QA of the system and gets it from
/// <c>GrantQaPermissionsToVerifier</c>.
/// </summary>
public partial class GrantReconciliationRead : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('RECONCILIATION_READ', 'Xem đối soát giao dịch', 'reconciliation', 'read')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('QA', 'All'), ('ADMIN', 'All'), ('SYSTEM_ADMIN', 'All'),
                ('BRANCH_MANAGER', 'Branch'),
                ('MANAGER', 'Dept'), ('DEPT_MANAGER', 'Dept')
            ) AS m(role_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = 'RECONCILIATION_READ'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'RECONCILIATION_READ');
            DELETE FROM permission WHERE code = 'RECONCILIATION_READ';
            """);
    }
}
