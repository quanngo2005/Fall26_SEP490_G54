using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class KpiDefinitionServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetKpisAsync_WithFilterAndPagination_ReturnsExpectedResults()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "FIN",
            Name = "Finance",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);

        var kpi1 = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-REV",
            Name = "Revenue",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi2 = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-COST",
            Name = "Operating Cost",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.LowerBetter,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiDefinitions.AddRangeAsync(kpi1, kpi2);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);

        var filter = new KpiFilterDto
        {
            Keyword = "REV",
            Status = KpiStatus.Active,
            Page = 1,
            PageSize = 10,
        };

        var result = await service.GetKpisAsync(filter);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("KPI-REV", result.Items[0].Code);
    }

    [Fact]
    public async Task GetKpiByIdAsync_ExistingId_ReturnsDto()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "SALES",
            Name = "Sales",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);

        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-DEAL",
            Name = "Closed Deals",
            Unit = "Deals",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiDefinitions.AddAsync(kpi);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        var result = await service.GetKpiByIdAsync(kpi.Id);

        Assert.NotNull(result);
        Assert.Equal(kpi.Id, result.Id);
        Assert.Equal("Sales", result.KpiGroupName);
    }

    [Fact]
    public async Task GetKpiByIdAsync_NonExistingId_ReturnsNull()
    {
        using var context = CreateInMemoryDbContext();
        var service = new KpiDefinitionService(context);

        var result = await service.GetKpiByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task GetGroupOptionsAsync_ReturnsOnlyActiveGroups()
    {
        using var context = CreateInMemoryDbContext();
        var activeGroup = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "ACT",
            Name = "Active Group",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var inactiveGroup = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "INACT",
            Name = "Inactive Group",
            Status = KpiStatus.Inactive,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddRangeAsync(activeGroup, inactiveGroup);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        var options = await service.GetGroupOptionsAsync();

        Assert.Single(options);
        Assert.Equal("ACT", options[0].Code);
    }

    [Fact]
    public async Task CreateKpiAsync_ValidData_CreatesAsDraft()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "OPS",
            Name = "Operations",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        var dto = new KpiDefinitionSaveDto
        {
            KpiGroupId = group.Id,
            Code = "KPI-SLA",
            Name = "SLA Compliance",
            Description = "Percentage of requests handled within SLA",
            Unit = "%",
            AggregationType = KpiAggregationType.Avg,
            Direction = KpiDirection.HigherBetter,
            MaxAchievementPercent = 120,
        };

        var created = await service.CreateKpiAsync(dto);

        Assert.NotNull(created);
        Assert.Equal("KPI-SLA", created.Code);
        Assert.Equal(KpiStatus.Draft, created.Status);
    }

    [Fact]
    public async Task CreateKpiAsync_DuplicateCode_ThrowsConflictException()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "OPS",
            Name = "Operations",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var existingKpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-DUP",
            Name = "Duplicate Test",
            Unit = "%",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(existingKpi);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        var dto = new KpiDefinitionSaveDto
        {
            KpiGroupId = group.Id,
            Code = "KPI-DUP",
            Name = "Another Duplicate",
            Unit = "%",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
        };

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateKpiAsync(dto));
    }

    [Fact]
    public async Task CreateKpiAsync_NonExistentGroup_ThrowsNotFoundException()
    {
        using var context = CreateInMemoryDbContext();
        var service = new KpiDefinitionService(context);

        var dto = new KpiDefinitionSaveDto
        {
            KpiGroupId = Guid.NewGuid(),
            Code = "KPI-NOGROUP",
            Name = "No Group",
            Unit = "%",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
        };

        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateKpiAsync(dto));
    }

    [Fact]
    public async Task CreateKpiAsync_InvalidInput_ThrowsValidationException()
    {
        using var context = CreateInMemoryDbContext();
        var service = new KpiDefinitionService(context);

        var dto = new KpiDefinitionSaveDto
        {
            KpiGroupId = Guid.Empty,
            Code = "",
            Name = "",
            Unit = "",
        };

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateKpiAsync(dto));
    }

    [Fact]
    public async Task UpdateKpiAsync_ValidData_UpdatesSuccessfully()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "HR",
            Name = "Human Resources",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-HR-OLD",
            Name = "Old Name",
            Unit = "Persons",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        var updateDto = new KpiDefinitionSaveDto
        {
            KpiGroupId = group.Id,
            Code = "KPI-HR-NEW",
            Name = "New Name",
            Unit = "Persons",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            MaxAchievementPercent = 100,
        };

        var updated = await service.UpdateKpiAsync(kpi.Id, updateDto);

        Assert.Equal("KPI-HR-NEW", updated.Code);
        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task ChangeStatusAsync_ValidStatus_UpdatesStatus()
    {
        using var context = CreateInMemoryDbContext();
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP",
            Name = "Group",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-STATUS",
            Name = "Status Test",
            Unit = "pts",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.SaveChangesAsync();

        var service = new KpiDefinitionService(context);
        await service.ChangeStatusAsync(kpi.Id, new KpiStatusDto { Status = KpiStatus.Active });

        var updated = await context.KpiDefinitions.FindAsync(kpi.Id);
        Assert.NotNull(updated);
        Assert.Equal(KpiStatus.Active, updated.Status);
    }
}
