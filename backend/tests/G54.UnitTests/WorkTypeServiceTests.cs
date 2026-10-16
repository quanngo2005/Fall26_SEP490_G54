using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.WorkType;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

public sealed class WorkTypeServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, WorkTypeService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new WorkTypeService(context, NullLogger<WorkTypeService>.Instance);
        return (context, service);
    }

    [Fact]
    public async Task GetActiveKpisAsync_ReturnsOnlyActiveKpis()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var group = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "GRP-01",
                Name = "Group 1",
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var activeKpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI-ACT",
                Name = "Active KPI",
                Unit = "pts",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var draftKpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI-DFT",
                Name = "Draft KPI",
                Unit = "pts",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Draft,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.KpiGroups.AddAsync(group);
            await context.KpiDefinitions.AddRangeAsync(activeKpi, draftKpi);
            await context.SaveChangesAsync();

            var result = await service.GetActiveKpisAsync();

            Assert.Single(result);
            Assert.Equal("KPI-ACT", result[0].Code);
        }
    }

    [Fact]
    public async Task GetWorkflowsAsync_ReturnsOnlyActiveWorkflows()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var activeWf = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-ACT",
                Name = "Active Workflow",
                Version = 1,
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var draftWf = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-DFT",
                Name = "Draft Workflow",
                Version = 1,
                Status = WorkflowDefinitionStatus.Draft,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddRangeAsync(activeWf, draftWf);
            await context.SaveChangesAsync();

            var result = await service.GetWorkflowsAsync();

            Assert.Single(result);
            Assert.Equal("WF-ACT", result[0].Code);
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_ValidData_CreatesSuccessfullyWithKpis()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-CARD",
                Name = "Card Workflow",
                Version = 1,
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var group = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "GRP-02",
                Name = "Group 2",
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var kpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI-REVENUE",
                Name = "Revenue",
                Unit = "VND",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.KpiGroups.AddAsync(group);
            await context.KpiDefinitions.AddAsync(kpi);
            await context.SaveChangesAsync();

            var dto = new CreateWorkTypeDto
            {
                Code = "WT-CARD-01",
                Name = "Credit Card Issuance",
                Description = "Process of issuing new credit cards",
                WorkflowDefinitionId = workflow.Id,
                Kpis =
                [
                    new WorkTypeKpiItemDto
                    {
                        KpiDefinitionId = kpi.Id,
                        IsRequired = true,
                        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                        EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)),
                    }
                ],
            };

            var created = await service.CreateWorkTypeAsync(dto);

            Assert.NotNull(created);
            Assert.Equal("WT-CARD-01", created.Code);
            Assert.Equal("Credit Card Issuance", created.Name);
            Assert.Equal("Card Workflow", created.WorkflowName);
            Assert.Equal(WorkTypeStatus.Active, created.Status);
            Assert.Single(created.Kpis);
            Assert.Equal("KPI-REVENUE", created.Kpis[0].KpiCode);
            Assert.True(created.Kpis[0].IsRequired);
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_DuplicateCode_ThrowsConflictException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-01",
                Name = "WF 1",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var existing = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-DUP",
                Name = "Existing",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.WorkTypes.AddAsync(existing);
            await context.SaveChangesAsync();

            var dto = new CreateWorkTypeDto
            {
                Code = "WT-DUP",
                Name = "New with duplicate code",
                WorkflowDefinitionId = workflow.Id,
            };

            await Assert.ThrowsAsync<ConflictException>(() => service.CreateWorkTypeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_NonExistentWorkflow_ThrowsNotFoundException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var dto = new CreateWorkTypeDto
            {
                Code = "WT-NOWORKFLOW",
                Name = "No Workflow",
                WorkflowDefinitionId = Guid.NewGuid(),
            };

            await Assert.ThrowsAsync<NotFoundException>(() => service.CreateWorkTypeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_InactiveKpi_ThrowsBusinessRuleException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-02",
                Name = "WF 2",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var group = new KpiGroup
            {
                Id = Guid.NewGuid(),
                Code = "GRP-03",
                Name = "Group 3",
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var inactiveKpi = new KpiDefinition
            {
                Id = Guid.NewGuid(),
                KpiGroupId = group.Id,
                Code = "KPI-INACT",
                Name = "Inactive KPI",
                Unit = "pts",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Inactive,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.KpiGroups.AddAsync(group);
            await context.KpiDefinitions.AddAsync(inactiveKpi);
            await context.SaveChangesAsync();

            var dto = new CreateWorkTypeDto
            {
                Code = "WT-TEST-KPI",
                Name = "Test Inactive KPI",
                WorkflowDefinitionId = workflow.Id,
                Kpis =
                [
                    new WorkTypeKpiItemDto
                    {
                        KpiDefinitionId = inactiveKpi.Id,
                        IsRequired = true,
                        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                    }
                ],
            };

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateWorkTypeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_InvalidDateRange_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-03",
                Name = "WF 3",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.SaveChangesAsync();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dto = new CreateWorkTypeDto
            {
                Code = "WT-DATES",
                Name = "Invalid Dates",
                WorkflowDefinitionId = workflow.Id,
                Kpis =
                [
                    new WorkTypeKpiItemDto
                    {
                        KpiDefinitionId = Guid.NewGuid(),
                        IsRequired = false,
                        EffectiveFrom = today,
                        EffectiveTo = today.AddDays(-5),
                    }
                ],
            };

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkTypeAsync(dto));
        }
    }

    [Fact]
    public async Task CreateWorkTypeAsync_DuplicateKpisInList_ThrowsValidationException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-04",
                Name = "WF 4",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.SaveChangesAsync();

            var kpiId = Guid.NewGuid();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var dto = new CreateWorkTypeDto
            {
                Code = "WT-DUP-KPIS",
                Name = "Duplicate KPIs",
                WorkflowDefinitionId = workflow.Id,
                Kpis =
                [
                    new WorkTypeKpiItemDto { KpiDefinitionId = kpiId, EffectiveFrom = today },
                    new WorkTypeKpiItemDto { KpiDefinitionId = kpiId, EffectiveFrom = today },
                ],
            };

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkTypeAsync(dto));
        }
    }

    [Fact]
    public async Task GetWorkTypesAsync_FiltersByKeywordAndStatus()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-05",
                Name = "WF 5",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt1 = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-LOAN-01",
                Name = "Consumer Loan",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt2 = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-LOAN-02",
                Name = "Mortgage Loan",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Inactive,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.WorkTypes.AddRangeAsync(wt1, wt2);
            await context.SaveChangesAsync();

            var filter = new WorkTypeFilterDto
            {
                Keyword = "Consumer",
                Status = WorkTypeStatus.Active,
            };

            var result = await service.GetWorkTypesAsync(filter);

            Assert.Equal(1, result.TotalCount);
            Assert.Single(result.Items);
            Assert.Equal("WT-LOAN-01", result.Items[0].Code);
        }
    }

    [Fact]
    public async Task GetWorkTypeByIdAsync_ExistingId_ReturnsDetailDto()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-06",
                Name = "Workflow 6",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-GET-01",
                Name = "Get Test",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.WorkTypes.AddAsync(wt);
            await context.SaveChangesAsync();

            var result = await service.GetWorkTypeByIdAsync(wt.Id);

            Assert.NotNull(result);
            Assert.Equal("WT-GET-01", result.Code);
            Assert.Equal("Workflow 6", result.WorkflowName);
        }
    }

    [Fact]
    public async Task UpdateWorkTypeAsync_ValidData_UpdatesWorkTypeAndKpis()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-07",
                Name = "Workflow 7",
                Status = WorkflowDefinitionStatus.Active,
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
                Code = "KPI-UPDATE",
                Name = "KPI Update",
                Unit = "pts",
                AggregationType = KpiAggregationType.Sum,
                Direction = KpiDirection.HigherBetter,
                Status = KpiStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-OLD",
                Name = "Old Name",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.KpiGroups.AddAsync(group);
            await context.KpiDefinitions.AddAsync(kpi);
            await context.WorkTypes.AddAsync(wt);
            await context.SaveChangesAsync();

            var updateDto = new UpdateWorkTypeDto
            {
                Code = "WT-NEW",
                Name = "New Name",
                Description = "Updated Description",
                WorkflowDefinitionId = workflow.Id,
                Kpis =
                [
                    new WorkTypeKpiItemDto
                    {
                        KpiDefinitionId = kpi.Id,
                        IsRequired = true,
                        EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
                    }
                ],
            };

            var updated = await service.UpdateWorkTypeAsync(wt.Id, updateDto);

            Assert.Equal("WT-NEW", updated.Code);
            Assert.Equal("New Name", updated.Name);
            Assert.Single(updated.Kpis);
            Assert.Equal("KPI-UPDATE", updated.Kpis[0].KpiCode);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ValidStatus_UpdatesStatus()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-08",
                Name = "Workflow 8",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-STATUS",
                Name = "Status Test",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.WorkTypes.AddAsync(wt);
            await context.SaveChangesAsync();

            await service.ChangeStatusAsync(wt.Id, new WorkTypeStatusDto { Status = WorkTypeStatus.Archived });

            var dbWt = await context.WorkTypes.FindAsync(wt.Id);
            Assert.NotNull(dbWt);
            Assert.Equal(RecordStatus.Archived, dbWt.Status);
        }
    }

    [Fact]
    public async Task DeleteWorkTypeAsync_NoRelatedWorks_DeletesSuccessfully()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-09",
                Name = "Workflow 9",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var wt = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-DEL",
                Name = "Delete Test",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.WorkTypes.AddAsync(wt);
            await context.SaveChangesAsync();

            await service.DeleteWorkTypeAsync(wt.Id);

            var dbWt = await context.WorkTypes.FindAsync(wt.Id);
            Assert.Null(dbWt);
        }
    }

    [Fact]
    public async Task DeleteWorkTypeAsync_HasRelatedWorks_ThrowsConflictException()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var workflow = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Code = "WF-10",
                Name = "Workflow 10",
                Status = WorkflowDefinitionStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP999",
                FullName = "Tester",
                Email = "test@bank.com",
                Status = EmployeeStatus.Active,
            };
            var wt = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-HAS-WORKS",
                Name = "Has Works Test",
                WorkflowDefinitionId = workflow.Id,
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = "W-0001",
                Title = "Test Work",
                WorkTypeId = wt.Id,
                AssigneeEmployeeId = employee.Id,
                Priority = WorkPriority.Medium,
                Status = WorkStatus.New,
                PlannedStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
                PlannedEndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkflowDefinitions.AddAsync(workflow);
            await context.Employees.AddAsync(employee);
            await context.WorkTypes.AddAsync(wt);
            await context.Works.AddAsync(work);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => service.DeleteWorkTypeAsync(wt.Id));
        }
    }
}
