using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<QualityFlag> QualityFlags => Set<QualityFlag>();

    public DbSet<QaSample> QaSamples => Set<QaSample>();

    private static void ConfigureQualityAssurance(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>("qa_item_code_seq");

        modelBuilder.Entity<QualityFlag>(entity =>
        {
            entity.ToTable("quality_flag");
            entity.Property(e => e.CodeNo).HasColumnName("code_no").HasDefaultValueSql("nextval('qa_item_code_seq')").ValueGeneratedOnAdd();
            entity.HasIndex(e => e.CodeNo).IsUnique().HasDatabaseName("uq_quality_flag_code_no");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.FlaggedByEmployeeId).HasColumnName("flagged_by_employee_id");
            entity.Property(e => e.QaSampleId).HasColumnName("qa_sample_id");
            entity.Property(e => e.Severity).HasConversion(PgNameConverter<QualityFlagSeverity>()).HasMaxLength(10).HasColumnName("severity");
            entity.Property(e => e.Status).HasConversion(PgNameConverter<QualityFlagStatus>()).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.Reason).HasMaxLength(2000).HasColumnName("reason");
            entity.Property(e => e.ReworkComment).HasMaxLength(2000).HasColumnName("rework_comment");
            entity.Property(e => e.ReworkRequestedAt).HasColumnName("rework_requested_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("ix_quality_flag_status");
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("ix_quality_flag_employee");
            entity.HasIndex(e => e.ResultSubmissionId).HasDatabaseName("ix_quality_flag_submission");
            entity.HasIndex(e => e.QaSampleId).IsUnique().HasFilter("qa_sample_id IS NOT NULL").HasDatabaseName("uq_quality_flag_sample");
            entity.HasOne(e => e.ResultSubmission).WithMany().HasForeignKey(e => e.ResultSubmissionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_quality_flag_submission");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_quality_flag_employee");
            entity.HasOne(e => e.FlaggedByEmployee).WithMany().HasForeignKey(e => e.FlaggedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_quality_flag_flagged_by");
        });

        modelBuilder.Entity<QaSample>(entity =>
        {
            entity.ToTable("qa_sample");
            entity.Property(e => e.CodeNo).HasColumnName("code_no").HasDefaultValueSql("nextval('qa_item_code_seq')").ValueGeneratedOnAdd();
            entity.HasIndex(e => e.CodeNo).IsUnique().HasDatabaseName("uq_qa_sample_code_no");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.SubmitterEmployeeId).HasColumnName("submitter_employee_id");
            entity.Property(e => e.Status).HasConversion(PgNameConverter<QaSampleStatus>()).HasMaxLength(10).HasColumnName("status");
            entity.Property(e => e.SampledAt).HasColumnName("sampled_at");
            entity.Property(e => e.ReviewedByEmployeeId).HasColumnName("reviewed_by_employee_id");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
            entity.Property(e => e.Comment).HasMaxLength(2000).HasColumnName("comment");
            entity.HasIndex(e => e.ResultSubmissionId).IsUnique().HasDatabaseName("uq_qa_sample_submission");
            entity.HasIndex(e => new { e.Status, e.SampledAt }).HasDatabaseName("ix_qa_sample_status");
            entity.HasOne(e => e.ResultSubmission).WithMany().HasForeignKey(e => e.ResultSubmissionId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_qa_sample_submission");
            entity.HasOne(e => e.SubmitterEmployee).WithMany().HasForeignKey(e => e.SubmitterEmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_qa_sample_submitter");
            entity.HasOne(e => e.ReviewedByEmployee).WithMany().HasForeignKey(e => e.ReviewedByEmployeeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_qa_sample_reviewer");
            entity.HasOne(e => e.QualityFlag).WithOne(f => f.QaSample).HasForeignKey<QualityFlag>(f => f.QaSampleId)
                .OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_quality_flag_sample");
        });
    }
}
