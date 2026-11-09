using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Lets the <c>BRANCH_MANAGER</c> role see and decide the appeals of the whole branch: grants <c>verification:decide</c>
/// at <c>Branch</c> scope (the appeal list, the appeal detail and the evidence of an appeal all follow that grant).
/// Idempotent and data-only: a row that already exists, including one an administrator adjusted, is left alone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261104090000_GrantAppealDecisionToBranchManager")]
public partial class GrantAppealDecisionToBranchManager : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'Branch'
            FROM role r
            JOIN permission p ON p.code = 'VERIFICATION_DECIDE'
            WHERE r.code = 'BRANCH_MANAGER'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Only the grant this migration adds; an administrator's own scope for the same pair is not removed.
        migrationBuilder.Sql("""
            DELETE FROM role_permission rp
            USING role r, permission p
            WHERE rp.role_id = r.id
              AND rp.permission_id = p.id
              AND r.code = 'BRANCH_MANAGER'
              AND p.code = 'VERIFICATION_DECIDE'
              AND rp.scope = 'Branch';
            """);
    }
}
