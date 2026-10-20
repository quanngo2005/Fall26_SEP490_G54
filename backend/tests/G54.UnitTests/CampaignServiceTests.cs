using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Campaign;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

public sealed class CampaignServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, CampaignService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var service = new CampaignService(context, NullLogger<CampaignService>.Instance);
        return (context, service);
    }

    private static async Task<(BusinessObjective Objective, WorkType WorkType, Employee Emp1, Employee Emp2)> SeedBasicTestDataAsync(AppDbContext context)
    {
        var objective = new BusinessObjective
        {
            Id = Guid.NewGuid(),
            Code = "OBJ-2026-TEST",
            Name = "Mục tiêu Kinh doanh 2026",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = BusinessObjectiveStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var dept = new Department
        {
            Id = Guid.NewGuid(),
            Code = "DEPT-KHCN",
            Name = "Phòng Khách hàng Cá nhân",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var pos = new JobPosition
        {
            Id = Guid.NewGuid(),
            Code = "POS-GDV",
            Title = "Giao dịch viên",
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var emp1 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-0001",
            FullName = "Nguyễn Văn A",
            Email = "nva@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var emp2 = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV-0002",
            FullName = "Trần Thị B",
            Email = "ttb@g54.test",
            DepartmentId = dept.Id,
            JobPositionId = pos.Id,
            Status = EmployeeStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var wf = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Code = "WF-STD",
            Name = "Quy trình Tiêu chuẩn",
            Status = WorkflowDefinitionStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-CALL",
            Name = "Tư vấn Tiết kiệm",
            WorkflowDefinitionId = wf.Id,
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await context.Departments.AddAsync(dept);
        await context.JobPositions.AddAsync(pos);
        await context.Employees.AddRangeAsync(emp1, emp2);
        await context.WorkflowDefinitions.AddAsync(wf);
        await context.WorkTypes.AddAsync(workType);
        await context.BusinessObjectives.AddAsync(objective);
        await context.SaveChangesAsync();

        return (objective, workType, emp1, emp2);
    }

    [Fact]
    public async Task GetCampaignsAsync_ReturnsMetricsAndPagedCampaigns()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var c1 = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-AUTUMN-01",
                Name = "Chiến dịch Mùa thu",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var c2 = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-WINTER-02",
                Name = "Chiến dịch Mùa đông",
                StartDate = new DateOnly(2026, 11, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddRangeAsync(c1, c2);
            await context.SaveChangesAsync();

            var result = await service.GetCampaignsAsync(null, null, null, 1, 10);

            Assert.NotNull(result);
            Assert.Equal(2, result.Metrics.TotalCampaigns);
            Assert.Equal(1, result.Metrics.RunningCampaigns);
            Assert.Equal(1, result.Metrics.PlannedCampaigns);
            Assert.Equal(2, result.Campaigns.TotalCount);
            Assert.Equal(2, result.Campaigns.Items.Count);
        }
    }

    [Fact]
    public async Task GetCampaignsAsync_FiltersBySearchAndStatus()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, _, _) = await SeedBasicTestDataAsync(context);

            var c1 = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-DEPOSIT",
                Name = "Huy động tiền gửi",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var c2 = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-LOAN",
                Name = "Gói vay tiêu dùng",
                StartDate = new DateOnly(2026, 11, 1),
                EndDate = new DateOnly(2026, 12, 31),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddRangeAsync(c1, c2);
            await context.SaveChangesAsync();

            var searchResult = await service.GetCampaignsAsync("tiền gửi", null, null, 1, 10);
            Assert.Single(searchResult.Campaigns.Items);
            Assert.Equal("CMP-DEPOSIT", searchResult.Campaigns.Items[0].Code);

            var statusResult = await service.GetCampaignsAsync(null, "PLANNED", null, 1, 10);
            Assert.Single(statusResult.Campaigns.Items);
            Assert.Equal("CMP-LOAN", statusResult.Campaigns.Items[0].Code);
        }
    }

    [Fact]
    public async Task GetCampaignByIdAsync_ReturnsDetailsAndCalculatesMetrics()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-DETAIL-01",
                Name = "Chiến dịch Xem chi tiết",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p1 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var cwt = new CampaignWorkType
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                WorkTypeId = workType.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var w1 = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-001",
                Title = "Nhiệm vụ 1",
                BusinessCampaignId = campaign.Id,
                AssigneeEmployeeId = emp1.Id,
                WorkTypeId = workType.Id,
                Status = WorkStatus.Done,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var w2 = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-002",
                Title = "Nhiệm vụ 2",
                BusinessCampaignId = campaign.Id,
                AssigneeEmployeeId = emp1.Id,
                WorkTypeId = workType.Id,
                Status = WorkStatus.InProgress,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(p1);
            await context.CampaignWorkTypes.AddAsync(cwt);
            await context.Works.AddRangeAsync(w1, w2);
            await context.SaveChangesAsync();

            var detail = await service.GetCampaignByIdAsync(campaign.Id);

            Assert.NotNull(detail);
            Assert.Equal("CMP-DETAIL-01", detail.Code);
            Assert.Single(detail.WorkTypes);
            Assert.Single(detail.Participants);
            Assert.Equal(2, detail.Metrics.TotalWorksCount);
            Assert.Equal(1, detail.Metrics.CompletedWorksCount);
            Assert.Equal(1, detail.Metrics.InExecutionWorksCount);
            Assert.Equal(50.0m, detail.Metrics.ProgressPercent);
            Assert.True(detail.CanGenerateBatchWork);
            Assert.False(detail.CanApprove); // Running status cannot be approved again
        }
    }

    [Fact]
    public async Task CreateCampaignAsync_Success_CreatesPlannedCampaign()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var request = new CreateCampaignRequestDto(
                BusinessObjectiveId: objective.Id,
                Code: "CMP-NEW-01",
                Name: "Chiến dịch Mới Tạo",
                Description: "Mô tả chiến dịch",
                StartDate: new DateOnly(2026, 9, 1),
                EndDate: new DateOnly(2026, 11, 30),
                WorkTypeIds: [workType.Id],
                ParticipantEmployeeIds: [emp1.Id, emp2.Id]);

            var result = await service.CreateCampaignAsync(request, Guid.NewGuid());

            Assert.NotNull(result);
            Assert.Equal("CMP-NEW-01", result.Code);
            Assert.Equal("PLANNED", result.Status);
            Assert.Equal(2, result.Participants.Count);
            Assert.Single(result.WorkTypes);
            Assert.True(result.CanApprove);
        }
    }

    [Fact]
    public async Task CreateCampaignAsync_ThrowsConflict_WhenCodeExists()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, _, _) = await SeedBasicTestDataAsync(context);

            var existing = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-DUP-01",
                Name = "Chiến dịch đã có",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.BusinessCampaigns.AddAsync(existing);
            await context.SaveChangesAsync();

            var request = new CreateCampaignRequestDto(
                BusinessObjectiveId: objective.Id,
                Code: "CMP-DUP-01",
                Name: "Chiến dịch Trùng mã",
                Description: null,
                StartDate: new DateOnly(2026, 9, 1),
                EndDate: new DateOnly(2026, 11, 30));

            await Assert.ThrowsAsync<ConflictException>(() =>
                service.CreateCampaignAsync(request, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CreateCampaignAsync_ThrowsValidation_WhenDatesInvalid()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, _, _) = await SeedBasicTestDataAsync(context);

            var request = new CreateCampaignRequestDto(
                BusinessObjectiveId: objective.Id,
                Code: "CMP-BAD-DATES",
                Name: "Sai ngày",
                Description: null,
                StartDate: new DateOnly(2026, 11, 30),
                EndDate: new DateOnly(2026, 9, 1)); // End < Start

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.CreateCampaignAsync(request, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task UpdateCampaignAsync_Success_UpdatesFieldsAndRelations()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-EDIT-01",
                Name = "Chiến dịch Ban đầu",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.SaveChangesAsync();

            var updateReq = new UpdateCampaignRequestDto(
                Name: "Chiến dịch Đã Cập Nhật",
                Description: "Cập nhật mô tả mới",
                StartDate: new DateOnly(2026, 9, 5),
                EndDate: new DateOnly(2026, 12, 15),
                WorkTypeIds: [workType.Id],
                ParticipantEmployeeIds: [emp1.Id]);

            var updated = await service.UpdateCampaignAsync(campaign.Id, updateReq, Guid.NewGuid());

            Assert.Equal("Chiến dịch Đã Cập Nhật", updated.Name);
            Assert.Equal(new DateOnly(2026, 9, 5), updated.StartDate);
            Assert.Equal(new DateOnly(2026, 12, 15), updated.EndDate);
            Assert.Single(updated.Participants);
            Assert.Equal(emp1.Id, updated.Participants[0].EmployeeId);
        }
    }

    [Fact]
    public async Task UpdateCampaignAsync_ThrowsMSG18_WhenEndDateEarlierThanTasks()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-BOUND-01",
                Name = "Chiến dịch Giới hạn Ngày",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-BOUND-01",
                Title = "Nhiệm vụ có hạn 25/11",
                BusinessCampaignId = campaign.Id,
                AssigneeEmployeeId = emp1.Id,
                PlannedEndDate = new DateOnly(2026, 11, 25),
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Works.AddAsync(work);
            await context.SaveChangesAsync();

            // Try to set campaign end date to 20/11 (< 25/11)
            var updateReq = new UpdateCampaignRequestDto(
                Name: "Sửa ngày kết thúc quá sớm",
                Description: null,
                StartDate: new DateOnly(2026, 9, 1),
                EndDate: new DateOnly(2026, 11, 20));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.UpdateCampaignAsync(campaign.Id, updateReq, Guid.NewGuid()));

            Assert.Equal(ErrorCodes.MSG18, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task DeleteCampaignAsync_Success_WhenNoWorksGenerated()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, _, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-DEL-01",
                Name = "Chiến dịch Chưa chạy",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.SaveChangesAsync();

            await service.DeleteCampaignAsync(campaign.Id, Guid.NewGuid());

            var deleted = await context.BusinessCampaigns.FindAsync(campaign.Id);
            Assert.Null(deleted);
        }
    }

    [Fact]
    public async Task DeleteCampaignAsync_ThrowsConflict_WhenWorksAlreadyGenerated()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, emp1, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-HAS-WORKS",
                Name = "Chiến dịch đã có task",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-01",
                Title = "Task 1",
                BusinessCampaignId = campaign.Id,
                AssigneeEmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Works.AddAsync(work);
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.DeleteCampaignAsync(campaign.Id, Guid.NewGuid()));

            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task DecideCampaignAsync_Approve_TransitionsToRunning()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, _, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-APPROVE-01",
                Name = "Chiến dịch Chờ duyệt",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.SaveChangesAsync();

            var req = new CampaignDecisionRequestDto("APPROVE", "Đồng ý phê duyệt", 50000000m);
            var result = await service.DecideCampaignAsync(campaign.Id, req, Guid.NewGuid());

            Assert.Equal("RUNNING", result.Status);
            var updated = await context.BusinessCampaigns.FindAsync(campaign.Id);
            Assert.Equal(BusinessCampaignStatus.Running, updated!.Status);
        }
    }

    [Fact]
    public async Task DecideCampaignAsync_ThrowsMSG10_WhenApproverIsParticipant_BR02()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, _, emp1, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-MAKER-CHECKER",
                Name = "Chiến dịch BR-02",
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 11, 30),
                Status = BusinessCampaignStatus.Planned,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var part = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(part);
            await context.SaveChangesAsync();

            // Employee 1 attempts to approve own campaign plan
            var req = new CampaignDecisionRequestDto("APPROVE");
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.DecideCampaignAsync(campaign.Id, req, emp1.Id));

            Assert.Equal(ErrorCodes.MSG10, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task BatchGenerateWorkAsync_RoundRobin_GeneratesTasksEvenlyWithBR25()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-BATCH-RR",
                Name = "Chiến dịch Batch Round Robin",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p1 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p2 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp2.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddRangeAsync(p1, p2);
            await context.SaveChangesAsync();

            var req = new BatchWorkGeneratorRequestDto(
                WorkTypeId: workType.Id,
                LeadPoolSource: "Pool KH Đáo hạn tháng 10",
                AllocationRule: "ROUND_ROBIN",
                TotalLeadsCount: 6,
                WorkTitleTemplate: "Tiếp cận khách hàng {CustomerName} - {CampaignCode}",
                EstimatedHours: 1.5m,
                PlannedStartDate: new DateOnly(2026, 10, 2),
                PlannedEndDate: new DateOnly(2026, 10, 20),
                Priority: "HIGH",
                Leads:
                [
                    new BatchLeadItemDto("CUST-001", "Nguyễn An", "Khách hàng thân thiết"),
                    new BatchLeadItemDto("CUST-002", "Trần Bình", "Tiền gửi 300tr")
                ]);

            var result = await service.BatchGenerateWorkAsync(campaign.Id, req, Guid.NewGuid());

            Assert.Equal(6, result.TotalGenerated);
            Assert.Equal(2, result.ParticipantCount);
            Assert.Equal(3.0m, result.TasksPerParticipantAverage);
            Assert.Equal(6, result.GeneratedTaskCodes.Count);

            var generatedWorks = await context.Works.Where(w => w.BusinessCampaignId == campaign.Id).ToListAsync();
            Assert.Equal(6, generatedWorks.Count);

            // Verify BR-25 7-day exclusivity note is present
            foreach (var w in generatedWorks)
            {
                Assert.Contains("theo BR-25", w.Description);
                Assert.Contains("Khóa độc quyền 7 ngày", w.Description);
                Assert.StartsWith("TSK-CMP-BATCH-RR-", w.Code);
            }

            // Verify Round-Robin alternation
            var emp1WorksCount = generatedWorks.Count(w => w.AssigneeEmployeeId == emp1.Id);
            var emp2WorksCount = generatedWorks.Count(w => w.AssigneeEmployeeId == emp2.Id);
            Assert.Equal(3, emp1WorksCount);
            Assert.Equal(3, emp2WorksCount);
        }
    }

    [Fact]
    public async Task BatchGenerateWorkAsync_LoadBalancing_AssignsToParticipantWithLowestLoad()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, emp2) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-LOAD-BAL",
                Name = "Chiến dịch Cân bằng tải",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p1 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p2 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp2.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            // Pre-seed emp1 with 2 active tasks, emp2 with 0 tasks
            var existingWork1 = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-PRE-01",
                Title = "Việc cũ 1",
                AssigneeEmployeeId = emp1.Id,
                Status = WorkStatus.InProgress,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var existingWork2 = new Work
            {
                Id = Guid.NewGuid(),
                Code = "TSK-PRE-02",
                Title = "Việc cũ 2",
                AssigneeEmployeeId = emp1.Id,
                Status = WorkStatus.New,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddRangeAsync(p1, p2);
            await context.Works.AddRangeAsync(existingWork1, existingWork2);
            await context.SaveChangesAsync();

            // Generate 3 tasks with LOAD_BALANCING:
            // Initial: emp1 has 2, emp2 has 0
            // Task 1 -> emp2 (emp2 now 1)
            // Task 2 -> emp2 (emp2 now 2)
            // Task 3 -> emp1 or emp2 (both at 2)
            var req = new BatchWorkGeneratorRequestDto(
                WorkTypeId: workType.Id,
                LeadPoolSource: "Pool Tải",
                AllocationRule: "LOAD_BALANCING",
                TotalLeadsCount: 3);

            var result = await service.BatchGenerateWorkAsync(campaign.Id, req, Guid.NewGuid());
            Assert.Equal(3, result.TotalGenerated);

            var campaignTasks = await context.Works.Where(w => w.BusinessCampaignId == campaign.Id).ToListAsync();
            var emp2CampaignTasks = campaignTasks.Count(w => w.AssigneeEmployeeId == emp2.Id);
            // Emp2 started with 0 so must have received at least 2 of the 3 tasks
            Assert.True(emp2CampaignTasks >= 2);
        }
    }

    [Fact]
    public async Task BatchGenerateWorkAsync_ThrowsMSG18_WhenDeadlineExceedsCampaignEndDate()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, emp1, _) = await SeedBasicTestDataAsync(context);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-MSG18",
                Name = "Chiến dịch Hạn chót",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 15),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p1 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = emp1.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(p1);
            await context.SaveChangesAsync();

            // Work planned end date 2026-10-20 exceeds campaign end date 2026-10-15
            var req = new BatchWorkGeneratorRequestDto(
                WorkTypeId: workType.Id,
                LeadPoolSource: "Test Pool",
                AllocationRule: "ROUND_ROBIN",
                TotalLeadsCount: 1,
                PlannedEndDate: new DateOnly(2026, 10, 20));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.BatchGenerateWorkAsync(campaign.Id, req, Guid.NewGuid()));

            Assert.Equal(ErrorCodes.MSG18, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task BatchGenerateWorkAsync_ThrowsValidationError_WhenNoActiveParticipants_BR41()
    {
        var (context, service) = CreateTestEnvironment();
        using (context)
        {
            var (objective, workType, _, _) = await SeedBasicTestDataAsync(context);

            var dept = await context.Departments.FirstAsync();
            var pos = await context.JobPositions.FirstAsync();

            // Inactive employee
            var inactiveEmp = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV-INACTIVE",
                FullName = "Nhân viên đã nghỉ việc",
                Email = "inactive@g54.test",
                DepartmentId = dept.Id,
                JobPositionId = pos.Id,
                Status = EmployeeStatus.Inactive,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await context.Employees.AddAsync(inactiveEmp);

            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                BusinessObjectiveId = objective.Id,
                Code = "CMP-BR41",
                Name = "Chiến dịch chỉ có nhân sự Inactive",
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                Status = BusinessCampaignStatus.Running,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var p1 = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = inactiveEmp.Id,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(p1);
            await context.SaveChangesAsync();

            var req = new BatchWorkGeneratorRequestDto(
                WorkTypeId: workType.Id,
                LeadPoolSource: "Test Pool",
                AllocationRule: "ROUND_ROBIN",
                TotalLeadsCount: 5);

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.BatchGenerateWorkAsync(campaign.Id, req, Guid.NewGuid()));

            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
            Assert.Contains("BR-41", ex.Message);
        }
    }
}
