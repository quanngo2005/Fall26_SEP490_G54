using System.Linq;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class AppDbContextModelTests
{
    [Fact]
    public void Model_MapsEverySqlTableAndPreservesPostgresTypes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=g54_model_test")
            .Options;
        using var context = new AppDbContext(options);
        var entityTypes = context.Model.GetEntityTypes();

        Assert.Equal(64, entityTypes.Count());
        Assert.Equal(60, entityTypes.Count(entity => entity.GetTableName() is not "auth_audit_log" and not "sidebar_configurations" and not "audit_logs" and not "system_parameters"));
        Assert.Equal("ranking_policy", context.Model.FindEntityType(typeof(RankingPolicy))?.GetTableName());
        var sidebar = context.Model.FindEntityType(typeof(SidebarConfiguration));
        Assert.NotNull(sidebar);
        Assert.Equal("text[]", sidebar.FindProperty(nameof(SidebarConfiguration.ItemIds))?.GetColumnType());
        Assert.True(sidebar.FindProperty(nameof(SidebarConfiguration.Version))?.IsConcurrencyToken);
        var enumProperties = entityTypes.SelectMany(entity => entity.GetProperties())
            .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum)
            .ToArray();
        Assert.Equal(47, enumProperties.Length);
        Assert.All(enumProperties, property => Assert.False(string.IsNullOrWhiteSpace(property.GetColumnType())));

        var account = context.Model.FindEntityType(typeof(Account));
        Assert.NotNull(account);
        Assert.Equal("timestamp with time zone", account.FindProperty(nameof(Account.LockedUntil))?.GetColumnType());

        var department = context.Model.FindEntityType(typeof(Department));
        Assert.NotNull(department);
        Assert.Equal(typeof(DepartmentLevelType), department.FindProperty(nameof(Department.LevelType))?.ClrType);
        Assert.Equal("department_level_type", department.FindProperty(nameof(Department.LevelType))?.GetColumnType());

        var contribution = context.Model.FindEntityType(typeof(PerformanceContribution));
        Assert.NotNull(contribution);
        Assert.Equal(18, contribution.FindProperty(nameof(PerformanceContribution.TargetValueSnapshot))?.GetPrecision());
        Assert.Equal(4, contribution.FindProperty(nameof(PerformanceContribution.TargetValueSnapshot))?.GetScale());

        var campaignCustomer = context.Model.FindEntityType(typeof(CampaignCustomer));
        Assert.NotNull(campaignCustomer);
        Assert.Equal("campaign_customer", campaignCustomer.GetTableName());
        Assert.Equal(typeof(CampaignContactStatus), campaignCustomer.FindProperty(nameof(CampaignCustomer.ContactStatus))?.ClrType);
        Assert.Equal("campaign_contact_status", campaignCustomer.FindProperty(nameof(CampaignCustomer.ContactStatus))?.GetColumnType());

        var campaignCustomerUniqueIndex = campaignCustomer.GetIndexes()
            .FirstOrDefault(index => index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(CampaignCustomer.BusinessCampaignId), nameof(CampaignCustomer.CustomerCif) }));
        Assert.NotNull(campaignCustomerUniqueIndex);

        var campaignCustomerAssigneeIndex = campaignCustomer.GetIndexes()
            .FirstOrDefault(index => !index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(CampaignCustomer.BusinessCampaignId), nameof(CampaignCustomer.AssigneeEmployeeId) }));
        Assert.NotNull(campaignCustomerAssigneeIndex);
        var rating = context.Model.FindEntityType(typeof(ServiceRating));
        Assert.NotNull(rating);
        Assert.Equal("service_rating", rating.GetTableName());
        Assert.Equal("smallint", rating.FindProperty(nameof(ServiceRating.Score))?.GetColumnType());
        Assert.Equal("date", rating.FindProperty(nameof(ServiceRating.RatingDate))?.GetColumnType());
        Assert.Contains(rating.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(ServiceRating.TransactionCode));
        Assert.Equal(DeleteBehavior.Cascade, rating.GetForeignKeys().Single(k => k.PrincipalEntityType.ClrType == typeof(ImportBatch)).DeleteBehavior);
        var fileType = context.Model.FindEntityType(typeof(ImportBatch))?.FindProperty(nameof(ImportBatch.FileType));
        Assert.Equal("character varying(20)", fileType?.GetColumnType());
        Assert.Equal(ImportFileType.Transaction, fileType?.GetDefaultValue());
        var qualityFlag = context.Model.FindEntityType(typeof(QualityFlag));
        Assert.NotNull(qualityFlag);
        Assert.Equal("reviewed_by_employee_id", qualityFlag.FindProperty(nameof(QualityFlag.ReviewedByEmployeeId))?.GetColumnName());
        Assert.Equal("reviewed_at", qualityFlag.FindProperty(nameof(QualityFlag.ReviewedAt))?.GetColumnName());
        Assert.Equal("character varying(2000)", qualityFlag.FindProperty(nameof(QualityFlag.DismissReason))?.GetColumnType());
        var reviewer = Assert.Single(qualityFlag.GetForeignKeys(), fk => fk.GetConstraintName() == "fk_quality_flag_reviewer");
        Assert.Equal(DeleteBehavior.Restrict, reviewer.DeleteBehavior);
        Assert.Equal(typeof(Employee), reviewer.PrincipalEntityType.ClrType);
    }

    [Fact]
    public void Model_MapsCallRecordingAiTriageTablesAndTheRecordingLinkOfRiskEvents()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=g54_model_test")
            .Options;
        using var context = new AppDbContext(options);

        var recording = context.Model.FindEntityType(typeof(CallRecording));
        Assert.NotNull(recording);
        Assert.Equal("call_recording", recording.GetTableName());
        Assert.Contains(recording.GetIndexes(), i => i.GetDatabaseName() == "ix_call_recording_queue"
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(CallRecording.ProcessingStatus), nameof(CallRecording.UploadedAt)]));
        Assert.Contains(recording.GetIndexes(), i => i.GetDatabaseName() == "ix_call_recording_submission");

        var transcript = context.Model.FindEntityType(typeof(CallTranscript));
        Assert.NotNull(transcript);
        Assert.Equal("jsonb", transcript.FindProperty(nameof(CallTranscript.Segments))?.GetColumnType());
        Assert.True(Assert.Single(transcript.GetIndexes(), i => i.GetDatabaseName() == "uq_call_transcript_recording").IsUnique);

        var flag = context.Model.FindEntityType(typeof(AiReviewFlag));
        Assert.NotNull(flag);
        Assert.Equal("ai_review_flag", flag.GetTableName());
        Assert.Equal("nextval('ai_review_flag_code_seq')", flag.FindProperty(nameof(AiReviewFlag.CodeNo))?.GetDefaultValueSql());
        Assert.Equal("jsonb", flag.FindProperty(nameof(AiReviewFlag.KbRefs))?.GetColumnType());
        Assert.Equal("jsonb", flag.FindProperty(nameof(AiReviewFlag.Details))?.GetColumnType());
        Assert.Contains(flag.GetIndexes(), i => i.GetDatabaseName() == "ix_ai_review_flag_status"
            && i.Properties.Select(p => p.Name).SequenceEqual([nameof(AiReviewFlag.Status), nameof(AiReviewFlag.RankScore)]));
        Assert.Contains(flag.GetIndexes(), i => i.GetDatabaseName() == "ix_ai_review_flag_recording");

        var riskEvent = context.Model.FindEntityType(typeof(RiskEvent));
        Assert.NotNull(riskEvent);
        Assert.Equal("call_recording_id", riskEvent.FindProperty(nameof(RiskEvent.CallRecordingId))?.GetColumnName());
        Assert.Equal("media_start_ms", riskEvent.FindProperty(nameof(RiskEvent.MediaStartMs))?.GetColumnName());
        Assert.Equal("media_end_ms", riskEvent.FindProperty(nameof(RiskEvent.MediaEndMs))?.GetColumnName());
        var flagLink = Assert.Single(riskEvent.GetIndexes(), i => i.GetDatabaseName() == "uq_risk_event_ai_review_flag");
        Assert.True(flagLink.IsUnique);
        Assert.Equal("ai_review_flag_id IS NOT NULL", flagLink.GetFilter());
    }
}
