using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// SRS UC89 / UC90: the department manager may flag a quality issue on an approved result and send it back for rework,
/// for the staff of their own department subtree (<c>Dept</c> scope, enforced again by QualityAssuranceService).
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
            SELECT r.id, p.id, 'Dept'
            FROM role r
            JOIN permission p ON p.code IN ('QUALITY_FLAG_CREATE', 'QUALITY_FLAG_REWORK')
            WHERE r.code = 'DEPT_MANAGER'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE role_id IN (SELECT id FROM role WHERE code = 'DEPT_MANAGER')
              AND permission_id IN (SELECT id FROM permission WHERE code IN ('QUALITY_FLAG_CREATE', 'QUALITY_FLAG_REWORK'));
            """);
    }
}
