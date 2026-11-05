using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <inheritdoc />
public partial class AddQualityAssurance : Migration
{
    private static readonly string[] SampleStatusColumns = ["status", "sampled_at"];
    private static readonly string[] FlagStatusColumns = ["status", "created_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "qa_sample",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                result_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                submitter_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                sampled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reviewed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_qa_sample", x => x.id);
                table.ForeignKey(
                    name: "fk_qa_sample_reviewer",
                    column: x => x.reviewed_by_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_qa_sample_submission",
                    column: x => x.result_submission_id,
                    principalTable: "result_submission",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_qa_sample_submitter",
                    column: x => x.submitter_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "quality_flag",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                result_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                flagged_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                qa_sample_id = table.Column<Guid>(type: "uuid", nullable: true),
                severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                rework_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                rework_requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quality_flag", x => x.id);
                table.ForeignKey(
                    name: "fk_quality_flag_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_quality_flag_flagged_by",
                    column: x => x.flagged_by_employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_quality_flag_sample",
                    column: x => x.qa_sample_id,
                    principalTable: "qa_sample",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_quality_flag_submission",
                    column: x => x.result_submission_id,
                    principalTable: "result_submission",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_qa_sample_reviewed_by_employee_id",
            table: "qa_sample",
            column: "reviewed_by_employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_qa_sample_status",
            table: "qa_sample",
            columns: SampleStatusColumns);

        migrationBuilder.CreateIndex(
            name: "IX_qa_sample_submitter_employee_id",
            table: "qa_sample",
            column: "submitter_employee_id");

        migrationBuilder.CreateIndex(
            name: "uq_qa_sample_submission",
            table: "qa_sample",
            column: "result_submission_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_quality_flag_employee",
            table: "quality_flag",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "IX_quality_flag_flagged_by_employee_id",
            table: "quality_flag",
            column: "flagged_by_employee_id");

        migrationBuilder.CreateIndex(
            name: "ix_quality_flag_status",
            table: "quality_flag",
            columns: FlagStatusColumns);

        migrationBuilder.CreateIndex(
            name: "ix_quality_flag_submission",
            table: "quality_flag",
            column: "result_submission_id");

        migrationBuilder.CreateIndex(
            name: "uq_quality_flag_sample",
            table: "quality_flag",
            column: "qa_sample_id",
            unique: true,
            filter: "qa_sample_id IS NOT NULL");

        // QA quality control (BR-32): permissions, and the QA role itself (the seed only had it as a sidebar role).
        migrationBuilder.Sql("""
            INSERT INTO role (code, name, description)
            VALUES ('QA', 'Kiểm soát chất lượng', 'Gắn cờ chất lượng, yêu cầu làm lại và kiểm tra ngẫu nhiên 5% kết quả đã duyệt')
            ON CONFLICT DO NOTHING;

            INSERT INTO permission (code, name, resource, action)
            VALUES ('QUALITY_FLAG_CREATE', 'Gắn cờ chất lượng kết quả đã duyệt', 'quality_flag', 'create'),
                   ('QUALITY_FLAG_READ', 'Xem cờ chất lượng', 'quality_flag', 'read'),
                   ('QUALITY_FLAG_REWORK', 'Yêu cầu nhân viên làm lại', 'quality_flag', 'rework'),
                   ('QUALITY_FLAG_DISMISS', 'Bỏ qua cờ chất lượng', 'quality_flag', 'dismiss'),
                   ('QA_SAMPLE_READ', 'Xem mẫu kiểm tra QA 5%', 'qa_sample', 'read'),
                   ('QA_SAMPLE_DECIDE', 'Quyết định kết quả kiểm tra mẫu QA', 'qa_sample', 'decide')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r
            JOIN permission p ON p.resource IN ('quality_flag', 'qa_sample')
            WHERE r.code IN ('QA', 'ADMIN', 'SYSTEM_ADMIN')
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE resource IN ('quality_flag', 'qa_sample'));
            DELETE FROM permission WHERE resource IN ('quality_flag', 'qa_sample');
            """);

        migrationBuilder.DropTable(
            name: "quality_flag");

        migrationBuilder.DropTable(
            name: "qa_sample");
    }
}
