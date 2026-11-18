using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddObjectiveWeightAndKpiRequestApproval : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Npgsql would emit a plain ADD VALUE, which fails when the migration runs again after a rollback
        // (PostgreSQL keeps enum labels), so the labels are added idempotently.
        migrationBuilder.Sql("ALTER TYPE kpi_status ADD VALUE IF NOT EXISTS 'PENDING_APPROVAL';");
        migrationBuilder.Sql("ALTER TYPE kpi_status ADD VALUE IF NOT EXISTS 'REJECTED';");

        migrationBuilder.AddColumn<string>(
            name: "reject_reason",
            table: "kpi_definition",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "request_note",
            table: "kpi_definition",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "requested_by",
            table: "kpi_definition",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "submitted_by",
            table: "kpi_definition",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_kpi_definition_requested_by",
            table: "kpi_definition",
            column: "requested_by");

        migrationBuilder.CreateIndex(
            name: "IX_kpi_definition_submitted_by",
            table: "kpi_definition",
            column: "submitted_by");

        migrationBuilder.AddForeignKey(
            name: "fk_kpi_definition_requested_by",
            table: "kpi_definition",
            column: "requested_by",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_kpi_definition_submitted_by",
            table: "kpi_definition",
            column: "submitted_by",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        // The Branch Manager owns business objectives, requests missing KPIs and approves them; the Department Manager
        // reads the objectives of the department. The Admin passes every permission policy already.
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action) VALUES
                ('OBJECTIVE_READ',   'Xem mục tiêu kinh doanh',     'objective', 'read'),
                ('OBJECTIVE_MANAGE', 'Quản lý mục tiêu kinh doanh', 'objective', 'manage'),
                ('KPI_REQUEST',      'Yêu cầu bổ sung KPI',         'kpi',       'request'),
                ('KPI_APPROVE',      'Duyệt hoặc từ chối KPI',      'kpi',       'approve')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('MANAGER',        'OBJECTIVE_READ',   'Dept'),
                ('DEPT_MANAGER',   'OBJECTIVE_READ',   'Dept'),
                ('BRANCH_MANAGER', 'OBJECTIVE_READ',   'Branch'),
                ('BRANCH_MANAGER', 'OBJECTIVE_MANAGE', 'All'),
                ('BRANCH_MANAGER', 'KPI_REQUEST',      'All'),
                ('BRANCH_MANAGER', 'KPI_APPROVE',      'All')
            ) AS m(role_code, permission_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = m.permission_code
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (
                SELECT id FROM permission WHERE code IN ('OBJECTIVE_READ', 'OBJECTIVE_MANAGE', 'KPI_REQUEST', 'KPI_APPROVE'));
            DELETE FROM permission WHERE code IN ('OBJECTIVE_READ', 'OBJECTIVE_MANAGE', 'KPI_REQUEST', 'KPI_APPROVE');
            """);

        // PostgreSQL cannot drop enum labels: PENDING_APPROVAL and REJECTED stay in kpi_status.
        // Move such KPIs back to DRAFT so no row keeps a status the previous version does not know about.
        migrationBuilder.Sql("UPDATE kpi_definition SET status = 'DRAFT' WHERE status IN ('PENDING_APPROVAL', 'REJECTED');");

        migrationBuilder.DropForeignKey(
            name: "fk_kpi_definition_submitted_by",
            table: "kpi_definition");

        migrationBuilder.DropForeignKey(
            name: "fk_kpi_definition_requested_by",
            table: "kpi_definition");

        migrationBuilder.DropIndex(
            name: "IX_kpi_definition_submitted_by",
            table: "kpi_definition");

        migrationBuilder.DropIndex(
            name: "IX_kpi_definition_requested_by",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "submitted_by",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "requested_by",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "request_note",
            table: "kpi_definition");

        migrationBuilder.DropColumn(
            name: "reject_reason",
            table: "kpi_definition");

    }
}
