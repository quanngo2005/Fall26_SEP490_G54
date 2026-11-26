using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<CallRecording> CallRecordings => Set<CallRecording>();

    public DbSet<CallTranscript> CallTranscripts => Set<CallTranscript>();

    public DbSet<AiReviewFlag> AiReviewFlags => Set<AiReviewFlag>();

    /// <summary>Call recordings, their masked transcripts and the AI review flags (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md).</summary>
    private static void ConfigureCallRecordings(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("ai_review_flag_code_seq");

        modelBuilder.Entity<CallRecording>(entity =>
        {
            entity.ToTable("call_recording", table => table.HasCheckConstraint("ck_call_recording_size", "size_bytes > 0"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.TransactionRef).HasMaxLength(100).HasColumnName("transaction_ref");
            entity.Property(e => e.ProductCode).HasMaxLength(50).HasColumnName("product_code");
            entity.Property(e => e.FilePath).HasMaxLength(1000).HasColumnName("file_path");
            entity.Property(e => e.FileName).HasMaxLength(255).HasColumnName("file_name");
            entity.Property(e => e.MimeType).HasMaxLength(100).HasColumnName("mime_type");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.Sha256).HasMaxLength(64).HasColumnName("sha256");
            entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
            entity.Property(e => e.ProcessingStatus).HasConversion(PgNameConverter<CallRecordingStatus>()).HasMaxLength(20).HasColumnName("processing_status");
            entity.Property(e => e.ErrorCode).HasMaxLength(50).HasColumnName("error_code");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.PipelineVersion).HasMaxLength(20).HasColumnName("pipeline_version");
            entity.Property(e => e.KbVersion).HasMaxLength(20).HasColumnName("kb_version");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entity.Property(e => e.RetainUntil).HasColumnName("retain_until");
            entity.HasIndex(e => e.ResultSubmissionId).HasDatabaseName("ix_call_recording_submission");
            entity.HasIndex(e => new { e.ProcessingStatus, e.UploadedAt }).HasDatabaseName("ix_call_recording_queue");
            entity.HasOne(e => e.ResultSubmission).WithMany().HasForeignKey(e => e.ResultSubmissionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_call_recording_submission");
            entity.HasOne(e => e.Work).WithMany().HasForeignKey(e => e.WorkId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_call_recording_work");
            entity.HasOne(e => e.UploadedByEmployee).WithMany().HasForeignKey(e => e.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_call_recording_uploaded_by");
        });

        modelBuilder.Entity<CallTranscript>(entity =>
        {
            entity.ToTable("call_transcript");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CallRecordingId).HasColumnName("call_recording_id");
            entity.Property(e => e.AsrModel).HasMaxLength(100).HasColumnName("asr_model");
            entity.Property(e => e.Language).HasMaxLength(10).HasColumnName("language");
            entity.Property(e => e.Segments).HasColumnType("jsonb").HasColumnName("segments");
            entity.Property(e => e.PiiMasked).HasColumnName("pii_masked");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.CallRecordingId).IsUnique().HasDatabaseName("uq_call_transcript_recording");
            entity.HasOne(e => e.CallRecording).WithOne(r => r.Transcript).HasForeignKey<CallTranscript>(e => e.CallRecordingId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_call_transcript_recording");
        });

        modelBuilder.Entity<AiReviewFlag>(entity =>
        {
            entity.ToTable("ai_review_flag", table =>
            {
                table.HasCheckConstraint("ck_ai_review_flag_media", "start_ms >= 0 AND end_ms > start_ms");
                table.HasCheckConstraint("ck_ai_review_flag_rank_score", "rank_score >= 0 AND rank_score <= 1");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CodeNo).HasColumnName("code_no").HasDefaultValueSql("nextval('ai_review_flag_code_seq')").ValueGeneratedOnAdd();
            entity.Property(e => e.CallRecordingId).HasColumnName("call_recording_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.FlagType).HasConversion(PgNameConverter<AiReviewFlagType>()).HasMaxLength(30).HasColumnName("flag_type");
            entity.Property(e => e.RuleCode).HasMaxLength(50).HasColumnName("rule_code");
            entity.Property(e => e.Detector).HasMaxLength(50).HasColumnName("detector");
            entity.Property(e => e.RankScore).HasPrecision(5, 4).HasColumnName("rank_score");
            entity.Property(e => e.StartMs).HasColumnName("start_ms");
            entity.Property(e => e.EndMs).HasColumnName("end_ms");
            entity.Property(e => e.EvidenceText).HasMaxLength(2000).HasColumnName("evidence_text");
            entity.Property(e => e.KbRefs).HasColumnType("jsonb").HasColumnName("kb_refs");
            entity.Property(e => e.Details).HasColumnType("jsonb").HasColumnName("details");
            entity.Property(e => e.Status).HasConversion(PgNameConverter<AiReviewFlagStatus>()).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
            entity.Property(e => e.DismissReason).HasMaxLength(2000).HasColumnName("dismiss_reason");
            entity.Property(e => e.RiskEventId).HasColumnName("risk_event_id");
            entity.Property(e => e.QualityFlagId).HasColumnName("quality_flag_id");
            entity.Property(e => e.PipelineVersion).HasMaxLength(20).HasColumnName("pipeline_version");
            entity.Property(e => e.KbVersion).HasMaxLength(20).HasColumnName("kb_version");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.CodeNo).IsUnique().HasDatabaseName("uq_ai_review_flag_code_no");
            entity.HasIndex(e => new { e.Status, e.RankScore }).HasDatabaseName("ix_ai_review_flag_status");
            entity.HasIndex(e => e.CallRecordingId).HasDatabaseName("ix_ai_review_flag_recording");
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("ix_ai_review_flag_employee");
            entity.HasOne(e => e.CallRecording).WithMany(r => r.Flags).HasForeignKey(e => e.CallRecordingId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ai_review_flag_recording");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ai_review_flag_employee");
            entity.HasOne(e => e.ReviewedByEmployee).WithMany().HasForeignKey(e => e.ReviewedBy)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_ai_review_flag_reviewed_by");
            entity.HasOne<RiskEvent>().WithMany().HasForeignKey(e => e.RiskEventId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_ai_review_flag_risk_event");
            entity.HasOne<QualityFlag>().WithMany().HasForeignKey(e => e.QualityFlagId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_ai_review_flag_quality_flag");
        });

        modelBuilder.Entity<RiskEvent>(entity =>
        {
            entity.Property(e => e.CallRecordingId).HasColumnName("call_recording_id");
            entity.Property(e => e.MediaStartMs).HasColumnName("media_start_ms");
            entity.Property(e => e.MediaEndMs).HasColumnName("media_end_ms");
            entity.Property(e => e.AiReviewFlagId).HasColumnName("ai_review_flag_id");
            entity.HasIndex(e => e.CallRecordingId).HasDatabaseName("ix_risk_event_call_recording");
            entity.HasIndex(e => e.AiReviewFlagId).IsUnique().HasFilter("ai_review_flag_id IS NOT NULL")
                .HasDatabaseName("uq_risk_event_ai_review_flag");
            entity.HasOne<CallRecording>().WithMany().HasForeignKey(e => e.CallRecordingId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_risk_event_call_recording");
            entity.HasOne<AiReviewFlag>().WithOne().HasForeignKey<RiskEvent>(e => e.AiReviewFlagId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_risk_event_ai_review_flag");
        });
    }
}
