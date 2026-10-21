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
        var service = new WorkService(uow, NullLogger<WorkService>.Instance);
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
    public async Task CreateWorkAsync_WithCampaign_DueDateExceedsCampaign_ThrowsBusinessRuleException()
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

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateWorkAsync(request, Guid.NewGuid()));
            Assert.Equal(ErrorCodes.MSG18, ex.ErrorCode);
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
        var (context, _, service) = CreateTestEnvironment();
        using (context)
        {
            var controller = new WorksController(service)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { TraceIdentifier = "trace-not-found" },
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
}
