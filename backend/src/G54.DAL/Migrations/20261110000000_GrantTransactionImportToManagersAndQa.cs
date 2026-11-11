using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Grants TRANSACTION_IMPORT_MANAGE with scope All to SYSTEM_ADMIN, DEPT_MANAGER, BRANCH_MANAGER and QA.
/// Idempotent and data-only.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261110000000_GrantTransactionImportToManagersAndQa")]
public partial class GrantTransactionImportToManagersAndQa : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r, permission p
            WHERE r.code IN ('SYSTEM_ADMIN', 'DEPT_MANAGER', 'BRANCH_MANAGER', 'QA')
              AND p.code = 'TRANSACTION_IMPORT_MANAGE'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE role_id IN (SELECT id FROM role WHERE code IN ('SYSTEM_ADMIN', 'DEPT_MANAGER', 'BRANCH_MANAGER', 'QA'))
              AND permission_id IN (SELECT id FROM permission WHERE code = 'TRANSACTION_IMPORT_MANAGE')
              AND scope = 'All';
            """);
    }
}
