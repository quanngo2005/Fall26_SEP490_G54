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

        Assert.Equal(36, entityTypes.Count());
        Assert.Equal(35, entityTypes.Count(entity => entity.GetTableName() != "auth_audit_log"));
        var enumProperties = entityTypes.SelectMany(entity => entity.GetProperties())
            .Where(property => (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).IsEnum)
            .ToArray();
        Assert.Equal(29, enumProperties.Length);
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
    }
}
