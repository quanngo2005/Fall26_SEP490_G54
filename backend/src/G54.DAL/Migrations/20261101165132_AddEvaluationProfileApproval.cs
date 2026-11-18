using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddEvaluationProfileApproval : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Npgsql would emit a plain ADD VALUE, which fails when the migration runs again after a rollback
        // (PostgreSQL keeps enum labels), so the labels are added idempotently.
        migrationBuilder.Sql("ALTER TYPE evaluation_profile_status ADD VALUE IF NOT EXISTS 'PENDING_APPROVAL';");
        migrationBuilder.Sql("ALTER TYPE evaluation_profile_status ADD VALUE IF NOT EXISTS 'REJECTED';");

        migrationBuilder.AddColumn<string>(
            name: "reject_reason",
            table: "evaluation_profile",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        // The Branch Manager approves what the Admin writes. The Admin passes every permission policy already.
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('EVALUATION_PROFILE_APPROVE', 'Duyệt hoặc từ chối hồ sơ đánh giá', 'evaluation-profile', 'approve')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r
            JOIN permission p ON p.code = 'EVALUATION_PROFILE_APPROVE'
            WHERE r.code = 'BRANCH_MANAGER'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE code = 'EVALUATION_PROFILE_APPROVE');
            DELETE FROM permission WHERE code = 'EVALUATION_PROFILE_APPROVE';
            """);

        // PostgreSQL cannot drop enum labels: PENDING_APPROVAL and REJECTED stay in evaluation_profile_status.
        // Move such profiles back to DRAFT so no row keeps a status the previous version does not know about.
        migrationBuilder.Sql("UPDATE evaluation_profile SET status = 'DRAFT' WHERE status IN ('PENDING_APPROVAL', 'REJECTED');");

        migrationBuilder.DropColumn(
            name: "reject_reason",
            table: "evaluation_profile");
    }
}
