using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Makes the roles added by <c>SeedOrganizationReadAccess</c> usable on their own, so one account needs exactly one role:
/// <list type="bullet">
/// <item><c>DEPT_MANAGER</c> receives every permission the seeded <c>MANAGER</c> role holds, with the same scopes.</item>
/// <item><c>BRANCH_MANAGER</c> receives the same permissions with department-level scopes widened to <c>Branch</c>
/// (<c>Own</c> and <c>All</c> grants are kept as they are).</item>
/// <item><c>SYSTEM_ADMIN</c> receives every permission the seeded <c>ADMIN</c> role holds (never the development-only
/// <c>FULL_ACCESS</c> wildcard).</item>
/// </list>
/// The grants are copied from the source role at the time the migration runs, so a role that does not exist yet (an empty
/// database before the development seed) simply copies nothing; the development seed lists the same grants explicitly.
/// Idempotent and data-only: existing rows, including an administrator's adjusted scopes, are left alone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261027140000_GrantWorkAccessToOrganizationRoles")]
public partial class GrantWorkAccessToOrganizationRoles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT target.id, rp.permission_id, rp.scope
            FROM role_permission rp
            JOIN role source ON source.id = rp.role_id AND source.code = 'MANAGER'
            JOIN role target ON target.code = 'DEPT_MANAGER'
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT target.id,
                   rp.permission_id,
                   CASE WHEN lower(rp.scope) IN ('dept', 'department') THEN 'Branch' ELSE rp.scope END
            FROM role_permission rp
            JOIN role source ON source.id = rp.role_id AND source.code = 'MANAGER'
            JOIN role target ON target.code = 'BRANCH_MANAGER'
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT target.id, rp.permission_id, rp.scope
            FROM role_permission rp
            JOIN permission p ON p.id = rp.permission_id AND p.code <> 'FULL_ACCESS'
            JOIN role source ON source.id = rp.role_id AND source.code = 'ADMIN'
            JOIN role target ON target.code = 'SYSTEM_ADMIN'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep organization:read, which belongs to the previous migration.
        migrationBuilder.Sql("""
            DELETE FROM role_permission rp
            USING role r, permission p
            WHERE rp.role_id = r.id
              AND rp.permission_id = p.id
              AND r.code IN ('DEPT_MANAGER', 'BRANCH_MANAGER', 'SYSTEM_ADMIN')
              AND p.code <> 'ORGANIZATION_READ';
            """);
    }
}
