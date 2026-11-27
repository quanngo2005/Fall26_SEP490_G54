using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// SRS UC89 / UC90: the department manager may flag a quality issue on an approved result and send it back for rework,
/// for the staff of their own department subtree (<c>Dept</c> scope, enforced again by QualityAssuranceService). The
/// branch manager keeps the manager's permissions widened to <c>Branch</c>, as for every other manager grant.
/// Reading the QA queue, dismissing flags and deciding samples stay with QA. Idempotent and data-only.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261126090000_GrantQualityFlagToDeptManager")]
public partial class GrantQualityFlagToDeptManager : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES ('DEPT_MANAGER', 'Dept'), ('BRANCH_MANAGER', 'Branch')) AS m(role_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code IN ('QUALITY_FLAG_CREATE', 'QUALITY_FLAG_REWORK')
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE role_id IN (SELECT id FROM role WHERE code IN ('DEPT_MANAGER', 'BRANCH_MANAGER'))
              AND permission_id IN (SELECT id FROM permission WHERE code IN ('QUALITY_FLAG_CREATE', 'QUALITY_FLAG_REWORK'));
            """);
    }
}
