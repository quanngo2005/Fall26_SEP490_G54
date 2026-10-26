using System.Reflection;
using System.Security.Claims;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Employee;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Caller-scoped reads (employee:read, work:read, work:create, submission:read) and the permission attributes that gate them.
/// World: Branch BR contains D1 (the caller's department) and D2 (a sibling department). Self and Colleague sit in D1,
/// Outsider sits in D2.
/// </summary>
public sealed class ScopedReadAccessTests
{
    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static UserPermissionDto Grant(string resource, string action, string scope) =>
        new(resource, action, scope, $"{resource.ToUpperInvariant()}_{action.ToUpperInvariant()}");

    private sealed record World(
        AppDbContext Db,
        Department Branch,
        Department D1,
        Department D2,
        Employee Self,
        Employee Colleague,
        Employee Outsider,
        Account SelfAccount);

    private static async Task<World> CreateWorldAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = new Department { Id = Guid.NewGuid(), Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var d1 = new Department { Id = Guid.NewGuid(), Code = "D1", Name = "Dept 1", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var d2 = new Department { Id = Guid.NewGuid(), Code = "D2", Name = "Dept 2", LevelType = DepartmentLevelType.Department, ParentDepartmentId = branch.Id, Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "CV", Title = "Officer", Status = RecordStatus.Active, LastSyncAt = now, UpdatedAt = now };
        db.AddRange(branch, d1, d2, position);

        var self = NewEmployee("SELF", d1.Id, position.Id);
        var colleague = NewEmployee("COL", d1.Id, position.Id);
        var outsider = NewEmployee("OUT", d2.Id, position.Id);
        db.AddRange(self, colleague, outsider);

        var account = new Account { Id = Guid.NewGuid(), EmployeeId = self.Id, Username = "self", PasswordHash = "hash", LastSyncAt = now, UpdatedAt = now };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return new World(db, branch, d1, d2, self, colleague, outsider, account);
    }

    private static Employee NewEmployee(string code, Guid departmentId, Guid positionId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Employee {code}",
        Email = $"{code.ToLowerInvariant()}@bank.com",
        DepartmentId = departmentId,
        JobPositionId = positionId,
        Status = EmployeeStatus.Active,
        LastSyncAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static EmployeeService EmployeeServiceFor(AppDbContext db, params UserPermissionDto[] grants) =>
        new(db, NullLogger<EmployeeService>.Instance, new FakePermissionService(null, grants));

    // ---- employee:read ------------------------------------------------------------------------------

    [Fact]
    public async Task Employees_OwnScope_ListsAndReadsOnlyTheCaller()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db, Grant("employee", "read", "Own"));

        var page = await service.GetEmployeesAsync(world.SelfAccount.Id, new EmployeeFilterDto());

        Assert.Equal([world.Self.Id], page.Items.Select(e => e.Id).ToArray());
        Assert.NotNull(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Self.Id));
        Assert.Null(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Colleague.Id));
        Assert.Null(await service.GetEmployeeByCodeAsync(world.SelfAccount.Id, "COL"));
    }

    [Fact]
    public async Task Employees_DeptScope_CoversOnlyTheCallersDepartmentSubtree()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db, Grant("employee", "read", "Dept"));

        var page = await service.GetEmployeesAsync(world.SelfAccount.Id, new EmployeeFilterDto());

        Assert.Equal(new[] { world.Self.Id, world.Colleague.Id }.OrderBy(x => x), page.Items.Select(e => e.Id).OrderBy(x => x));
        Assert.NotNull(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Colleague.Id));
        Assert.Null(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Outsider.Id));
        Assert.Null(await service.GetEmployeeByCodeAsync(world.SelfAccount.Id, "OUT"));
    }

    [Fact]
    public async Task Employees_BranchScope_WidensToTheWholeBranch()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db, Grant("employee", "read", "Branch"));

        Assert.NotNull(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Outsider.Id));
        Assert.Equal(3, (await service.GetEmployeesAsync(world.SelfAccount.Id, new EmployeeFilterDto())).TotalCount);
    }

    [Fact]
    public async Task Employees_AllScope_ReadsEveryone()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db, Grant("employee", "read", "All"));

        Assert.NotNull(await service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Outsider.Id));
        Assert.Equal(3, (await service.GetEmployeesAsync(world.SelfAccount.Id, new EmployeeFilterDto())).TotalCount);
    }

    [Fact]
    public async Task Employees_WithoutEmployeeRead_AreForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetEmployeesAsync(world.SelfAccount.Id, new EmployeeFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetEmployeeByIdAsync(world.SelfAccount.Id, world.Self.Id));
    }

    [Fact]
    public async Task Employees_ScopedCallerWithNoAccount_IsForbidden()
    {
        using var db = CreateDb();
        await CreateWorldAsync(db);
        var service = EmployeeServiceFor(db, Grant("employee", "read", "Dept"));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetEmployeesAsync(Guid.NewGuid(), new EmployeeFilterDto()));
    }

    // ---- work:read / work:create ------------------------------------------------------------------

    private static WorkService WorkServiceFor(AppDbContext db, FakePermissionService permissions) =>
        new(new UnitOfWork(db), NullLogger<WorkService>.Instance, TimeProvider.System, permissions);

    private static Work NewWork(string code, Guid assigneeId) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Title = $"Work {code}",
        AssigneeEmployeeId = assigneeId,
        Priority = WorkPriority.Medium,
        Status = WorkStatus.New,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Work_GetById_OwnScope_ShowsOnlyOwnOrManagedWork()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        world.Colleague.ManagerId = world.Self.Id;
        var ownWork = NewWork("W-OWN", world.Self.Id);
        var reportWork = NewWork("W-REPORT", world.Colleague.Id);
        var outsiderWork = NewWork("W-OUT", world.Outsider.Id);
        db.Works.AddRange(ownWork, reportWork, outsiderWork);
        await db.SaveChangesAsync();
        var service = WorkServiceFor(db, new FakePermissionService("Own"));

        Assert.NotNull(await service.GetByIdAsync(ownWork.Id, world.SelfAccount.Id));
        Assert.NotNull(await service.GetByIdAsync(reportWork.Id, world.SelfAccount.Id));
        Assert.Null(await service.GetByIdAsync(outsiderWork.Id, world.SelfAccount.Id));
    }

    [Fact]
    public async Task Work_GetById_DeptScope_FollowsTheAssigneesDepartment()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var colleagueWork = NewWork("W-COL", world.Colleague.Id);
        var outsiderWork = NewWork("W-OUT", world.Outsider.Id);
        db.Works.AddRange(colleagueWork, outsiderWork);
        await db.SaveChangesAsync();
        var service = WorkServiceFor(db, new FakePermissionService("Dept"));

        Assert.NotNull(await service.GetByIdAsync(colleagueWork.Id, world.SelfAccount.Id));
        Assert.Null(await service.GetByIdAsync(outsiderWork.Id, world.SelfAccount.Id));
    }

    [Fact]
    public async Task Work_GetById_WithoutWorkRead_IsForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var work = NewWork("W-1", world.Self.Id);
        db.Works.Add(work);
        await db.SaveChangesAsync();
        var service = WorkServiceFor(db, new FakePermissionService(null));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetByIdAsync(work.Id, world.SelfAccount.Id));
    }

    private static CreateWorkRequestDto CreateRequest(Guid workTypeId, Guid assigneeId) => new(
        Title: "Work created by a manager",
        WorkTypeId: workTypeId,
        AssigneeEmployeeId: assigneeId,
        Priority: "Medium",
        PlannedStartDate: new DateOnly(2026, 10, 15),
        PlannedEndDate: new DateOnly(2026, 10, 20),
        EstimatedHours: 2m,
        Description: null);

    private static async Task<WorkType> SeedWorkTypeAsync(AppDbContext db)
    {
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT-SCOPE", Name = "Scope test", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };
        db.WorkTypes.Add(workType);
        await db.SaveChangesAsync();
        return workType;
    }

    [Fact]
    public async Task CreateWork_DeptScope_AllowsAssigneeInCallersDepartment()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var workType = await SeedWorkTypeAsync(db);
        var service = WorkServiceFor(db, new FakePermissionService(null, FakePermissionService.WorkCreate("Dept")));

        var result = await service.CreateWorkAsync(CreateRequest(workType.Id, world.Colleague.Id), world.SelfAccount.Id);

        Assert.Equal(world.Colleague.Id, result.AssigneeEmployeeId);
    }

    [Fact]
    public async Task CreateWork_DeptScope_RejectsAssigneeOutsideCallersDepartment()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var workType = await SeedWorkTypeAsync(db);
        var service = WorkServiceFor(db, new FakePermissionService(null, FakePermissionService.WorkCreate("Dept")));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateWorkAsync(CreateRequest(workType.Id, world.Outsider.Id), world.SelfAccount.Id));
    }

    [Fact]
    public async Task CreateWork_AllScope_AllowsAnyAssignee()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var workType = await SeedWorkTypeAsync(db);
        var service = WorkServiceFor(db, new FakePermissionService(null, FakePermissionService.WorkCreate("All")));

        var result = await service.CreateWorkAsync(CreateRequest(workType.Id, world.Outsider.Id), world.SelfAccount.Id);

        Assert.Equal(world.Outsider.Id, result.AssigneeEmployeeId);
    }

    [Fact]
    public async Task CreateWork_WithoutWorkCreate_IsForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var workType = await SeedWorkTypeAsync(db);
        var service = WorkServiceFor(db, new FakePermissionService("All"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateWorkAsync(CreateRequest(workType.Id, world.Colleague.Id), world.SelfAccount.Id));
    }

    // ---- submission:read ----------------------------------------------------------------------------

    private static ResultSubmissionService SubmissionServiceFor(AppDbContext db, FakePermissionService permissions)
    {
        var uow = new UnitOfWork(db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = Path.GetTempPath() })
            .Build();
        var storage = new StorageService(config, NullLogger<StorageService>.Instance);
        return new ResultSubmissionService(
            uow,
            storage,
            new WorkSubmissionValidator(uow, TimeProvider.System),
            new EventBus(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<EventBus>.Instance),
            NullLogger<ResultSubmissionService>.Instance,
            TimeProvider.System,
            permissions);
    }

    /// <summary>A work assigned to Outsider (D2) and a submission made by Outsider.</summary>
    private static async Task<(Work Work, ResultSubmission Submission)> SeedOutsiderSubmissionAsync(AppDbContext db, World world)
    {
        var work = NewWork("W-SUB", world.Outsider.Id);
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = world.Outsider.Id,
            SubmissionNo = 1,
            Comment = "outsider report",
            Status = SubmissionStatus.Submitted,
            SubmittedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Works.Add(work);
        db.ResultSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return (work, submission);
    }

    [Fact]
    public async Task Submission_UnrelatedCallerWithoutScope_IsForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (work, submission) = await SeedOutsiderSubmissionAsync(db, world);
        var service = SubmissionServiceFor(db, new FakePermissionService(null));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSubmissionByIdAsync(submission.Id, world.SelfAccount.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSubmissionsByWorkIdAsync(work.Id, world.SelfAccount.Id));
    }

    [Fact]
    public async Task Submission_DeptScopeOverOtherDepartment_IsForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (work, submission) = await SeedOutsiderSubmissionAsync(db, world);
        var service = SubmissionServiceFor(db, new FakePermissionService(null, Grant("submission", "read", "Dept")));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.GetSubmissionByIdAsync(submission.Id, world.SelfAccount.Id));
        Assert.Empty(await service.GetSubmissionsByWorkIdAsync(work.Id, world.SelfAccount.Id));
    }

    [Fact]
    public async Task Submission_BranchScope_ReadsAcrossTheBranch()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (work, submission) = await SeedOutsiderSubmissionAsync(db, world);
        var service = SubmissionServiceFor(db, new FakePermissionService(null, Grant("submission", "read", "Branch")));

        var detail = await service.GetSubmissionByIdAsync(submission.Id, world.SelfAccount.Id);
        Assert.Equal(submission.Id, detail.Id);
        Assert.Single(await service.GetSubmissionsByWorkIdAsync(work.Id, world.SelfAccount.Id));
    }

    [Fact]
    public async Task Submission_AssigneeAndSubmitter_ReadWithoutAnyScope()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (work, submission) = await SeedOutsiderSubmissionAsync(db, world);

        var asSubmitter = SubmissionServiceFor(db, new FakePermissionService(null));
        var outsiderAccount = new Account { Id = Guid.NewGuid(), EmployeeId = world.Outsider.Id, Username = "out", PasswordHash = "hash", LastSyncAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Accounts.Add(outsiderAccount);
        await db.SaveChangesAsync();

        Assert.Equal(submission.Id, (await asSubmitter.GetSubmissionByIdAsync(submission.Id, outsiderAccount.Id)).Id);
        Assert.Single(await asSubmitter.GetSubmissionsByWorkIdAsync(work.Id, outsiderAccount.Id));
    }

    [Fact]
    public async Task Submission_AllScope_ReadsEverything()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (_, submission) = await SeedOutsiderSubmissionAsync(db, world);
        var service = SubmissionServiceFor(db, new FakePermissionService(null, Grant("submission", "read", "All")));

        Assert.Equal(submission.Id, (await service.GetSubmissionByIdAsync(submission.Id, world.SelfAccount.Id)).Id);
    }

    [Fact]
    public async Task Submission_LegacyRoute_Returns404WhenTheSubmissionBelongsToAnotherWork()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var (_, submission) = await SeedOutsiderSubmissionAsync(db, world);
        var service = SubmissionServiceFor(db, new FakePermissionService(null, Grant("submission", "read", "All")));
        var controller = new ResultSubmissionsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", world.SelfAccount.Id.ToString())], "test")),
                },
            },
        };

        var mismatched = await controller.GetSubmissionByIdLegacy(Guid.NewGuid(), submission.Id, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(mismatched.Result);
    }

    // ---- permission attributes ----------------------------------------------------------------------

    private static HasPermissionAttribute PermissionOf(Type controller, string method) =>
        controller.GetMethod(method)!.GetCustomAttributes<HasPermissionAttribute>().Single();

    [Theory]
    [InlineData(typeof(KpiDefinitionController), nameof(KpiDefinitionController.CreateKpi))]
    [InlineData(typeof(KpiDefinitionController), nameof(KpiDefinitionController.UpdateKpi))]
    [InlineData(typeof(KpiDefinitionController), nameof(KpiDefinitionController.ChangeKpiStatus))]
    [InlineData(typeof(KpiMappingController), nameof(KpiMappingController.AddObjectiveKpi))]
    [InlineData(typeof(KpiMappingController), nameof(KpiMappingController.RemoveObjectiveKpi))]
    public void KpiWrites_RequireKpiManageAtAll(Type controller, string method)
    {
        var attribute = PermissionOf(controller, method);

        Assert.Equal(("kpi", "manage", "All"), (attribute.Resource, attribute.Action, attribute.Scope));
    }

    [Theory]
    [InlineData(nameof(EmployeeController.GetEmployees))]
    [InlineData(nameof(EmployeeController.GetEmployeeByCode))]
    [InlineData(nameof(EmployeeController.GetEmployeeById))]
    public void EmployeeReads_RequireEmployeeRead(string method)
    {
        var attribute = PermissionOf(typeof(EmployeeController), method);

        Assert.Equal(("employee", "read", "Own"), (attribute.Resource, attribute.Action, attribute.Scope));
    }

    [Fact]
    public void WorkEndpoints_RequireWorkPermissions()
    {
        var create = PermissionOf(typeof(WorksController), nameof(WorksController.Create));
        var read = PermissionOf(typeof(WorksController), nameof(WorksController.GetById));

        Assert.Equal(("work", "create", "Department"), (create.Resource, create.Action, create.Scope));
        Assert.Equal(("work", "read", "Own"), (read.Resource, read.Action, read.Scope));
    }
}
