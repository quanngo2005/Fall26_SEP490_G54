using System.Globalization;
using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class WorkServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static (AppDbContext Context, IUnitOfWork Uow, WorkService Service) CreateTestEnvironment()
    {
        var context = CreateInMemoryDbContext();
        var uow = new UnitOfWork(context);
        var service = new WorkService(uow, NullLogger<WorkService>.Instance, TimeProvider.System, new FakePermissionService(null, FakePermissionService.WorkCreate("All")));
        return (context, uow, service);
    }

    [Fact]
    public async Task CreateWorkAsync_ValidData_CreatesSuccessfully()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CARD",
                Name = "Credit Card Issuance",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP001",
                FullName = "Nguyen Van A",
                Email = "a.nguyen@bank.com",
                Status = EmployeeStatus.Active,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Phat hanh the tin dung cho KH ca nhan",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id,
                Priority: "High",
                PlannedStartDate: new DateOnly(2026, 10, 15),
                PlannedEndDate: new DateOnly(2026, 10, 20),
                EstimatedHours: 8.5m,
                Description: "Mo the JCB");

            var result = await service.CreateWorkAsync(request, employee.Id);

            Assert.NotNull(result);
            Assert.StartsWith("WRK-", result.Code);
            Assert.Equal(request.Title, result.Title);
            Assert.Equal("Credit Card Issuance", result.WorkTypeName);
            Assert.Equal("Nguyen Van A", result.AssigneeEmployeeName);
            Assert.Equal("High", result.Priority);
            Assert.Equal("New", result.Status);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_EmptyTitle_ThrowsValidationException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var request = new CreateWorkRequestDto(
                Title: "   ",
                WorkTypeId: Guid.NewGuid(),
                AssigneeEmployeeId: Guid.NewGuid());

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CreateWorkAsync_EndDateBeforeStartDate_ThrowsValidationException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var request = new CreateWorkRequestDto(
                Title: "Valid Title Here",
                WorkTypeId: Guid.NewGuid(),
                AssigneeEmployeeId: Guid.NewGuid(),
                PlannedStartDate: new DateOnly(2026, 10, 20),
                PlannedEndDate: new DateOnly(2026, 10, 10));

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CreateWorkAsync_InactiveOrMissingWorkType_ThrowsValidationException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP002",
                FullName = "Tran Thi B",
                Email = "b.tran@bank.com",
                Status = EmployeeStatus.Active,
            };
            var inactiveWorkType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-INACTIVE",
                Name = "Inactive Type",
                Status = RecordStatus.Inactive,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.Employees.AddAsync(employee);
            await context.WorkTypes.AddAsync(inactiveWorkType);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Valid Title for Work",
                WorkTypeId: inactiveWorkType.Id,
                AssigneeEmployeeId: employee.Id);

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
        }
    }

    private static (WorkType WorkType, Employee Employee) NewActiveWorkTypeAndEmployee(string code, Guid positionId) =>
        (
            new WorkType
            {
                Id = Guid.NewGuid(),
                Code = $"WT-{code}",
                Name = $"Type {code}",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = code,
                FullName = $"Employee {code}",
                Email = $"{code.ToLowerInvariant()}@bank.com",
                JobPositionId = positionId,
                Status = EmployeeStatus.Active,
            });

    [Fact]
    public async Task CreateWorkAsync_WorkTypeNotAssignedToAssigneePosition_ThrowsValidationAndCreatesNothing()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var positionId = Guid.NewGuid();
            var (workType, employee) = NewActiveWorkTypeAndEmployee("EMP-NOLINK", positionId);
            var otherPositionId = Guid.NewGuid();
            context.WorkTypes.Add(workType);
            context.Employees.Add(employee);
            // Linked to a different job position only.
            context.JobPositionWorkTypes.Add(new JobPositionWorkType
            {
                Id = Guid.NewGuid(),
                JobPositionId = otherPositionId,
                WorkTypeId = workType.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(
                new CreateWorkRequestDto(
                    Title: "Cong viec sai chuc danh",
                    WorkTypeId: workType.Id,
                    AssigneeEmployeeId: employee.Id),
                employee.Id));

            Assert.Equal("Loại công việc không hợp lệ với chức danh của nhân viên được phân công.", ex.Message);
            Assert.Empty(context.Works);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_PositionWithNoWorkTypesAssigned_ThrowsValidation()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var (workType, employee) = NewActiveWorkTypeAndEmployee("EMP-EMPTY", Guid.NewGuid());
            context.WorkTypes.Add(workType);
            context.Employees.Add(employee);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(
                new CreateWorkRequestDto(
                    Title: "Cong viec chuc danh trong",
                    WorkTypeId: workType.Id,
                    AssigneeEmployeeId: employee.Id),
                employee.Id));
        }
    }

    [Fact]
    public async Task CreateWorkAsync_WorkTypeAssignedToAssigneePosition_Succeeds()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var positionId = Guid.NewGuid();
            var (workType, employee) = NewActiveWorkTypeAndEmployee("EMP-OK", positionId);
            var (otherWorkType, _) = NewActiveWorkTypeAndEmployee("OTHER", positionId);
            context.WorkTypes.AddRange(workType, otherWorkType);
            context.Employees.Add(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.SaveChangesAsync();

            var result = await service.CreateWorkAsync(
                new CreateWorkRequestDto(
                    Title: "Cong viec dung chuc danh",
                    WorkTypeId: workType.Id,
                    AssigneeEmployeeId: employee.Id),
                employee.Id);
            Assert.Equal(workType.Id, result.WorkTypeId);

            // A type assigned to nobody at this position is still rejected for the same assignee.
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(
                new CreateWorkRequestDto(
                    Title: "Cong viec loai khac",
                    WorkTypeId: otherWorkType.Id,
                    AssigneeEmployeeId: employee.Id),
                employee.Id));
        }
    }

    [Fact]
    public async Task CreateWorkAsync_InactiveOrMissingAssignee_ThrowsValidationException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-LOAN",
                Name = "Personal Loan",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var inactiveEmployee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP003",
                FullName = "Le Van C",
                Email = "c.le@bank.com",
                Status = EmployeeStatus.Terminated,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(inactiveEmployee);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Valid Title for Loan Task",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: inactiveEmployee.Id);

            await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
        }
    }

    [Fact]
    public async Task CreateWorkAsync_WithCampaign_AssigneeNotParticipant_ThrowsBusinessRuleException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CMP",
                Name = "Campaign Task Type",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP004",
                FullName = "Pham D",
                Email = "d.pham@bank.com",
                Status = EmployeeStatus.Active,
            };
            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                Code = "CMP-AUTUMN-2026",
                Name = "Chien dich mua thu",
                Status = BusinessCampaignStatus.Running,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Cong viec chien dich mua thu",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id,
                BusinessCampaignId: campaign.Id);

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
            Assert.Contains("Participant", ex.Message);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_WithCampaign_DueDateExceedsCampaign_ThrowsValidationException()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CMP-2",
                Name = "Campaign Task Type 2",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP005",
                FullName = "Hoang E",
                Email = "e.hoang@bank.com",
                Status = EmployeeStatus.Active,
            };
            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                Code = "CMP-OCTOBER",
                Name = "Chien dich thang 10",
                Status = BusinessCampaignStatus.Running,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 20),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var participant = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = employee.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(participant);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Cong viec thang 10 co due date qua han",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id,
                BusinessCampaignId: campaign.Id,
                PlannedStartDate: new DateOnly(2026, 10, 10),
                PlannedEndDate: new DateOnly(2026, 10, 25)); // Exceeds 2026-10-20

            var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
            Assert.Contains("vượt quá ngày kết thúc chiến dịch", ex.Message);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_WithInactiveCampaign_ThrowsConflict()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CMP-3",
                Name = "Campaign Task Type 3",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP006",
                FullName = "Hoang F",
                Email = "f.hoang@bank.com",
                Status = EmployeeStatus.Active,
            };
            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                Code = "CMP-CLOSED",
                Name = "Chien dich da dong",
                Status = BusinessCampaignStatus.Completed,
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 9, 30),
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Cong viec cho chien dich da dong",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id,
                BusinessCampaignId: campaign.Id);

            var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
            Assert.Equal(ErrorCodes.Conflict, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_WithCampaign_ValidParticipant_Succeeds()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CMP-OK",
                Name = "Campaign OK",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP006",
                FullName = "Do F",
                Email = "f.do@bank.com",
                Status = EmployeeStatus.Active,
            };
            var campaign = new BusinessCampaign
            {
                Id = Guid.NewGuid(),
                Code = "CMP-VALID",
                Name = "Chien dich hop le",
                Status = BusinessCampaignStatus.Running,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 31),
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var participant = new Participant
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaign.Id,
                EmployeeId = employee.Id,
                UpdatedAt = DateTimeOffset.UtcNow,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.BusinessCampaigns.AddAsync(campaign);
            await context.Participants.AddAsync(participant);
            await context.SaveChangesAsync();

            var request = new CreateWorkRequestDto(
                Title: "Giao viec chien dich hop le",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id,
                BusinessCampaignId: campaign.Id,
                PlannedStartDate: new DateOnly(2026, 10, 10),
                PlannedEndDate: new DateOnly(2026, 10, 25));

            var result = await service.CreateWorkAsync(request, employee.Id);

            Assert.NotNull(result);
            Assert.Equal("Chien dich hop le", result.BusinessCampaignName);
            Assert.Equal(employee.Id, result.AssigneeEmployeeId);
        }
    }

    [Fact]
    public async Task WorksController_Create_ReturnsCreatedAtAction()
    {
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var workType = new WorkType
            {
                Id = Guid.NewGuid(),
                Code = "WT-CTRL",
                Name = "Controller Test",
                Status = RecordStatus.Active,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "EMP007",
                FullName = "Controller Employee",
                Email = "ctrl@bank.com",
                Status = EmployeeStatus.Active,
            };

            await context.WorkTypes.AddAsync(workType);
            await context.Employees.AddAsync(employee);
            context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
            await context.SaveChangesAsync();

            var controller = new WorksController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        TraceIdentifier = "trace-ctrl-test",
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", employee.Id.ToString())], "test")),
                    },
                },
            };

            var request = new CreateWorkRequestDto(
                Title: "Tieu de qua Controller API",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id);

            var actionResult = await controller.Create(request, CancellationToken.None);
            var createdAt = Assert.IsType<CreatedAtActionResult>(actionResult.Result);

            Assert.Equal(StatusCodes.Status201Created, createdAt.StatusCode);
            var envelope = Assert.IsType<ApiResponse<WorkDetailDto>>(createdAt.Value);
            Assert.True(envelope.Success);
            Assert.NotNull(envelope.Data);
            Assert.Equal("trace-ctrl-test", envelope.TraceId);
        }
    }

    [Fact]
    public async Task WorksController_GetById_ReturnsNotFoundWhenMissing()
    {
        var context = CreateInMemoryDbContext();
        using (context)
        {
            var employee = NewEmployee("EMP-NF");
            var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = "emp-nf", PasswordHash = "hash", LastSyncAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            context.Employees.Add(employee);
            context.Accounts.Add(account);
            await context.SaveChangesAsync();

            var service = new WorkService(new UnitOfWork(context), NullLogger<WorkService>.Instance, TimeProvider.System, new FakePermissionService("All"));
            var controller = new WorksController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        TraceIdentifier = "trace-not-found",
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", account.Id.ToString())], "test")),
                    },
                },
            };

            var actionResult = await controller.GetById(Guid.NewGuid(), CancellationToken.None);
            var notFound = Assert.IsType<NotFoundObjectResult>(actionResult.Result);

            Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
            var envelope = Assert.IsType<ApiResponse>(notFound.Value);
            Assert.False(envelope.Success);
            Assert.Equal(ErrorCodes.NotFound, envelope.ErrorCode);
        }
    }

    // ---- ChangeStatusAsync / MarkOverdueAsync ----

    private static readonly DateOnly OverdueToday = new(2026, 10, 21);

    private static (AppDbContext Context, WorkService Service) CreateStatusTestEnvironment()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        return (context, new WorkService(new UnitOfWork(context), NullLogger<WorkService>.Instance, TimeProvider.System, new FakePermissionService(null, FakePermissionService.WorkCreate("All"))));
    }

    private static async Task<(Employee Manager, Employee Assignee, Employee Stranger, Work Work)> SeedAsync(
        AppDbContext context,
        WorkStatus status,
        DateOnly? plannedEnd = null)
    {
        var manager = NewEmployee("MGR001");
        var assignee = NewEmployee("EMP001");
        assignee.ManagerId = manager.Id;
        var stranger = NewEmployee("EMP999");

        var work = NewWork("WRK-1", assignee.Id, status, plannedEnd);

        context.Employees.AddRange(manager, assignee, stranger);
        context.Works.Add(work);
        await context.SaveChangesAsync();
        return (manager, assignee, stranger, work);
    }

    private static Employee NewEmployee(string code) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Employee {code}",
        Email = $"{code.ToLowerInvariant()}@bank.com",
        Status = EmployeeStatus.Active,
    };

    private static Work NewWork(string code, Guid assigneeId, WorkStatus status, DateOnly? plannedEnd = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Title = $"Work {code}",
        AssigneeEmployeeId = assigneeId,
        Priority = WorkPriority.Medium,
        Status = status,
        PlannedEndDate = plannedEnd,
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };


    [Theory]
    [InlineData("InProgress")]
    [InlineData("IN_PROGRESS")]
    [InlineData("in_progress")]
    public async Task ChangeStatusAsync_AssigneeStartsNewWork_MovesToInProgress(string statusInput)
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.New);

            var result = await service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = statusInput });

            Assert.Equal("InProgress", result.Status);
            Assert.Equal(WorkStatus.InProgress, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_AssigneeStartsOverdueWork_ThrowsConflictAndKeepsOverdue()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.Overdue);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = "InProgress" }));

            Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
            Assert.Equal(WorkStatus.Overdue, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }

    [Fact]
    public async Task MarkOverdueAsync_WorkThatCannotLeaveOverdueByStarting_DoesNotFlipBetweenStatuses()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.InProgress, new DateOnly(2026, 10, 1));

            Assert.Equal(1, await service.MarkOverdueAsync(OverdueToday));
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = "InProgress" }));

            Assert.Equal(0, await service.MarkOverdueAsync(OverdueToday));
            Assert.Equal(WorkStatus.Overdue, (await context.Works.AsNoTracking().FirstAsync(w => w.Id == work.Id)).Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ManagerMarksVerifiedWorkDone_Succeeds()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.Verified);

            var result = await service.ChangeStatusAsync(work.Id, manager.Id, new WorkStatusDto { Status = "Done" });

            Assert.Equal("Done", result.Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ManagerCancelsWithReason_Succeeds()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.InProgress);

            var result = await service.ChangeStatusAsync(
                work.Id, manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "Khach hang huy yeu cau" });

            Assert.Equal("Cancelled", result.Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_CancelWithoutReason_ThrowsValidation()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.InProgress);

            var ex = await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(work.Id, manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "  " }));

            Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
            Assert.Equal(WorkStatus.InProgress, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Verified")]
    [InlineData("Rejected")]
    [InlineData("Overdue")]
    [InlineData("HandedOver")]
    [InlineData("New")]
    public async Task ChangeStatusAsync_SystemOnlyOrNonManualTarget_ThrowsValidation(string target)
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.InProgress);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(work.Id, manager.Id, new WorkStatusDto { Status = target }));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NOT_A_STATUS")]
    public async Task ChangeStatusAsync_BlankOrUnknownStatus_ThrowsValidation(string target)
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.New);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = target }));
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ReasonTooLong_ThrowsValidation()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.InProgress);

            await Assert.ThrowsAsync<ValidationException>(() =>
                service.ChangeStatusAsync(
                    work.Id, manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = new string('x', 501) }));
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_InvalidTransition_ThrowsConflictAndKeepsStatus()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            // New -> Done is not allowed even for the manager.
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.New);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(work.Id, manager.Id, new WorkStatusDto { Status = "Done" }));

            Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
            Assert.Equal(409, ex.StatusCode);
            Assert.Equal(WorkStatus.New, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_SameStatusAsCurrent_ThrowsConflict()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.InProgress);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = "InProgress" }));

            Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_TerminalWork_CannotBeReopened()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.Cancelled);

            var ex = await Assert.ThrowsAsync<ConflictException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = "InProgress" }));

            Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_StrangerStartingWork_ThrowsForbidden()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, _, stranger, work) = await SeedAsync(context, WorkStatus.New);

            var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.ChangeStatusAsync(work.Id, stranger.Id, new WorkStatusDto { Status = "InProgress" }));

            Assert.Equal(403, ex.StatusCode);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_ManagerStartingWork_ThrowsForbidden()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (manager, _, _, work) = await SeedAsync(context, WorkStatus.New);

            await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.ChangeStatusAsync(work.Id, manager.Id, new WorkStatusDto { Status = "InProgress" }));
        }
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("Cancelled")]
    public async Task ChangeStatusAsync_AssigneeOrStrangerClosingWork_ThrowsForbidden(string target)
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, stranger, work) = await SeedAsync(context, WorkStatus.Verified);
            var request = new WorkStatusDto { Status = target, Reason = "ly do" };

            await Assert.ThrowsAsync<ForbiddenException>(() => service.ChangeStatusAsync(work.Id, assignee.Id, request));
            await Assert.ThrowsAsync<ForbiddenException>(() => service.ChangeStatusAsync(work.Id, stranger.Id, request));
            Assert.Equal(WorkStatus.Verified, (await context.Works.FindAsync(work.Id))!.Status);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownWork_ThrowsNotFound()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, _) = await SeedAsync(context, WorkStatus.New);

            await Assert.ThrowsAsync<NotFoundException>(() =>
                service.ChangeStatusAsync(Guid.NewGuid(), assignee.Id, new WorkStatusDto { Status = "InProgress" }));
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_UnknownOrInactiveActor_ThrowsUnauthorized()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, assignee, _, work) = await SeedAsync(context, WorkStatus.New);

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                service.ChangeStatusAsync(work.Id, Guid.NewGuid(), new WorkStatusDto { Status = "InProgress" }));

            assignee.Status = EmployeeStatus.Inactive;
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<UnauthorizedException>(() =>
                service.ChangeStatusAsync(work.Id, assignee.Id, new WorkStatusDto { Status = "InProgress" }));
        }
    }


    [Fact]
    public async Task MarkOverdueAsync_MovesOnlyEligiblePastDueWork()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var employee = NewEmployee("EMP100");
            context.Employees.Add(employee);

            var past = OverdueToday.AddDays(-2);
            var wNew = NewWork("W-NEW", employee.Id, WorkStatus.New, past);
            var wProgress = NewWork("W-PROG", employee.Id, WorkStatus.InProgress, past);
            var wHandedOver = NewWork("W-HAND", employee.Id, WorkStatus.HandedOver, past);
            var wSubmitted = NewWork("W-SUB", employee.Id, WorkStatus.Submitted, past);
            var wVerified = NewWork("W-VER", employee.Id, WorkStatus.Verified, past);
            var wRejected = NewWork("W-REJ", employee.Id, WorkStatus.Rejected, past);
            var wDone = NewWork("W-DONE", employee.Id, WorkStatus.Done, past);
            var wCancelled = NewWork("W-CAN", employee.Id, WorkStatus.Cancelled, past);
            var wAlreadyOverdue = NewWork("W-OVD", employee.Id, WorkStatus.Overdue, past);
            var wDueToday = NewWork("W-TODAY", employee.Id, WorkStatus.InProgress, OverdueToday);
            var wFuture = NewWork("W-FUT", employee.Id, WorkStatus.InProgress, OverdueToday.AddDays(5));
            var wNoDate = NewWork("W-NODATE", employee.Id, WorkStatus.InProgress, null);

            context.Works.AddRange(
                wNew, wProgress, wHandedOver, wSubmitted, wVerified, wRejected, wDone, wCancelled,
                wAlreadyOverdue, wDueToday, wFuture, wNoDate);
            await context.SaveChangesAsync();

            var updated = await service.MarkOverdueAsync(OverdueToday);

            Assert.Equal(3, updated);

            async Task<WorkStatus> StatusOf(Work w) => (await context.Works.AsNoTracking().FirstAsync(x => x.Id == w.Id)).Status;

            Assert.Equal(WorkStatus.Overdue, await StatusOf(wNew));
            Assert.Equal(WorkStatus.Overdue, await StatusOf(wProgress));
            Assert.Equal(WorkStatus.Overdue, await StatusOf(wHandedOver));
            Assert.Equal(WorkStatus.Submitted, await StatusOf(wSubmitted));
            Assert.Equal(WorkStatus.Verified, await StatusOf(wVerified));
            Assert.Equal(WorkStatus.Rejected, await StatusOf(wRejected));
            Assert.Equal(WorkStatus.Done, await StatusOf(wDone));
            Assert.Equal(WorkStatus.Cancelled, await StatusOf(wCancelled));
            Assert.Equal(WorkStatus.Overdue, await StatusOf(wAlreadyOverdue));
            Assert.Equal(WorkStatus.InProgress, await StatusOf(wDueToday));
            Assert.Equal(WorkStatus.InProgress, await StatusOf(wFuture));
            Assert.Equal(WorkStatus.InProgress, await StatusOf(wNoDate));
        }
    }

    [Fact]
    public async Task MarkOverdueAsync_IsIdempotentAndReturnsZeroWhenNothingToDo()
    {
        var (context, service) = CreateStatusTestEnvironment();
        using (context)
        {
            var (_, _, _, work) = await SeedAsync(context, WorkStatus.InProgress, OverdueToday.AddDays(-1));

            Assert.Equal(1, await service.MarkOverdueAsync(OverdueToday));
            Assert.Equal(0, await service.MarkOverdueAsync(OverdueToday));
            Assert.Equal(WorkStatus.Overdue, (await context.Works.AsNoTracking().FirstAsync(w => w.Id == work.Id)).Status);
        }
    }

    // ---- Business date (UTC+7) ----

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task CreateWorkAsync_BeforeVietnamMidnight_UsesVietnamDateForDefaultStartAndCode()
    {
        // 02:00 on 22/10 in Vietnam is still 21/10 in UTC: the business date must be the 22nd.
        var (context, service, workType, employee) = await CreateAtAsync("2026-10-21T19:00:00+00:00");
        using (context)
        {
            var result = await service.CreateWorkAsync(new CreateWorkRequestDto(
                Title: "Cong viec dau ngay moi",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id), employee.Id);

            Assert.Equal(new DateOnly(2026, 10, 22), result.PlannedStartDate);
            Assert.StartsWith("WRK-20261022-", result.Code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task CreateWorkAsync_BeforeVietnamMidnightEndsAt2359_StillUsesTheSameVietnamDate()
    {
        var (context, service, workType, employee) = await CreateAtAsync("2026-10-21T16:59:59+00:00");
        using (context)
        {
            var result = await service.CreateWorkAsync(new CreateWorkRequestDto(
                Title: "Cong viec cuoi ngay",
                WorkTypeId: workType.Id,
                AssigneeEmployeeId: employee.Id), employee.Id);

            Assert.Equal(new DateOnly(2026, 10, 21), result.PlannedStartDate);
            Assert.StartsWith("WRK-20261021-", result.Code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ChangeStatusAsync_StampsUpdatedAtFromTheTimeProvider()
    {
        var (context, service, _, employee) = await CreateAtAsync("2026-10-21T19:00:00+00:00");
        using (context)
        {
            var work = NewWork("W-CLOCK", employee.Id, WorkStatus.New);
            context.Works.Add(work);
            await context.SaveChangesAsync();

            var result = await service.ChangeStatusAsync(work.Id, employee.Id, new WorkStatusDto { Status = "InProgress" });

            Assert.Equal(DateTimeOffset.Parse("2026-10-21T19:00:00+00:00", CultureInfo.InvariantCulture), result.UpdatedAt);
        }
    }

    private static JobPositionWorkType LinkFor(Employee employee, WorkType workType) => new()
    {
        Id = Guid.NewGuid(),
        JobPositionId = employee.JobPositionId,
        WorkTypeId = workType.Id,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<(AppDbContext Context, WorkService Service, WorkType WorkType, Employee Employee)> CreateAtAsync(string utcNow)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new AppDbContext(options);
        var service = new WorkService(
            new UnitOfWork(context),
            NullLogger<WorkService>.Instance,
            new FixedTime(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)),
            new FakePermissionService(null, FakePermissionService.WorkCreate("All")));

        var workType = new WorkType
        {
            Id = Guid.NewGuid(),
            Code = "WT-CLOCK",
            Name = "Clock",
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var employee = NewEmployee("EMP-CLOCK");
        context.WorkTypes.Add(workType);
        context.Employees.Add(employee);
        context.JobPositionWorkTypes.Add(LinkFor(employee, workType));
        await context.SaveChangesAsync();
        return (context, service, workType, employee);
    }
}
