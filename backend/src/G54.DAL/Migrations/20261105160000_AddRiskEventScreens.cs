using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddRiskEventScreens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "risk_event_code_seq");

        migrationBuilder.AddColumn<long>(
            name: "code_no",
            table: "risk_event",
            type: "bigint",
            nullable: false,
            defaultValueSql: "nextval('risk_event_code_seq')");

        migrationBuilder.AddColumn<decimal>(
            name: "penalty_points",
            table: "risk_event",
            type: "numeric(8,2)",
            precision: 8,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "recorded_at",
            table: "risk_event",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        migrationBuilder.AddColumn<string>(
            name: "reversal_reason",
            table: "risk_event",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "reversed_at",
            table: "risk_event",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "reversed_by",
            table: "risk_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "work_id",
            table: "risk_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_risk_event_occurred_at",
            table: "risk_event",
            column: "occurred_at");

        migrationBuilder.CreateIndex(
            name: "IX_risk_event_reversed_by",
            table: "risk_event",
            column: "reversed_by");

        migrationBuilder.CreateIndex(
            name: "IX_risk_event_work_id",
            table: "risk_event",
            column: "work_id");

        migrationBuilder.CreateIndex(
            name: "uq_risk_event_code_no",
            table: "risk_event",
            column: "code_no",
            unique: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_risk_event_penalty_points",
            table: "risk_event",
            sql: "penalty_points IS NULL OR (penalty_points > 0 AND penalty_points <= 100)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_risk_event_reversal",
            table: "risk_event",
            sql: "(reversed_at IS NULL) = (reversed_by IS NULL)");

        migrationBuilder.AddForeignKey(
            name: "fk_risk_event_reversed_by",
            table: "risk_event",
            column: "reversed_by",
            principalTable: "employee",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_risk_event_work",
            table: "risk_event",
            column: "work_id",
            principalTable: "work",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        // Risk events become a QA screen: read for QA, administrators and the managers of the people concerned (within their
        // scope); recording and reversing only for QA and administrators.
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('RISK_EVENT_CREATE', 'Ghi sự kiện rủi ro', 'risk_event', 'create'),
                   ('RISK_EVENT_READ', 'Xem sự kiện rủi ro', 'risk_event', 'read'),
                   ('RISK_EVENT_CONFIRM', 'Xác nhận sự kiện rủi ro', 'risk_event', 'confirm'),
                   ('RISK_EVENT_DISMISS', 'Bác bỏ sự kiện rủi ro', 'risk_event', 'dismiss'),
                   ('RISK_EVENT_RESOLVE', 'Giải quyết sự kiện rủi ro', 'risk_event', 'resolve'),
                   ('RISK_EVENT_REVERSE', 'Gỡ điểm phạt sự kiện rủi ro', 'risk_event', 'reverse')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM role r
            JOIN permission p ON p.resource = 'risk_event'
            JOIN (VALUES
                ('QA', 'All'), ('ADMIN', 'All'), ('SYSTEM_ADMIN', 'All')
            ) AS m(role_code, scope) ON m.role_code = r.code
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, m.scope
            FROM (VALUES
                ('MANAGER', 'Dept'), ('DEPT_MANAGER', 'Dept'), ('BRANCH_MANAGER', 'Branch')
            ) AS m(role_code, scope)
            JOIN role r ON r.code = m.role_code
            JOIN permission p ON p.code = 'RISK_EVENT_READ'
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE resource = 'risk_event');
            DELETE FROM permission WHERE resource = 'risk_event';
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_risk_event_reversed_by",
            table: "risk_event");

        migrationBuilder.DropForeignKey(
            name: "fk_risk_event_work",
            table: "risk_event");

        migrationBuilder.DropIndex(
            name: "ix_risk_event_occurred_at",
            table: "risk_event");

        migrationBuilder.DropIndex(
            name: "IX_risk_event_reversed_by",
            table: "risk_event");

        migrationBuilder.DropIndex(
            name: "IX_risk_event_work_id",
            table: "risk_event");

        migrationBuilder.DropIndex(
            name: "uq_risk_event_code_no",
            table: "risk_event");

        migrationBuilder.DropCheckConstraint(
            name: "ck_risk_event_penalty_points",
            table: "risk_event");

        migrationBuilder.DropCheckConstraint(
            name: "ck_risk_event_reversal",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "code_no",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "penalty_points",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "recorded_at",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "reversal_reason",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "reversed_at",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "reversed_by",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "work_id",
            table: "risk_event");

        migrationBuilder.DropSequence(
            name: "risk_event_code_seq");
    }
}
