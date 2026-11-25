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

        Assert.Equal(58, entityTypes.Count());
        Assert.Equal(54, entityTypes.Count(entity => entity.GetTableName() is not "auth_audit_log" and not "sidebar_configurations" and not "audit_logs" and not "system_parameters"));
        Assert.Equal("ranking_policy", context.Model.FindEntityType(typeof(RankingPolicy))?.GetTableName());
        var sidebar = context.Model.FindEntityType(typeof(SidebarConfiguration));
        Assert.NotNull(sidebar);
        Assert.Equal("text[]", sidebar.FindProperty(nameof(SidebarConfiguration.ItemIds))?.GetColumnType());
        Assert.True(sidebar.FindProperty(nameof(SidebarConfiguration.Version))?.IsConcurrencyToken);
        var enumProperties = entityTypes.SelectMany(entity => entity.GetProperties())
            .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum)
            .ToArray();
        Assert.Equal(43, enumProperties.Length);
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
}
