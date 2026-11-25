using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G54.DAL.Migrations;

/// <summary>
/// Call recording AI triage (AI-02): call recordings, masked transcripts, AI review flags, the recording link on risk events,
/// and the <c>ai_review_flag</c> / <c>call_recording</c> permissions for VERIFIER and SYSTEM_ADMIN.
/// </summary>
public partial class AddCallRecordingAiTriage : Migration
{
    private static readonly string[] FlagStatusColumns = ["status", "rank_score"];
    private static readonly string[] RecordingQueueColumns = ["processing_status", "uploaded_at"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "ai_review_flag_code_seq");

        migrationBuilder.AddColumn<Guid>(
            name: "ai_review_flag_id",
            table: "risk_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "call_recording_id",
            table: "risk_event",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "media_end_ms",
            table: "risk_event",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "media_start_ms",
            table: "risk_event",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "call_recording",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                result_submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                work_id = table.Column<Guid>(type: "uuid", nullable: false),
                transaction_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                file_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                duration_ms = table.Column<int>(type: "integer", nullable: true),
                processing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                pipeline_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                kb_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                retain_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_call_recording", x => x.id);
                table.CheckConstraint("ck_call_recording_size", "size_bytes > 0");
                table.ForeignKey(
                    name: "fk_call_recording_submission",
                    column: x => x.result_submission_id,
                    principalTable: "result_submission",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_call_recording_uploaded_by",
                    column: x => x.uploaded_by,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_call_recording_work",
                    column: x => x.work_id,
                    principalTable: "work",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ai_review_flag",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code_no = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('ai_review_flag_code_seq')"),
                call_recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                flag_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                rule_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                detector = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                rank_score = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                start_ms = table.Column<int>(type: "integer", nullable: false),
                end_ms = table.Column<int>(type: "integer", nullable: false),
                evidence_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                kb_refs = table.Column<string>(type: "jsonb", nullable: false),
                details = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                dismiss_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                risk_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                quality_flag_id = table.Column<Guid>(type: "uuid", nullable: true),
                pipeline_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                kb_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ai_review_flag", x => x.id);
                table.CheckConstraint("ck_ai_review_flag_media", "start_ms >= 0 AND end_ms > start_ms");
                table.CheckConstraint("ck_ai_review_flag_rank_score", "rank_score >= 0 AND rank_score <= 1");
                table.ForeignKey(
                    name: "fk_ai_review_flag_employee",
                    column: x => x.employee_id,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ai_review_flag_quality_flag",
                    column: x => x.quality_flag_id,
                    principalTable: "quality_flag",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "fk_ai_review_flag_recording",
                    column: x => x.call_recording_id,
                    principalTable: "call_recording",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ai_review_flag_reviewed_by",
                    column: x => x.reviewed_by,
                    principalTable: "employee",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_ai_review_flag_risk_event",
                    column: x => x.risk_event_id,
                    principalTable: "risk_event",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "call_transcript",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                call_recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                asr_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                segments = table.Column<string>(type: "jsonb", nullable: false),
                pii_masked = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_call_transcript", x => x.id);
                table.ForeignKey(
                    name: "fk_call_transcript_recording",
                    column: x => x.call_recording_id,
                    principalTable: "call_recording",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_risk_event_call_recording",
            table: "risk_event",
            column: "call_recording_id");

        migrationBuilder.CreateIndex(
            name: "uq_risk_event_ai_review_flag",
            table: "risk_event",
            column: "ai_review_flag_id",
            unique: true,
            filter: "ai_review_flag_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_ai_review_flag_employee",
            table: "ai_review_flag",
            column: "employee_id");

        migrationBuilder.CreateIndex(
            name: "IX_ai_review_flag_quality_flag_id",
            table: "ai_review_flag",
            column: "quality_flag_id");

        migrationBuilder.CreateIndex(
            name: "ix_ai_review_flag_recording",
            table: "ai_review_flag",
            column: "call_recording_id");

        migrationBuilder.CreateIndex(
            name: "IX_ai_review_flag_reviewed_by",
            table: "ai_review_flag",
            column: "reviewed_by");

        migrationBuilder.CreateIndex(
            name: "IX_ai_review_flag_risk_event_id",
            table: "ai_review_flag",
            column: "risk_event_id");

        migrationBuilder.CreateIndex(
            name: "ix_ai_review_flag_status",
            table: "ai_review_flag",
            columns: FlagStatusColumns);

        migrationBuilder.CreateIndex(
            name: "uq_ai_review_flag_code_no",
            table: "ai_review_flag",
            column: "code_no",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_call_recording_queue",
            table: "call_recording",
            columns: RecordingQueueColumns);

        migrationBuilder.CreateIndex(
            name: "ix_call_recording_submission",
            table: "call_recording",
            column: "result_submission_id");

        migrationBuilder.CreateIndex(
            name: "IX_call_recording_uploaded_by",
            table: "call_recording",
            column: "uploaded_by");

        migrationBuilder.CreateIndex(
            name: "IX_call_recording_work_id",
            table: "call_recording",
            column: "work_id");

        migrationBuilder.CreateIndex(
            name: "uq_call_transcript_recording",
            table: "call_transcript",
            column: "call_recording_id",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "fk_risk_event_ai_review_flag",
            table: "risk_event",
            column: "ai_review_flag_id",
            principalTable: "ai_review_flag",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_risk_event_call_recording",
            table: "risk_event",
            column: "call_recording_id",
            principalTable: "call_recording",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        // AI call triage (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §2): QA (VERIFIER) and administrators.
        migrationBuilder.Sql("""
            INSERT INTO permission (code, name, resource, action)
            VALUES ('AI_REVIEW_FLAG_READ', 'Xem đoạn ghi âm cần nghe lại', 'ai_review_flag', 'read'),
                   ('AI_REVIEW_FLAG_REVIEW', 'Xác nhận hoặc bỏ qua đoạn ghi âm cần nghe lại', 'ai_review_flag', 'review'),
                   ('CALL_RECORDING_PLAY', 'Nghe ghi âm cuộc gọi', 'call_recording', 'play')
            ON CONFLICT DO NOTHING;

            INSERT INTO role_permission (role_id, permission_id, scope)
            SELECT r.id, p.id, 'All'
            FROM role r
            JOIN permission p ON p.resource IN ('ai_review_flag', 'call_recording')
            WHERE r.code IN ('VERIFIER', 'SYSTEM_ADMIN')
            ON CONFLICT DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM role_permission WHERE permission_id IN (SELECT id FROM permission WHERE resource IN ('ai_review_flag', 'call_recording'));
            DELETE FROM permission WHERE resource IN ('ai_review_flag', 'call_recording');
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_risk_event_ai_review_flag",
            table: "risk_event");

        migrationBuilder.DropForeignKey(
            name: "fk_risk_event_call_recording",
            table: "risk_event");

        migrationBuilder.DropTable(
            name: "ai_review_flag");

        migrationBuilder.DropTable(
            name: "call_transcript");

        migrationBuilder.DropTable(
            name: "call_recording");

        migrationBuilder.DropIndex(
            name: "ix_risk_event_call_recording",
            table: "risk_event");

        migrationBuilder.DropIndex(
            name: "uq_risk_event_ai_review_flag",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "ai_review_flag_id",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "call_recording_id",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "media_end_ms",
            table: "risk_event");

        migrationBuilder.DropColumn(
            name: "media_start_ms",
            table: "risk_event");

        migrationBuilder.DropSequence(
            name: "ai_review_flag_code_seq");
    }
}
