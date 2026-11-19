using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Only a manager hands a work over (the assignee's direct manager transfers it outright), so the <c>EMPLOYEE</c> role
/// loses <c>handover:create</c>. It keeps <c>handover:respond</c> to read its own handovers. Data-only.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261119090000_RevokeEmployeeHandoverCreate")]
public partial class RevokeEmployeeHandoverCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE role_id = (SELECT id FROM role WHERE code = 'EMPLOYEE')
              AND permission_id = (SELECT id FROM permission WHERE code = 'HANDOVER_CREATE');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'Own'
            FROM role r
            JOIN permission p ON p.code = 'HANDOVER_CREATE'
            WHERE r.code = 'EMPLOYEE'
            ON CONFLICT DO NOTHING;
            """);
    }
}
