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

        Assert.Equal(59, entityTypes.Count());
        Assert.Equal(55, entityTypes.Count(entity => entity.GetTableName() is not "auth_audit_log" and not "sidebar_configurations" and not "audit_logs" and not "system_parameters"));
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
    }
}
