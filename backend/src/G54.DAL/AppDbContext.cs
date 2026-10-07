using System;
using System.Collections.Generic;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }

    public virtual DbSet<ActualProgress> ActualProgresses { get; set; }

    public virtual DbSet<Appeal> Appeals { get; set; }

    public virtual DbSet<AuthAuditLog> AuthAuditLogs { get; set; }

    public virtual DbSet<BusinessCampaign> BusinessCampaigns { get; set; }

    public virtual DbSet<BusinessObjective> BusinessObjectives { get; set; }

    public virtual DbSet<CampaignWorkType> CampaignWorkTypes { get; set; }

    public virtual DbSet<Department> Departments { get; set; }

    public virtual DbSet<Employee> Employees { get; set; }

    public virtual DbSet<EvaluationPeriod> EvaluationPeriods { get; set; }

    public virtual DbSet<EvaluationProfile> EvaluationProfiles { get; set; }

    public virtual DbSet<EvaluationProfileItem> EvaluationProfileItems { get; set; }

    public virtual DbSet<Evidence> Evidences { get; set; }

    public virtual DbSet<JobPosition> JobPositions { get; set; }

    public virtual DbSet<JobPositionWorkType> JobPositionWorkTypes { get; set; }

    public virtual DbSet<KpiDefinition> KpiDefinitions { get; set; }

    public virtual DbSet<KpiGroup> KpiGroups { get; set; }

    public virtual DbSet<ObjectiveKpi> ObjectiveKpis { get; set; }

    public virtual DbSet<Participant> Participants { get; set; }

    public virtual DbSet<PerformanceContribution> PerformanceContributions { get; set; }

    public virtual DbSet<PerformanceEvaluation> PerformanceEvaluations { get; set; }

    public virtual DbSet<PermissionRecord> Permissions { get; set; }

    public virtual DbSet<ResultSubmission> ResultSubmissions { get; set; }

    public virtual DbSet<RiskEvent> RiskEvents { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<RolePermissionAssignment> RolePermissions { get; set; }

    public virtual DbSet<TargetAssignment> TargetAssignments { get; set; }

    public virtual DbSet<TaskHandover> TaskHandovers { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    public virtual DbSet<Verification> Verifications { get; set; }

    public virtual DbSet<VerifiedWorkResult> VerifiedWorkResults { get; set; }

    public virtual DbSet<Work> Works { get; set; }

    public virtual DbSet<WorkResultItem> WorkResultItems { get; set; }

    public virtual DbSet<WorkType> WorkTypes { get; set; }

    public virtual DbSet<WorkTypeKpi> WorkTypeKpis { get; set; }

    public virtual DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasPostgresEnum<DepartmentLevelType>("public", "department_level_type")
            .HasPostgresEnum<EmployeeStatus>("public", "employee_status")
            .HasPostgresEnum<RecordStatus>("public", "record_status")
            .HasPostgresEnum<BusinessObjectiveStatus>("public", "business_objective_status")
            .HasPostgresEnum<BusinessCampaignStatus>("public", "business_campaign_status")
            .HasPostgresEnum<KpiStatus>("public", "kpi_status")
            .HasPostgresEnum<KpiAggregationType>("public", "kpi_aggregation_type")
            .HasPostgresEnum<KpiDirection>("public", "kpi_direction")
            .HasPostgresEnum<WorkPriority>("public", "work_priority")
            .HasPostgresEnum<WorkStatus>("public", "work_status")
            .HasPostgresEnum<WorkflowDefinitionStatus>("public", "workflow_definition_status")
            .HasPostgresEnum<HandoverStatus>("public", "handover_status")
            .HasPostgresEnum<SubmissionStatus>("public", "submission_status")
            .HasPostgresEnum<VerificationDecision>("public", "verification_decision")
            .HasPostgresEnum<VerifiedResultStatus>("public", "verified_result_status")
            .HasPostgresEnum<ActualProgressStatus>("public", "actual_progress_status")
            .HasPostgresEnum<RiskEventSeverity>("public", "risk_event_severity")
            .HasPostgresEnum<RiskEventStatus>("public", "risk_event_status")
            .HasPostgresEnum<AppealStatus>("public", "appeal_status")
            .HasPostgresEnum<PeriodType>("public", "period_type")
            .HasPostgresEnum<PeriodStatus>("public", "period_status")
            .HasPostgresEnum<EvaluationProfileStatus>("public", "evaluation_profile_status")
            .HasPostgresEnum<PerformanceEvaluationStatus>("public", "performance_evaluation_status")
            .HasPostgresEnum<TargetAssignmentStatus>("public", "target_assignment_status")
            .HasPostgresEnum<TargetSplitDimension>("public", "target_split_dimension")
            .HasPostgresExtension("pgcrypto");

        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("account_pkey");

            entity.ToTable("account");

            entity.HasIndex(e => e.EmployeeId, "uq_account_employee").IsUnique();

            entity.HasIndex(e => e.Username, "uq_account_username").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.FailedLoginAttempts)
                .HasDefaultValue(0)
                .HasColumnName("failed_login_attempts");
            entity.Property(e => e.IsLocked)
                .HasDefaultValue(false)
                .HasColumnName("is_locked");
            entity.Property(e => e.LastSyncAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_sync_at");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username");

            entity.HasOne(d => d.Employee).WithOne(p => p.Account)
                .HasForeignKey<Account>(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_account_employee");
        });

        modelBuilder.Entity<ActualProgress>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("actual_progress_pkey");

            entity.ToTable("actual_progress", table =>
            {
                table.HasCheckConstraint("ck_actual_progress_value", "value >= 0");
                table.HasCheckConstraint("ck_actual_progress_denominator", "denominator IS NULL OR denominator > 0");
            });

            entity.HasIndex(e => e.PerformanceContributionId, "ix_actual_progress_contribution");

            entity.HasIndex(e => new { e.EmployeeId, e.EvaluationPeriodId }, "ix_actual_progress_employee_period");

            entity.HasIndex(e => new { e.KpiDefinitionId, e.EvaluationPeriodId }, "ix_actual_progress_kpi_period");

            entity.HasIndex(e => e.SourceItemId, "uq_actual_progress_source_item").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Denominator)
                .HasPrecision(18, 4)
                .HasColumnName("denominator");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EvaluationPeriodId).HasColumnName("evaluation_period_id");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.PerformanceContributionId).HasColumnName("performance_contribution_id");
            entity.Property(e => e.ProgressDate).HasColumnName("progress_date");
            entity.Property(e => e.SourceItemId).HasColumnName("source_item_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Value)
                .HasPrecision(18, 4)
                .HasColumnName("value");

            entity.HasOne(d => d.Employee).WithMany(p => p.ActualProgresses)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_actual_progress_employee");

            entity.HasOne(d => d.EvaluationPeriod).WithMany(p => p.ActualProgresses)
                .HasForeignKey(d => d.EvaluationPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_actual_progress_period");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.ActualProgresses)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_actual_progress_kpi");

            entity.HasOne(d => d.PerformanceContribution).WithMany(p => p.ActualProgresses)
                .HasForeignKey(d => d.PerformanceContributionId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_actual_progress_contribution");

            entity.HasOne(d => d.SourceItem).WithOne(p => p.ActualProgress)
                .HasForeignKey<ActualProgress>(d => d.SourceItemId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_actual_progress_source_item");

            entity.Property(e => e.Status)
                .HasColumnType("actual_progress_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<Appeal>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("appeal_pkey");

            entity.ToTable("appeal", table =>
            {
                table.HasCheckConstraint("ck_appeal_decided_at", "decided_at IS NULL OR decided_at >= filed_at");
            });

            entity.HasIndex(e => e.ResultSubmissionId, "ix_appeal_submission");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.DecidedAt).HasColumnName("decided_at");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.FiledAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("filed_at");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Employee).WithMany(p => p.Appeals)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appeal_employee");

            entity.HasOne(d => d.ResultSubmission).WithMany(p => p.Appeals)
                .HasForeignKey(d => d.ResultSubmissionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_appeal_submission");

            entity.Property(e => e.Status)
                .HasColumnType("appeal_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<AuthAuditLog>(entity =>
        {
            entity.ToTable("auth_audit_log");

            entity.HasIndex(e => new { e.ActorId, e.OccurredAt }, "IX_auth_audit_log_actor_id_occurred_at");

            entity.Property(e => e.Id)
                .ValueGeneratedNever()
                .HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.ActorId).HasColumnName("actor_id");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(64)
                .HasColumnName("ip_address");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");

            entity.HasOne(d => d.Actor).WithMany(p => p.AuthAuditLogs)
                .HasForeignKey(d => d.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BusinessCampaign>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("business_campaign_pkey");

            entity.ToTable("business_campaign", table =>
            {
                table.HasCheckConstraint("ck_business_campaign_dates", "end_date >= start_date");
            });

            entity.HasIndex(e => e.Code, "uq_business_campaign_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BusinessObjectiveId).HasColumnName("business_objective_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.BusinessObjective).WithMany(p => p.BusinessCampaigns)
                .HasForeignKey(d => d.BusinessObjectiveId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_business_campaign_objective");

            entity.Property(e => e.Status)
                .HasColumnType("business_campaign_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<BusinessObjective>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("business_objective_pkey");

            entity.ToTable("business_objective", table =>
            {
                table.HasCheckConstraint("ck_business_objective_dates", "end_date >= start_date");
            });

            entity.HasIndex(e => e.Code, "uq_business_objective_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.Property(e => e.Status)
                .HasColumnType("business_objective_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<CampaignWorkType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("campaign_work_type_pkey");

            entity.ToTable("campaign_work_type");

            entity.HasIndex(e => new { e.BusinessCampaignId, e.WorkTypeId }, "uq_campaign_work_type").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BusinessCampaignId).HasColumnName("business_campaign_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkTypeId).HasColumnName("work_type_id");

            entity.HasOne(d => d.BusinessCampaign).WithMany(p => p.CampaignWorkTypes)
                .HasForeignKey(d => d.BusinessCampaignId)
                .HasConstraintName("fk_campaign_work_type_campaign");

            entity.HasOne(d => d.WorkType).WithMany(p => p.CampaignWorkTypes)
                .HasForeignKey(d => d.WorkTypeId)
                .HasConstraintName("fk_campaign_work_type_work_type");
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("department_pkey");

            entity.ToTable("department", table =>
            {
                table.HasCheckConstraint("ck_department_no_self_parent", "parent_department_id IS NULL OR parent_department_id <> id");
            });

            entity.HasIndex(e => e.ParentDepartmentId, "ix_department_parent");

            entity.HasIndex(e => e.Code, "uq_department_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.LastSyncAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_sync_at");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.ParentDepartmentId).HasColumnName("parent_department_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ParentDepartment).WithMany(p => p.InverseParentDepartment)
                .HasForeignKey(d => d.ParentDepartmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_department_parent");

            entity.Property(e => e.LevelType)
                .HasColumnType("department_level_type")
                .HasColumnName("level_type");

            entity.Property(e => e.Status)
                .HasColumnType("record_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("employee_pkey");

            entity.ToTable("employee", table =>
            {
                table.HasCheckConstraint("ck_employee_no_self_manager", "manager_id IS NULL OR manager_id <> id");
            });

            entity.HasIndex(e => e.DepartmentId, "ix_employee_department");

            entity.HasIndex(e => e.JobPositionId, "ix_employee_job_position");

            entity.HasIndex(e => e.ManagerId, "ix_employee_manager");

            entity.HasIndex(e => e.EmployeeCode, "uq_employee_code").IsUnique();

            entity.HasIndex(e => e.Email, "uq_employee_email").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.EmployeeCode)
                .HasMaxLength(50)
                .HasColumnName("employee_code");
            entity.Property(e => e.FullName)
                .HasMaxLength(200)
                .HasColumnName("full_name");
            entity.Property(e => e.JobPositionId).HasColumnName("job_position_id");
            entity.Property(e => e.LastSyncAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_sync_at");
            entity.Property(e => e.ManagerId).HasColumnName("manager_id");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Department).WithMany(p => p.Employees)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_employee_department");

            entity.HasOne(d => d.JobPosition).WithMany(p => p.Employees)
                .HasForeignKey(d => d.JobPositionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_employee_job_position");

            entity.HasOne(d => d.Manager).WithMany(p => p.InverseManager)
                .HasForeignKey(d => d.ManagerId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_employee_manager");

            entity.Property(e => e.Status)
                .HasColumnType("employee_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<EvaluationPeriod>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("evaluation_period_pkey");

            entity.ToTable("evaluation_period", table =>
            {
                table.HasCheckConstraint("ck_evaluation_period_dates", "end_date >= start_date");
                table.HasCheckConstraint("ck_evaluation_period_no_self_parent", "parent_period_id IS NULL OR parent_period_id <> id");
            });

            entity.HasIndex(e => e.ParentPeriodId, "ix_evaluation_period_parent");

            entity.HasIndex(e => e.Code, "uq_evaluation_period_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.ParentPeriodId).HasColumnName("parent_period_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.ParentPeriod).WithMany(p => p.InverseParentPeriod)
                .HasForeignKey(d => d.ParentPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evaluation_period_parent");

            entity.Property(e => e.PeriodType)
                .HasColumnType("period_type")
                .HasColumnName("period_type");

            entity.Property(e => e.Status)
                .HasColumnType("period_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<EvaluationProfile>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("evaluation_profile_pkey");

            entity.ToTable("evaluation_profile", table =>
            {
                table.HasCheckConstraint("ck_evaluation_profile_version", "version > 0");
                table.HasCheckConstraint("ck_evaluation_profile_dates", "effective_to IS NULL OR effective_to >= effective_from");
            });

            entity.HasIndex(e => e.Code, "uq_evaluation_profile_active_code")
                .IsUnique()
                .HasFilter("(status = 'ACTIVE'::evaluation_profile_status)");

            entity.HasIndex(e => new { e.Code, e.Version }, "uq_evaluation_profile_code_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EffectiveFrom)
                .HasDefaultValueSql("CURRENT_DATE")
                .HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Version)
                .HasDefaultValue(1)
                .HasColumnName("version");

            entity.Property(e => e.Status)
                .HasColumnType("evaluation_profile_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<EvaluationProfileItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("evaluation_profile_item_pkey");

            entity.ToTable("evaluation_profile_item", table =>
            {
                table.HasCheckConstraint("ck_evaluation_profile_item_weight", "weight_percent > 0 AND weight_percent <= 100");
                table.HasCheckConstraint("ck_evaluation_profile_item_display_order", "display_order > 0");
            });

            entity.HasIndex(e => new { e.EvaluationProfileId, e.KpiGroupId }, "uq_evaluation_profile_item").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.DisplayOrder)
                .HasDefaultValue(1)
                .HasColumnName("display_order");
            entity.Property(e => e.EvaluationProfileId).HasColumnName("evaluation_profile_id");
            entity.Property(e => e.KpiGroupId).HasColumnName("kpi_group_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeightPercent)
                .HasPrecision(7, 4)
                .HasColumnName("weight_percent");

            entity.HasOne(d => d.EvaluationProfile).WithMany(p => p.EvaluationProfileItems)
                .HasForeignKey(d => d.EvaluationProfileId)
                .HasConstraintName("fk_evaluation_profile_item_profile");

            entity.HasOne(d => d.KpiGroup).WithMany(p => p.EvaluationProfileItems)
                .HasForeignKey(d => d.KpiGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evaluation_profile_item_group");
        });

        modelBuilder.Entity<Evidence>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("evidence_pkey");

            entity.ToTable("evidence", table =>
            {
                table.HasCheckConstraint("ck_evidence_single_owner", "(CASE WHEN result_submission_id IS NOT NULL THEN 1 ELSE 0 END +\r\n             CASE WHEN risk_event_id IS NOT NULL THEN 1 ELSE 0 END +\r\n             CASE WHEN appeal_id IS NOT NULL THEN 1 ELSE 0 END) = 1");
                table.HasCheckConstraint("ck_evidence_size", "file_size IS NULL OR file_size > 0");
            });

            entity.HasIndex(e => e.AppealId, "ix_evidence_appeal");

            entity.HasIndex(e => e.RiskEventId, "ix_evidence_risk_event");

            entity.HasIndex(e => e.ResultSubmissionId, "ix_evidence_submission");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AppealId).HasColumnName("appeal_id");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .HasColumnName("file_name");
            entity.Property(e => e.FilePath)
                .HasMaxLength(1000)
                .HasColumnName("file_path");
            entity.Property(e => e.FileSize).HasColumnName("file_size");
            entity.Property(e => e.MimeType)
                .HasMaxLength(100)
                .HasColumnName("mime_type");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.RiskEventId).HasColumnName("risk_event_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UploadedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("uploaded_at");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");

            entity.HasOne(d => d.Appeal).WithMany(p => p.Evidences)
                .HasForeignKey(d => d.AppealId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_evidence_appeal");

            entity.HasOne(d => d.ResultSubmission).WithMany(p => p.Evidences)
                .HasForeignKey(d => d.ResultSubmissionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_evidence_submission");

            entity.HasOne(d => d.RiskEvent).WithMany(p => p.Evidences)
                .HasForeignKey(d => d.RiskEventId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_evidence_risk_event");

            entity.HasOne(d => d.UploadedByNavigation).WithMany(p => p.Evidences)
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_evidence_uploader");
        });

        modelBuilder.Entity<JobPosition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("job_position_pkey");

            entity.ToTable("job_position");

            entity.HasIndex(e => e.Code, "uq_job_position_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EvaluationProfileId).HasColumnName("evaluation_profile_id");
            entity.Property(e => e.LastSyncAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("last_sync_at");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.EvaluationProfile).WithMany(p => p.JobPositions)
                .HasForeignKey(d => d.EvaluationProfileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_job_position_evaluation_profile");

            entity.Property(e => e.Status)
                .HasColumnType("record_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<JobPositionWorkType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("job_position_work_type_pkey");

            entity.ToTable("job_position_work_type");

            entity.HasIndex(e => new { e.JobPositionId, e.WorkTypeId }, "uq_job_position_work_type").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.JobPositionId).HasColumnName("job_position_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkTypeId).HasColumnName("work_type_id");

            entity.HasOne(d => d.JobPosition).WithMany(p => p.JobPositionWorkTypes)
                .HasForeignKey(d => d.JobPositionId)
                .HasConstraintName("fk_job_position_work_type_position");

            entity.HasOne(d => d.WorkType).WithMany(p => p.JobPositionWorkTypes)
                .HasForeignKey(d => d.WorkTypeId)
                .HasConstraintName("fk_job_position_work_type_work_type");
        });

        modelBuilder.Entity<KpiDefinition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("kpi_definition_pkey");

            entity.ToTable("kpi_definition", table =>
            {
                table.HasCheckConstraint("ck_kpi_max_achievement", "max_achievement_percent IS NULL OR max_achievement_percent > 0");
            });

            entity.HasIndex(e => e.Code, "uq_kpi_definition_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Formula).HasColumnName("formula");
            entity.Property(e => e.KpiGroupId).HasColumnName("kpi_group_id");
            entity.Property(e => e.MaxAchievementPercent)
                .HasPrecision(8, 4)
                .HasColumnName("max_achievement_percent");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Unit)
                .HasMaxLength(50)
                .HasColumnName("unit");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.KpiGroup).WithMany(p => p.KpiDefinitions)
                .HasForeignKey(d => d.KpiGroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_kpi_definition_group");

            entity.Property(e => e.AggregationType)
                .HasColumnType("kpi_aggregation_type")
                .HasColumnName("aggregation_type");

            entity.Property(e => e.Direction)
                .HasColumnType("kpi_direction")
                .HasColumnName("direction");

            entity.Property(e => e.Status)
                .HasColumnType("kpi_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<KpiGroup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("kpi_group_pkey");

            entity.ToTable("kpi_group");

            entity.HasIndex(e => e.Code, "uq_kpi_group_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.Property(e => e.Status)
                .HasColumnType("kpi_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<ObjectiveKpi>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("objective_kpi_pkey");

            entity.ToTable("objective_kpi");

            entity.HasIndex(e => new { e.BusinessObjectiveId, e.KpiDefinitionId }, "uq_objective_kpi").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BusinessObjectiveId).HasColumnName("business_objective_id");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.BusinessObjective).WithMany(p => p.ObjectiveKpis)
                .HasForeignKey(d => d.BusinessObjectiveId)
                .HasConstraintName("fk_objective_kpi_objective");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.ObjectiveKpis)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_objective_kpi_kpi");
        });

        modelBuilder.Entity<Participant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("participant_pkey");

            entity.ToTable("participant");

            entity.HasIndex(e => new { e.BusinessCampaignId, e.EmployeeId }, "uq_participant").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.BusinessCampaignId).HasColumnName("business_campaign_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.BusinessCampaign).WithMany(p => p.Participants)
                .HasForeignKey(d => d.BusinessCampaignId)
                .HasConstraintName("fk_participant_campaign");

            entity.HasOne(d => d.Employee).WithMany(p => p.Participants)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_participant_employee");
        });

        modelBuilder.Entity<PerformanceContribution>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("performance_contribution_pkey");

            entity.ToTable("performance_contribution", table =>
            {
                table.HasCheckConstraint("ck_performance_contribution_kind", "(kpi_definition_id IS NOT NULL) <> (evaluation_profile_item_id IS NOT NULL)");
                table.HasCheckConstraint("ck_performance_contribution_kpi_level", "kpi_definition_id IS NULL\r\n            OR (\r\n                parent_contribution_id IS NOT NULL\r\n                AND target_assignment_id IS NOT NULL\r\n                AND target_value_snapshot IS NOT NULL\r\n                AND actual_value IS NOT NULL\r\n                AND weight_snapshot IS NULL\r\n            )");
                table.HasCheckConstraint("ck_performance_contribution_group_level", "evaluation_profile_item_id IS NULL\r\n            OR (\r\n                parent_contribution_id IS NULL\r\n                AND target_assignment_id IS NULL\r\n                AND target_value_snapshot IS NULL\r\n                AND actual_value IS NULL\r\n                AND weight_snapshot IS NOT NULL\r\n                AND weight_snapshot > 0\r\n                AND weight_snapshot <= 100\r\n            )");
                table.HasCheckConstraint("ck_performance_contribution_target_version", "target_version_snapshot IS NULL OR target_version_snapshot > 0");
                table.HasCheckConstraint("ck_performance_contribution_denominator", "actual_denominator IS NULL OR actual_denominator > 0");
                table.HasCheckConstraint("ck_performance_contribution_achievement_raw", "achievement_percent_raw IS NULL OR achievement_percent_raw >= 0");
                table.HasCheckConstraint("ck_performance_contribution_achievement_capped", "achievement_percent_capped IS NULL OR achievement_percent_capped >= 0");
                table.HasCheckConstraint("ck_performance_contribution_weight_snapshot", "weight_snapshot IS NULL OR (weight_snapshot > 0 AND weight_snapshot <= 100)");
                table.HasCheckConstraint("ck_performance_contribution_capped_not_above_raw", "achievement_percent_raw IS NULL OR achievement_percent_capped IS NULL OR achievement_percent_capped <= achievement_percent_raw");
            });

            entity.HasIndex(e => e.ParentContributionId, "ix_performance_contribution_parent");

            entity.HasIndex(e => e.TargetAssignmentId, "ix_performance_contribution_target");

            entity.HasIndex(e => new { e.PerformanceEvaluationId, e.EvaluationProfileItemId }, "uq_performance_contribution_group")
                .IsUnique()
                .HasFilter("(evaluation_profile_item_id IS NOT NULL)");

            entity.HasIndex(e => new { e.PerformanceEvaluationId, e.KpiDefinitionId }, "uq_performance_contribution_kpi")
                .IsUnique()
                .HasFilter("(kpi_definition_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AchievementPercentCapped)
                .HasPrecision(10, 4)
                .HasColumnName("achievement_percent_capped");
            entity.Property(e => e.AchievementPercentRaw)
                .HasPrecision(10, 4)
                .HasColumnName("achievement_percent_raw");
            entity.Property(e => e.ActualDenominator)
                .HasPrecision(18, 4)
                .HasColumnName("actual_denominator");
            entity.Property(e => e.ActualValue)
                .HasPrecision(18, 4)
                .HasColumnName("actual_value");
            entity.Property(e => e.ComponentScore)
                .HasPrecision(10, 4)
                .HasColumnName("component_score");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EvaluationPeriodId).HasColumnName("evaluation_period_id");
            entity.Property(e => e.EvaluationProfileItemId).HasColumnName("evaluation_profile_item_id");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.ParentContributionId).HasColumnName("parent_contribution_id");
            entity.Property(e => e.PerformanceEvaluationId).HasColumnName("performance_evaluation_id");
            entity.Property(e => e.TargetAssignmentId).HasColumnName("target_assignment_id");
            entity.Property(e => e.TargetValueSnapshot)
                .HasPrecision(18, 4)
                .HasColumnName("target_value_snapshot");
            entity.Property(e => e.TargetVersionSnapshot).HasColumnName("target_version_snapshot");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WeightSnapshot)
                .HasPrecision(7, 4)
                .HasColumnName("weight_snapshot");

            entity.HasOne(d => d.Employee).WithMany(p => p.PerformanceContributions)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_contribution_employee");

            entity.HasOne(d => d.EvaluationPeriod).WithMany(p => p.PerformanceContributions)
                .HasForeignKey(d => d.EvaluationPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_contribution_period");

            entity.HasOne(d => d.EvaluationProfileItem).WithMany(p => p.PerformanceContributions)
                .HasForeignKey(d => d.EvaluationProfileItemId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_contribution_profile_item");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.PerformanceContributions)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_contribution_kpi");

            entity.HasOne(d => d.ParentContribution).WithMany(p => p.InverseParentContribution)
                .HasForeignKey(d => d.ParentContributionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_performance_contribution_parent");

            entity.HasOne(d => d.PerformanceEvaluation).WithMany(p => p.PerformanceContributionPerformanceEvaluations)
                .HasForeignKey(d => d.PerformanceEvaluationId)
                .HasConstraintName("fk_performance_contribution_evaluation");

            entity.HasOne(d => d.TargetAssignment).WithMany(p => p.PerformanceContributions)
                .HasForeignKey(d => d.TargetAssignmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_contribution_target");

            entity.HasOne(d => d.PerformanceEvaluationNavigation).WithMany(p => p.PerformanceContributionPerformanceEvaluationNavigations)
                .HasPrincipalKey(p => new { p.Id, p.EmployeeId, p.EvaluationPeriodId })
                .HasForeignKey(d => new { d.PerformanceEvaluationId, d.EmployeeId, d.EvaluationPeriodId })
                .HasConstraintName("fk_performance_contribution_evaluation_identity");
        });

        modelBuilder.Entity<PerformanceEvaluation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("performance_evaluation_pkey");

            entity.ToTable("performance_evaluation");

            entity.HasIndex(e => e.EvaluationPeriodId, "ix_performance_evaluation_period");

            entity.HasIndex(e => e.EvaluationProfileId, "ix_performance_evaluation_profile");

            entity.HasIndex(e => new { e.EmployeeId, e.EvaluationPeriodId }, "uq_performance_evaluation").IsUnique();

            entity.HasIndex(e => new { e.Id, e.EmployeeId, e.EvaluationPeriodId }, "uq_performance_evaluation_identity").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.CalculatedAt).HasColumnName("calculated_at");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EvaluationPeriodId).HasColumnName("evaluation_period_id");
            entity.Property(e => e.EvaluationProfileId).HasColumnName("evaluation_profile_id");
            entity.Property(e => e.ReviewerEmployeeId).HasColumnName("reviewer_employee_id");
            entity.Property(e => e.TotalScore)
                .HasPrecision(10, 4)
                .HasColumnName("total_score");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Employee).WithMany(p => p.PerformanceEvaluationEmployees)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_evaluation_employee");

            entity.HasOne(d => d.EvaluationPeriod).WithMany(p => p.PerformanceEvaluations)
                .HasForeignKey(d => d.EvaluationPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_evaluation_period");

            entity.HasOne(d => d.EvaluationProfile).WithMany(p => p.PerformanceEvaluations)
                .HasForeignKey(d => d.EvaluationProfileId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_performance_evaluation_profile");

            entity.HasOne(d => d.ReviewerEmployee).WithMany(p => p.PerformanceEvaluationReviewerEmployees)
                .HasForeignKey(d => d.ReviewerEmployeeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_performance_evaluation_reviewer");

            entity.Property(e => e.Status)
                .HasColumnType("performance_evaluation_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<PermissionRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("permission_pkey");

            entity.ToTable("permission");

            entity.HasIndex(e => e.Code, "uq_permission_code").IsUnique();

            entity.HasIndex(e => new { e.Resource, e.Action }, "uq_permission_resource_action").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.Code)
                .HasMaxLength(100)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Resource)
                .HasMaxLength(100)
                .HasColumnName("resource");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<ResultSubmission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("result_submission_pkey");

            entity.ToTable("result_submission", table =>
            {
                table.HasCheckConstraint("ck_result_submission_no", "submission_no > 0");
            });

            entity.HasIndex(e => e.WorkId, "ix_result_submission_work");

            entity.HasIndex(e => new { e.WorkId, e.SubmissionNo }, "uq_result_submission_attempt").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.SubmissionNo)
                .HasDefaultValue(1)
                .HasColumnName("submission_no");
            entity.Property(e => e.SubmittedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("submitted_at");
            entity.Property(e => e.SubmitterEmployeeId).HasColumnName("submitter_employee_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkId).HasColumnName("work_id");
            entity.Property(e => e.WorkflowDefinitionId).HasColumnName("workflow_definition_id");

            entity.HasOne(d => d.SubmitterEmployee).WithMany(p => p.ResultSubmissions)
                .HasForeignKey(d => d.SubmitterEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_result_submission_submitter");

            entity.HasOne(d => d.Work).WithMany(p => p.ResultSubmissions)
                .HasForeignKey(d => d.WorkId)
                .HasConstraintName("fk_result_submission_work");

            entity.HasOne(d => d.WorkflowDefinition).WithMany(p => p.ResultSubmissions)
                .HasForeignKey(d => d.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_result_submission_workflow_definition");

            entity.Property(e => e.Status)
                .HasColumnType("submission_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<RiskEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("risk_event_pkey");

            entity.ToTable("risk_event", table =>
            {
                table.HasCheckConstraint("ck_risk_event_resolved_at", "resolved_at IS NULL OR resolved_at >= occurred_at");
            });

            entity.HasIndex(e => e.EmployeeId, "ix_risk_event_employee");

            entity.HasIndex(e => e.Code, "uq_risk_event_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EventType)
                .HasMaxLength(50)
                .HasColumnName("event_type");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Employee).WithMany(p => p.RiskEvents)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_risk_event_employee");

            entity.Property(e => e.Severity)
                .HasColumnType("risk_event_severity")
                .HasColumnName("severity");

            entity.Property(e => e.Status)
                .HasColumnType("risk_event_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("role_pkey");

            entity.ToTable("role");

            entity.HasIndex(e => e.Code, "uq_role_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.Property(e => e.Status)
                .HasColumnType("record_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<RolePermissionAssignment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("role_permission_pkey");

            entity.ToTable("role_permission");

            entity.HasIndex(e => e.PermissionId, "ix_role_permission_permission");

            entity.HasIndex(e => new { e.RoleId, e.PermissionId }, "uq_role_permission").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.PermissionId).HasColumnName("permission_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.PermissionId)
                .HasConstraintName("fk_role_permission_permission");

            entity.HasOne(d => d.Role).WithMany(p => p.RolePermissions)
                .HasForeignKey(d => d.RoleId)
                .HasConstraintName("fk_role_permission_role");
        });

        modelBuilder.Entity<TargetAssignment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("target_assignment_pkey");

            entity.ToTable("target_assignment", table =>
            {
                table.HasCheckConstraint("ck_target_assignment_owner_xor", "(department_id IS NOT NULL) <> (employee_id IS NOT NULL)");
                table.HasCheckConstraint("ck_target_assignment_version", "version > 0");
                table.HasCheckConstraint("ck_target_assignment_value", "target_value >= 0");
                table.HasCheckConstraint("ck_target_assignment_version_link", "(version = 1 AND previous_version_id IS NULL)\r\n            OR\r\n            (version > 1 AND previous_version_id IS NOT NULL)");
                table.HasCheckConstraint("ck_target_assignment_approval", "(status IN ('ACTIVE', 'SUPERSEDED', 'CLOSED') AND approved_at IS NOT NULL)\r\n            OR status = 'DRAFT'");
                table.HasCheckConstraint("ck_target_assignment_approved_by_time", "approved_at IS NULL OR approved_at >= created_at");
                table.HasCheckConstraint("ck_target_assignment_assigned_time", "assigned_at IS NULL OR assigned_at >= created_at");
            });

            entity.HasIndex(e => e.KpiDefinitionId, "ix_target_assignment_kpi");

            entity.HasIndex(e => e.ParentAssignmentId, "ix_target_assignment_parent");

            entity.HasIndex(e => e.EvaluationPeriodId, "ix_target_assignment_period");

            entity.HasIndex(e => new { e.KpiDefinitionId, e.DepartmentId, e.EvaluationPeriodId }, "uq_target_assignment_active_department")
                .IsUnique()
                .HasFilter("((department_id IS NOT NULL) AND (status = 'ACTIVE'::target_assignment_status))");

            entity.HasIndex(e => new { e.KpiDefinitionId, e.EmployeeId, e.EvaluationPeriodId }, "uq_target_assignment_active_employee")
                .IsUnique()
                .HasFilter("((employee_id IS NOT NULL) AND (status = 'ACTIVE'::target_assignment_status))");

            entity.HasIndex(e => new { e.KpiDefinitionId, e.DepartmentId, e.EvaluationPeriodId, e.Version }, "uq_target_assignment_version_department")
                .IsUnique()
                .HasFilter("(department_id IS NOT NULL)");

            entity.HasIndex(e => new { e.KpiDefinitionId, e.EmployeeId, e.EvaluationPeriodId, e.Version }, "uq_target_assignment_version_employee")
                .IsUnique()
                .HasFilter("(employee_id IS NOT NULL)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by");
            entity.Property(e => e.AssignedAt).HasColumnName("assigned_at");
            entity.Property(e => e.AssignedBy).HasColumnName("assigned_by");
            entity.Property(e => e.ChangeReason).HasColumnName("change_reason");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DepartmentId).HasColumnName("department_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EvaluationPeriodId).HasColumnName("evaluation_period_id");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.ParentAssignmentId).HasColumnName("parent_assignment_id");
            entity.Property(e => e.PreviousVersionId).HasColumnName("previous_version_id");
            entity.Property(e => e.TargetValue)
                .HasPrecision(18, 4)
                .HasColumnName("target_value");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Version)
                .HasDefaultValue(1)
                .HasColumnName("version");

            entity.HasOne(d => d.ApprovedByNavigation).WithMany(p => p.TargetAssignmentApprovedByNavigations)
                .HasForeignKey(d => d.ApprovedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_target_assignment_approved_by");

            entity.HasOne(d => d.AssignedByNavigation).WithMany(p => p.TargetAssignmentAssignedByNavigations)
                .HasForeignKey(d => d.AssignedBy)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_target_assignment_assigned_by");

            entity.HasOne(d => d.Department).WithMany(p => p.TargetAssignments)
                .HasForeignKey(d => d.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_department");

            entity.HasOne(d => d.Employee).WithMany(p => p.TargetAssignmentEmployees)
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_employee");

            entity.HasOne(d => d.EvaluationPeriod).WithMany(p => p.TargetAssignments)
                .HasForeignKey(d => d.EvaluationPeriodId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_period");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.TargetAssignments)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_kpi");

            entity.HasOne(d => d.ParentAssignment).WithMany(p => p.InverseParentAssignment)
                .HasForeignKey(d => d.ParentAssignmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_parent");

            entity.HasOne(d => d.PreviousVersion).WithMany(p => p.InversePreviousVersion)
                .HasForeignKey(d => d.PreviousVersionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_target_assignment_previous_version");

            entity.Property(e => e.Status)
                .HasColumnType("target_assignment_status")
                .HasColumnName("status");

            entity.Property(e => e.SplitDimension)
                .HasColumnType("target_split_dimension")
                .HasColumnName("split_dimension");
        });

        modelBuilder.Entity<TaskHandover>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("task_handover_pkey");

            entity.ToTable("task_handover", table =>
            {
                table.HasCheckConstraint("ck_task_handover_distinct", "from_employee_id <> to_employee_id");
                table.HasCheckConstraint("ck_task_handover_accepted_at", "accepted_at IS NULL OR accepted_at >= handover_at");
            });

            entity.HasIndex(e => e.WorkId, "ix_task_handover_work");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AcceptedAt).HasColumnName("accepted_at");
            entity.Property(e => e.FromEmployeeId).HasColumnName("from_employee_id");
            entity.Property(e => e.HandoverAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("handover_at");
            entity.Property(e => e.Reason).HasColumnName("reason");
            entity.Property(e => e.ToEmployeeId).HasColumnName("to_employee_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkId).HasColumnName("work_id");

            entity.HasOne(d => d.FromEmployee).WithMany(p => p.TaskHandoverFromEmployees)
                .HasForeignKey(d => d.FromEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_task_handover_from_employee");

            entity.HasOne(d => d.ToEmployee).WithMany(p => p.TaskHandoverToEmployees)
                .HasForeignKey(d => d.ToEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_task_handover_to_employee");

            entity.HasOne(d => d.Work).WithMany(p => p.TaskHandovers)
                .HasForeignKey(d => d.WorkId)
                .HasConstraintName("fk_task_handover_work");

            entity.Property(e => e.Status)
                .HasColumnType("handover_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("user_role_pkey");

            entity.ToTable("user_role");

            entity.HasIndex(e => e.RoleId, "ix_user_role_role");

            entity.HasIndex(e => new { e.AccountId, e.RoleId }, "uq_user_role").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AccountId).HasColumnName("account_id");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Account).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.AccountId)
                .HasConstraintName("fk_user_role_account");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_user_role_role");
        });

        modelBuilder.Entity<Verification>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("verification_pkey");

            entity.ToTable("verification", table =>
            {
                table.HasCheckConstraint("ck_verification_round", "round_no > 0");
                table.HasCheckConstraint("ck_verification_time", "(decision = 'PENDING' AND verified_at IS NULL)\r\n            OR\r\n            (decision <> 'PENDING' AND verified_at IS NOT NULL)");
            });

            entity.HasIndex(e => e.ResultSubmissionId, "ix_verification_submission");

            entity.HasIndex(e => new { e.ResultSubmissionId, e.RoundNo }, "uq_verification_round").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.ResultSubmissionId).HasColumnName("result_submission_id");
            entity.Property(e => e.RoundNo)
                .HasDefaultValue(1)
                .HasColumnName("round_no");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at");
            entity.Property(e => e.VerifierEmployeeId).HasColumnName("verifier_employee_id");

            entity.HasOne(d => d.ResultSubmission).WithMany(p => p.Verifications)
                .HasForeignKey(d => d.ResultSubmissionId)
                .HasConstraintName("fk_verification_submission");

            entity.HasOne(d => d.VerifierEmployee).WithMany(p => p.Verifications)
                .HasForeignKey(d => d.VerifierEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_verification_verifier");

            entity.Property(e => e.Decision)
                .HasColumnType("verification_decision")
                .HasColumnName("decision");
        });

        modelBuilder.Entity<VerifiedWorkResult>(entity =>
        {
            entity.HasIndex(e => e.Status, "ix_verified_work_result_status");

            entity.HasKey(e => e.Id).HasName("verified_work_result_pkey");

            entity.ToTable("verified_work_result", table =>
            {
                table.HasCheckConstraint("ck_verified_work_result_invalidated_at", "(status = 'VALID' AND invalidated_at IS NULL)\r\n            OR\r\n            (status = 'INVALID' AND invalidated_at IS NOT NULL)");
            });

            entity.HasIndex(e => e.InvalidatedByRiskEventId, "uq_verified_work_result_risk_event").IsUnique();

            entity.HasIndex(e => e.VerificationId, "uq_verified_work_result_verification").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.InvalidatedAt).HasColumnName("invalidated_at");
            entity.Property(e => e.InvalidatedByRiskEventId).HasColumnName("invalidated_by_risk_event_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.VerificationId).HasColumnName("verification_id");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at");

            entity.HasOne(d => d.InvalidatedByRiskEvent).WithOne(p => p.VerifiedWorkResult)
                .HasForeignKey<VerifiedWorkResult>(d => d.InvalidatedByRiskEventId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_verified_work_result_invalidator");

            entity.HasOne(d => d.Verification).WithOne(p => p.VerifiedWorkResult)
                .HasForeignKey<VerifiedWorkResult>(d => d.VerificationId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_verified_work_result_verification");

            entity.Property(e => e.Status)
                .HasColumnType("verified_result_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<Work>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("work_pkey");

            entity.ToTable("work", table =>
            {
                table.HasCheckConstraint("ck_work_estimated_hours", "estimated_hours IS NULL OR estimated_hours >= 0");
                table.HasCheckConstraint("ck_work_planned_dates", "planned_end_date IS NULL OR planned_start_date IS NULL OR planned_end_date >= planned_start_date");
            });

            entity.HasIndex(e => e.AssigneeEmployeeId, "ix_work_assignee");

            entity.HasIndex(e => e.BusinessCampaignId, "ix_work_campaign");

            entity.HasIndex(e => e.WorkTypeId, "ix_work_work_type");

            entity.HasIndex(e => e.Code, "uq_work_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.AssigneeEmployeeId).HasColumnName("assignee_employee_id");
            entity.Property(e => e.BusinessCampaignId).HasColumnName("business_campaign_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.EstimatedHours)
                .HasPrecision(10, 2)
                .HasColumnName("estimated_hours");
            entity.Property(e => e.PlannedEndDate).HasColumnName("planned_end_date");
            entity.Property(e => e.PlannedStartDate).HasColumnName("planned_start_date");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkTypeId).HasColumnName("work_type_id");

            entity.HasOne(d => d.AssigneeEmployee).WithMany(p => p.Works)
                .HasForeignKey(d => d.AssigneeEmployeeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_work_assignee");

            entity.HasOne(d => d.BusinessCampaign).WithMany(p => p.Works)
                .HasForeignKey(d => d.BusinessCampaignId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("fk_work_campaign");

            entity.HasOne(d => d.WorkType).WithMany(p => p.Works)
                .HasForeignKey(d => d.WorkTypeId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_work_work_type");

            entity.Property(e => e.Priority)
                .HasColumnType("work_priority")
                .HasColumnName("priority");

            entity.Property(e => e.Status)
                .HasColumnType("work_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<WorkResultItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("work_result_item_pkey");

            entity.ToTable("work_result_item", table =>
            {
                table.HasCheckConstraint("ck_work_result_item_value", "value >= 0");
                table.HasCheckConstraint("ck_work_result_item_denominator", "denominator IS NULL OR denominator > 0");
            });

            entity.HasIndex(e => e.KpiDefinitionId, "ix_work_result_item_kpi");

            entity.HasIndex(e => e.VerifiedWorkResultId, "ix_work_result_item_result");

            entity.HasIndex(e => new { e.VerifiedWorkResultId, e.KpiDefinitionId }, "uq_work_result_item_kpi").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Denominator)
                .HasPrecision(18, 4)
                .HasColumnName("denominator");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Value)
                .HasPrecision(18, 4)
                .HasColumnName("value");
            entity.Property(e => e.VerifiedWorkResultId).HasColumnName("verified_work_result_id");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.WorkResultItems)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_work_result_item_kpi");

            entity.HasOne(d => d.VerifiedWorkResult).WithMany(p => p.WorkResultItems)
                .HasForeignKey(d => d.VerifiedWorkResultId)
                .HasConstraintName("fk_work_result_item_verified_result");
        });

        modelBuilder.Entity<WorkType>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("work_type_pkey");

            entity.ToTable("work_type");

            entity.HasIndex(e => e.Code, "uq_work_type_code").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkflowDefinitionId).HasColumnName("workflow_definition_id");

            entity.HasOne(d => d.WorkflowDefinition).WithMany(p => p.WorkTypes)
                .HasForeignKey(d => d.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_work_type_workflow_definition");

            entity.Property(e => e.Status)
                .HasColumnType("record_status")
                .HasColumnName("status");
        });

        modelBuilder.Entity<WorkTypeKpi>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("work_type_kpi_pkey");

            entity.ToTable("work_type_kpi", table =>
            {
                table.HasCheckConstraint("ck_work_type_kpi_dates", "effective_to IS NULL OR effective_to >= effective_from");
            });

            entity.HasIndex(e => new { e.WorkTypeId, e.KpiDefinitionId, e.EffectiveFrom }, "uq_work_type_kpi_effective").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.EffectiveFrom)
                .HasDefaultValueSql("CURRENT_DATE")
                .HasColumnName("effective_from");
            entity.Property(e => e.EffectiveTo).HasColumnName("effective_to");
            entity.Property(e => e.IsRequired)
                .HasDefaultValue(false)
                .HasColumnName("is_required");
            entity.Property(e => e.KpiDefinitionId).HasColumnName("kpi_definition_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.WorkTypeId).HasColumnName("work_type_id");

            entity.HasOne(d => d.KpiDefinition).WithMany(p => p.WorkTypeKpis)
                .HasForeignKey(d => d.KpiDefinitionId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_work_type_kpi_kpi");

            entity.HasOne(d => d.WorkType).WithMany(p => p.WorkTypeKpis)
                .HasForeignKey(d => d.WorkTypeId)
                .HasConstraintName("fk_work_type_kpi_work_type");
        });

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_definition_pkey");

            entity.ToTable("workflow_definition", table =>
            {
                table.HasCheckConstraint("ck_workflow_definition_version", "version > 0");
            });

            entity.HasIndex(e => new { e.Code, e.Version }, "uq_workflow_definition_code_version").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("gen_random_uuid()")
                .HasColumnName("id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.Version)
                .HasDefaultValue(1)
                .HasColumnName("version");

            entity.Property(e => e.Status)
                .HasColumnType("workflow_definition_status")
                .HasColumnName("status");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
