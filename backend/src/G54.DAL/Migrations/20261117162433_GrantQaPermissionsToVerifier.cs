using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// The verifier is the QA of the system (the sidebar maps <c>VERIFIER</c> to the QA workspace) but only held verification and
/// read grants, so QA screens answered 403. It now holds everything the <c>QA</c> role holds (quality flags, QA samples, risk
/// events, transaction import, reconciliation) at the same scope. It keeps its own verification grants.
/// </summary>
public partial class GrantQaPermissionsToVerifier : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT v.id, rp.permission_id, rp.scope
            FROM role v
            JOIN role q ON q.code = 'QA'
            JOIN role_permission rp ON rp.role_id = q.id
            WHERE v.code = 'VERIFIER'
            ON CONFLICT DO NOTHING;

            -- An earlier version of the reconciliation grant gave the verifier only its department.
            UPDATE role_permission
            SET scope = 'All'
            WHERE role_id = (SELECT id FROM role WHERE code = 'VERIFIER')
              AND permission_id = (SELECT id FROM permission WHERE code = 'RECONCILIATION_READ');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE role_id = (SELECT id FROM role WHERE code = 'VERIFIER')
              AND permission_id IN (
                  SELECT id FROM permission
                  WHERE resource IN ('quality_flag', 'qa_sample', 'risk_event', 'transaction-import', 'reconciliation'));
            """);
    }
}
