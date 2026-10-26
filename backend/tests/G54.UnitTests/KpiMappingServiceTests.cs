using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.KpiDefinition;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

public sealed class KpiMappingServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetObjectiveKpisAsync_ExistingObjective_ReturnsMappedKpis()
    {
        using var context = CreateInMemoryDbContext();
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-01",
            Name = "Expand Market Share",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6)),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP-MKT",
            Name = "Marketing",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-LEADS",
            Name = "Qualified Leads",
            Unit = "Leads",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var mapping = new ObjectiveKpi
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objective.Id,
            KpiDefinitionId = kpi.Id,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.BusinessObjectives.AddAsync(objective);
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.ObjectiveKpis.AddAsync(mapping);
        await context.SaveChangesAsync();

        var service = new KpiMappingService(context);
        var result = await service.GetObjectiveKpisAsync(objective.Id);

        Assert.Single(result);
        Assert.Equal("KPI-LEADS", result[0].KpiCode);
    }

    [Fact]
    public async Task AddObjectiveKpiAsync_ActiveKpi_MapsSuccessfully()
    {
        using var context = CreateInMemoryDbContext();
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-02",
            Name = "Increase Revenue",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP-REV",
            Name = "Revenue Group",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-REV-GROWTH",
            Name = "Revenue Growth",
            Unit = "%",
            AggregationType = KpiAggregationType.Avg,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.BusinessObjectives.AddAsync(objective);
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.SaveChangesAsync();

        var service = new KpiMappingService(context);
        var dto = new ObjectiveKpiSaveDto { KpiDefinitionId = kpi.Id };

        var mapped = await service.AddObjectiveKpiAsync(objective.Id, dto);

        Assert.NotNull(mapped);
        Assert.Equal("KPI-REV-GROWTH", mapped.KpiCode);
        Assert.Equal(objective.Id, mapped.BusinessObjectiveId);
    }

    [Fact]
    public async Task AddObjectiveKpiAsync_DraftKpi_ThrowsBusinessRuleException()
    {
        using var context = CreateInMemoryDbContext();
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-03",
            Name = "Objective Test",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP-GEN",
            Name = "General",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-DRAFT",
            Name = "Draft KPI",
            Unit = "count",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Draft,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.BusinessObjectives.AddAsync(objective);
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.SaveChangesAsync();

        var service = new KpiMappingService(context);
        var dto = new ObjectiveKpiSaveDto { KpiDefinitionId = kpi.Id };

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.AddObjectiveKpiAsync(objective.Id, dto));
    }

    [Fact]
    public async Task AddObjectiveKpiAsync_AlreadyMapped_ThrowsConflictException()
    {
        using var context = CreateInMemoryDbContext();
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-04",
            Name = "Objective Test 4",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var group = new KpiGroup
        {
            Id = Guid.NewGuid(),
            Code = "GRP-04",
            Name = "Group 4",
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-ALREADY-MAPPED",
            Name = "Already Mapped KPI",
            Unit = "count",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var mapping = new ObjectiveKpi
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objective.Id,
            KpiDefinitionId = kpi.Id,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.BusinessObjectives.AddAsync(objective);
        await context.KpiGroups.AddAsync(group);
        await context.KpiDefinitions.AddAsync(kpi);
        await context.ObjectiveKpis.AddAsync(mapping);
        await context.SaveChangesAsync();

        var service = new KpiMappingService(context);
        var dto = new ObjectiveKpiSaveDto { KpiDefinitionId = kpi.Id };

        await Assert.ThrowsAsync<ConflictException>(() => service.AddObjectiveKpiAsync(objective.Id, dto));
    }

    [Fact]
    public async Task RemoveObjectiveKpiAsync_ExistingMapping_RemovesSuccessfully()
    {
        using var context = CreateInMemoryDbContext();
        var objectiveId = Guid.NewGuid();
        var kpiId = Guid.NewGuid();

        var objective = new BusinessObjective
        {
            Id = objectiveId,
            Code = "OBJ-05",
            Name = "Objective Test 5",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var mapping = new ObjectiveKpi
        {
            Id = Guid.NewGuid(),
            BusinessObjectiveId = objectiveId,
            KpiDefinitionId = kpiId,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await context.BusinessObjectives.AddAsync(objective);
        await context.ObjectiveKpis.AddAsync(mapping);
        await context.SaveChangesAsync();

        var service = new KpiMappingService(context);
        await service.RemoveObjectiveKpiAsync(objectiveId, kpiId);

        var exists = await context.ObjectiveKpis.AnyAsync(m => m.BusinessObjectiveId == objectiveId && m.KpiDefinitionId == kpiId);
        Assert.False(exists);
    }
}
