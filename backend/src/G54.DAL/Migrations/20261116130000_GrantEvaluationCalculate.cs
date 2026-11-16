using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Adds the permission EVALUATION_CALCULATE (<c>evaluation:calculate</c>) used by
/// <c>POST /performance-evaluations/{id}/calculate</c>. It was never seeded, so only the development FULL_ACCESS wildcard
/// passed and every real role got 403. Grants it like EVALUATION_SUBMIT: ADMIN and SYSTEM_ADMIN with scope All, MANAGER
/// and DEPT_MANAGER with scope Department, BRANCH_MANAGER with scope Branch. Roles that do not exist yet (fresh database,
/// the seed creates them after the migrations) get it from seed_dev_data.sql. Idempotent and data-only.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261116130000_GrantEvaluationCalculate")]
public partial class GrantEvaluationCalculate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO permission (id, code, name, resource, action)
            VALUES ('5eed0000-0000-0000-0009-00000000002f', 'EVALUATION_CALCULATE', 'Tính điểm KPI', 'evaluation', 'calculate')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, g.scope
            FROM (VALUES
                ('ADMIN', 'All'), ('SYSTEM_ADMIN', 'All'),
                ('MANAGER', 'Department'), ('DEPT_MANAGER', 'Department'), ('BRANCH_MANAGER', 'Branch')
            ) AS g(role_code, scope)
            JOIN role r ON r.code = g.role_code
            JOIN permission p ON p.code = 'EVALUATION_CALCULATE'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission
            WHERE permission_id IN (SELECT id FROM permission WHERE code = 'EVALUATION_CALCULATE');
            DELETE FROM permission WHERE code = 'EVALUATION_CALCULATE';
            """);
    }
}
