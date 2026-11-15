using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Permission;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>SCR-WRK-03: checklist, activity log, related KPIs and the manager's plan edit.</summary>
public sealed class WorkDetailServiceTests
{
    private sealed class Env
    {
        public required AppDbContext Db { get; init; }
        public required WorkService Service { get; init; }
        public required RecordingNotifier Notifier { get; init; }
        public required Employee Manager { get; init; }
        public required Employee Assignee { get; init; }
        public required Employee Stranger { get; init; }
        public required WorkType WorkType { get; init; }
    }

    private static Employee NewEmployee(string code, Guid? managerId = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"Employee {code}",
        Email = $"{code.ToLowerInvariant()}@bank.com",
        Status = EmployeeStatus.Active,
        ManagerId = managerId,
        DepartmentId = Guid.NewGuid(),
        JobPositionId = Guid.NewGuid(),
    };

    private static async Task<Env> CreateEnvAsync(UserPermissionDto[]? grants = null, string? readScope = "All")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var notifier = new RecordingNotifier();
        var service = new WorkService(
            new UnitOfWork(db),
            NullLogger<WorkService>.Instance,
            TimeProvider.System,
            new FakePermissionService(readScope, grants ?? []),
            notifier);

        var manager = NewEmployee("MGR");
        var assignee = NewEmployee("EMP", manager.Id);
        var stranger = NewEmployee("OTHER");
        var workType = new WorkType { Id = Guid.NewGuid(), Code = "WT-EBK", Name = "Dịch vụ E-banking", Status = RecordStatus.Active, UpdatedAt = DateTimeOffset.UtcNow };
        db.Employees.AddRange(manager, assignee, stranger);
        db.WorkTypes.Add(workType);
        db.JobPositionWorkTypes.Add(new JobPositionWorkType { Id = Guid.NewGuid(), JobPositionId = assignee.JobPositionId, WorkTypeId = workType.Id, UpdatedAt = DateTimeOffset.UtcNow });
        foreach (var employee in new[] { manager, assignee, stranger })
        {
            db.JobPositions.Add(new JobPosition { Id = employee.JobPositionId, Code = "POS-" + employee.EmployeeCode, Title = "Vị trí " + employee.EmployeeCode, Status = RecordStatus.Active, LastSyncAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            db.Accounts.Add(new Account { Id = employee.Id, EmployeeId = employee.Id, Username = employee.Email, PasswordHash = "x" });
        }

        await db.SaveChangesAsync();
        return new Env { Db = db, Service = service, Notifier = notifier, Manager = manager, Assignee = assignee, Stranger = stranger, WorkType = workType };
    }

    private static CreateWorkRequestDto CreateRequest(Env env, params string[] checklist) => new(
        Title: "E-banking - Đăng ký",
        WorkTypeId: env.WorkType.Id,
        AssigneeEmployeeId: env.Assignee.Id,
        Priority: "High",
        PlannedStartDate: new DateOnly(2026, 10, 24),
        PlannedEndDate: new DateOnly(2026, 10, 25),
        ChecklistItems: checklist,
        CustomerName: "  Trần Thị B ",
        CustomerCif: "0012345");

    private static readonly UserPermissionDto CreateAll = FakePermissionService.WorkCreate("All");

    private static async Task<WorkDetailDto> CreateWorkAsync(Env env, params string[] checklist) =>
        await env.Service.CreateWorkAsync(CreateRequest(env, checklist), env.Manager.Id);

    // ---- create ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateWorkAsync_StoresChecklistCustomerAndWhoAssignedIt_AndLogsTheAssignment()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;

        var detail = await CreateWorkAsync(env, "Xác minh giấy tờ", "  Tạo tài khoản ", "");

        Assert.Equal(["Xác minh giấy tờ", "Tạo tài khoản"], detail.ChecklistItems!.Select(c => c.Title));
        Assert.Equal([0, 1], detail.ChecklistItems!.Select(c => c.SortOrder));
        Assert.Equal(0, detail.ProgressPercent);
        Assert.Equal("Trần Thị B", detail.CustomerName);
        Assert.Equal("0012345", detail.CustomerCif);
        Assert.Equal(env.Manager.Id, detail.AssignedByEmployeeId);
        Assert.Equal(env.Manager.FullName, detail.AssignedByName);
        Assert.NotNull(detail.AssignedAt);
        var activities = await env.Service.GetActivitiesAsync(detail.Id, env.Manager.Id);
        var entry = Assert.Single(activities!);
        Assert.Equal("ASSIGNED", entry.Action);
        Assert.Equal(env.Manager.FullName, entry.ActorName);
    }

    [Fact]
    public async Task CreateWorkAsync_WithoutChecklistOrCustomer_StillWorks()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;

        var detail = await env.Service.CreateWorkAsync(
            CreateRequest(env) with { CustomerName = null, CustomerCif = " " }, env.Manager.Id);

        Assert.Empty(detail.ChecklistItems!);
        Assert.Null(detail.ProgressPercent);
        Assert.Null(detail.CustomerName);
        Assert.Null(detail.CustomerCif);
    }

    [Fact]
    public async Task CreateWorkAsync_TooManyOrTooLongChecklistItems_ThrowValidation()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;

        await Assert.ThrowsAsync<ValidationException>(() => CreateWorkAsync(env, Enumerable.Range(0, 31).Select(i => $"Mục {i}").ToArray()));
        await Assert.ThrowsAsync<ValidationException>(() => CreateWorkAsync(env, new string('x', 201)));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.CreateWorkAsync(
            CreateRequest(env) with { CustomerName = new string('n', 201) }, env.Manager.Id));
        Assert.Empty(db.Works);
    }

    // ---- checklist ------------------------------------------------------------------------------------

    [Fact]
    public async Task SetChecklistItemAsync_AssigneeTicksAnItem_UpdatesProgressAndLogsIt()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A", "B", "C", "D");

        var detail = await env.Service.SetChecklistItemAsync(
            work.Id, work.ChecklistItems![0].Id, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });
        detail = await env.Service.SetChecklistItemAsync(
            work.Id, work.ChecklistItems![1].Id, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });

        Assert.Equal(50, detail.ProgressPercent);
        Assert.True(detail.CanUpdateChecklist);
        Assert.False(detail.CanEditPlan);
        Assert.Equal(2, detail.ChecklistItems!.Count(c => c.IsDone && c.DoneAt != null));
        var activities = await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id);
        Assert.Equal("Cập nhật danh mục kiểm tra 2/4", activities![0].Message);
    }

    [Fact]
    public async Task SetChecklistItemAsync_SameStateTwice_DoesNotLogAgain_AndUntickingWorks()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A", "B");
        var item = work.ChecklistItems![0].Id;

        await env.Service.SetChecklistItemAsync(work.Id, item, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });
        await env.Service.SetChecklistItemAsync(work.Id, item, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });
        var detail = await env.Service.SetChecklistItemAsync(work.Id, item, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = false });

        Assert.Equal(0, detail.ProgressPercent);
        Assert.Equal(3, (await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))!.Count);
    }

    [Fact]
    public async Task SetChecklistItemAsync_NotTheAssignee_IsForbidden()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.SetChecklistItemAsync(
            work.Id, work.ChecklistItems![0].Id, env.Manager.Id, new WorkChecklistItemStateDto { IsDone = true }));
    }

    [Fact]
    public async Task SetChecklistItemAsync_InvalidInputs_ThrowTheRightErrors()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");
        var item = work.ChecklistItems![0].Id;
        var tick = new WorkChecklistItemStateDto { IsDone = true };

        await Assert.ThrowsAsync<ValidationException>(() => env.Service.SetChecklistItemAsync(work.Id, item, env.Assignee.Id, new WorkChecklistItemStateDto()));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Service.SetChecklistItemAsync(work.Id, Guid.NewGuid(), env.Assignee.Id, tick));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Service.SetChecklistItemAsync(Guid.NewGuid(), item, env.Assignee.Id, tick));

        (await db.Works.FindAsync(work.Id))!.Status = WorkStatus.Done;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() => env.Service.SetChecklistItemAsync(work.Id, item, env.Assignee.Id, tick));
    }

    // ---- plan edit ------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdatePlanAsync_ManagerChangesDeadlinePriorityAndChecklist_SavesLogsAndNotifies()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "Xác minh", "Tạo tài khoản", "Kích hoạt OTP", "Hướng dẫn");
        await env.Service.SetChecklistItemAsync(work.Id, work.ChecklistItems![0].Id, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });
        await env.Service.SetChecklistItemAsync(work.Id, work.ChecklistItems![1].Id, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });

        var detail = await env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = new DateOnly(2026, 10, 27),
            Priority = "Critical",
            Checklist =
            [
                new WorkPlanChecklistItemDto { Id = work.ChecklistItems![0].Id, Title = "Xác minh" },
                new WorkPlanChecklistItemDto { Id = work.ChecklistItems![1].Id, Title = "Tạo tài khoản" },
                new WorkPlanChecklistItemDto { Id = work.ChecklistItems![2].Id, Title = "Kích hoạt OTP (sửa)" },
                new WorkPlanChecklistItemDto { Title = "Gửi tài liệu qua email" },
            ],
            Note = "Khách hẹn lại ngày 27/10",
        });

        Assert.Equal(new DateOnly(2026, 10, 27), detail.PlannedEndDate);
        Assert.Equal("Critical", detail.Priority);
        Assert.Equal(["Xác minh", "Tạo tài khoản", "Kích hoạt OTP (sửa)", "Gửi tài liệu qua email"], detail.ChecklistItems!.Select(c => c.Title));
        Assert.Equal(50, detail.ProgressPercent);
        Assert.True(detail.CanEditPlan);
        var log = (await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))![0];
        Assert.Equal("PLAN_UPDATED", log.Action);
        Assert.Contains("25/10/2026 → 27/10/2026", log.Message, StringComparison.Ordinal);
        Assert.Contains("Khách hẹn lại ngày 27/10", log.Message, StringComparison.Ordinal);
        var draft = Assert.Single(env.Notifier.Drafts);
        Assert.Equal(env.Assignee.Id, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.WorkPlanUpdated, draft.Type);
        Assert.Contains("Khách hẹn lại ngày 27/10", draft.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatePlanAsync_RemovesNotDoneItemsLeftOut_ButNeverDoneOnes()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A", "B", "C");
        await env.Service.SetChecklistItemAsync(work.Id, work.ChecklistItems![0].Id, env.Assignee.Id, new WorkChecklistItemStateDto { IsDone = true });
        var keepDone = new WorkPlanChecklistItemDto { Id = work.ChecklistItems![0].Id, Title = "A" };

        var detail = await env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = new DateOnly(2026, 10, 25),
            Checklist = [keepDone, new WorkPlanChecklistItemDto { Id = work.ChecklistItems![1].Id, Title = "B" }],
        });
        Assert.Equal(["A", "B"], detail.ChecklistItems!.Select(c => c.Title));

        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = new DateOnly(2026, 10, 25),
            Checklist = [new WorkPlanChecklistItemDto { Id = work.ChecklistItems![1].Id, Title = "B" }],
        }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = new DateOnly(2026, 10, 25),
            Checklist = [new WorkPlanChecklistItemDto { Id = work.ChecklistItems![0].Id, Title = "A đổi tên" }],
        }));
    }

    [Fact]
    public async Task UpdatePlanAsync_NothingChangedAndNoNote_WritesNoActivityAndNoNotification()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        await env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate });

        Assert.Single((await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))!);
        Assert.Empty(env.Notifier.Drafts);
    }

    [Fact]
    public async Task UpdatePlanAsync_OnlyANote_IsLoggedAndSent()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        await env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate, Note = "Nhớ gọi trước" });

        Assert.Equal(2, (await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))!.Count);
        Assert.Single(env.Notifier.Drafts);
    }

    [Fact]
    public async Task UpdatePlanAsync_ExtendingAnOverdueWork_PutsItBackInProgress()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");
        (await db.Works.FindAsync(work.Id))!.Status = WorkStatus.Overdue;
        await db.SaveChangesAsync();
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        var detail = await env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = tomorrow });

        Assert.Equal("InProgress", detail.Status);
    }

    [Fact]
    public async Task UpdatePlanAsync_WhoMayEdit_AssigneeNeverStrangerOnlyWithScope()
    {
        var env = await CreateEnvAsync([]);
        using var db = env.Db;
        // Nobody here holds work:create, so the work is stored directly.
        var entity = new Work
        {
            Id = Guid.NewGuid(),
            Code = "WK-1",
            Title = "Work",
            AssigneeEmployeeId = env.Assignee.Id,
            CreatedBy = env.Manager.Id,
            Status = WorkStatus.InProgress,
            Priority = WorkPriority.Medium,
            PlannedEndDate = new DateOnly(2026, 10, 25),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Works.Add(entity);
        await db.SaveChangesAsync();
        var request = new UpdateWorkPlanDto { PlannedEndDate = new DateOnly(2026, 10, 30) };

        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.UpdatePlanAsync(entity.Id, env.Assignee.Id, request));
        await Assert.ThrowsAsync<ForbiddenException>(() => env.Service.UpdatePlanAsync(entity.Id, env.Stranger.Id, request));
        var byManager = await env.Service.UpdatePlanAsync(entity.Id, env.Manager.Id, request);
        Assert.Equal(new DateOnly(2026, 10, 30), byManager.PlannedEndDate);
    }

    [Fact]
    public async Task UpdatePlanAsync_AStrangerWithWorkCreateAllScope_MayEdit()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        var detail = await env.Service.UpdatePlanAsync(work.Id, env.Stranger.Id, new UpdateWorkPlanDto { PlannedEndDate = new DateOnly(2026, 10, 26) });

        Assert.Equal(new DateOnly(2026, 10, 26), detail.PlannedEndDate);
    }

    [Fact]
    public async Task UpdatePlanAsync_InvalidRequests_ThrowTheRightErrors()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto()));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = new DateOnly(2026, 10, 1) }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate, Priority = "Urgentest" }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate, Note = new string('n', 1001) }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = work.PlannedEndDate,
            Checklist = [new WorkPlanChecklistItemDto { Title = " " }],
        }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = work.PlannedEndDate,
            Checklist = [new WorkPlanChecklistItemDto { Id = Guid.NewGuid(), Title = "Lạ" }],
        }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = work.PlannedEndDate,
            Checklist = Enumerable.Range(0, 31).Select(i => new WorkPlanChecklistItemDto { Title = $"M{i}" }).ToList(),
        }));
        await Assert.ThrowsAsync<ValidationException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto
        {
            PlannedEndDate = work.PlannedEndDate,
            Checklist = [new WorkPlanChecklistItemDto { Id = work.ChecklistItems![0].Id, Title = "A" }, new WorkPlanChecklistItemDto { Id = work.ChecklistItems![0].Id, Title = "A" }],
        }));
        await Assert.ThrowsAsync<NotFoundException>(() => env.Service.UpdatePlanAsync(Guid.NewGuid(), env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate }));
        Assert.Single((await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))!);
    }

    [Theory]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Verified)]
    [InlineData(WorkStatus.Cancelled)]
    [InlineData(WorkStatus.Submitted)]
    public async Task UpdatePlanAsync_ClosedOrSubmittedWork_IsAConflict(WorkStatus status)
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");
        (await db.Works.FindAsync(work.Id))!.Status = status;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => env.Service.UpdatePlanAsync(work.Id, env.Manager.Id, new UpdateWorkPlanDto { PlannedEndDate = work.PlannedEndDate }));
    }

    // ---- reading --------------------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsTheFlagsForTheViewer_AndTheRelatedKpis()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Nhóm", UpdatedAt = DateTimeOffset.UtcNow };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-EBK-P",
            Name = "E-banking",
            Unit = "tk",
            CreditRole = KpiCreditRole.Processor,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.KpiGroups.Add(group);
        db.KpiDefinitions.Add(kpi);
        db.WorkTypeKpis.Add(new WorkTypeKpi
        {
            Id = Guid.NewGuid(),
            WorkTypeId = env.WorkType.Id,
            KpiDefinitionId = kpi.Id,
            IsRequired = true,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        var work = await CreateWorkAsync(env, "A");

        var asManager = (await env.Service.GetByIdAsync(work.Id, env.Manager.Id))!;
        var asAssignee = (await env.Service.GetByIdAsync(work.Id, env.Assignee.Id))!;

        Assert.True(asManager.CanEditPlan);
        Assert.False(asManager.CanUpdateChecklist);
        Assert.False(asAssignee.CanEditPlan);
        Assert.True(asAssignee.CanUpdateChecklist);
        var related = Assert.Single(asManager.RelatedKpis!);
        Assert.Equal(("KPI-EBK-P", "PROCESSOR"), (related.Code, related.CreditRole));
        Assert.Equal(env.Manager.FullName, asManager.AssignedByName);
    }

    [Fact]
    public async Task GetActivitiesAsync_IsNewestFirst_AndNullOutsideTheCallersScope()
    {
        var env = await CreateEnvAsync([CreateAll], readScope: "Own");
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");
        await env.Service.ChangeStatusAsync(work.Id, env.Assignee.Id, new WorkStatusDto { Status = "InProgress" });

        var log = (await env.Service.GetActivitiesAsync(work.Id, env.Assignee.Id))!;

        Assert.Equal(["STATUS_CHANGED", "ASSIGNED"], log.Select(a => a.Action));
        Assert.Equal("Chuyển sang Đang thực hiện", log[0].Message);
        Assert.Null(await env.Service.GetActivitiesAsync(work.Id, env.Stranger.Id));
        Assert.Null(await env.Service.GetActivitiesAsync(Guid.NewGuid(), env.Assignee.Id));
    }

    [Fact]
    public async Task ChangeStatusAsync_Cancelling_LogsTheReason()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        await env.Service.ChangeStatusAsync(work.Id, env.Manager.Id, new WorkStatusDto { Status = "Cancelled", Reason = "Khách huỷ" });

        Assert.Equal("Hủy công việc: Khách huỷ", (await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))![0].Message);
    }

    [Fact]
    public async Task MarkOverdueAsync_LogsASystemEntry()
    {
        var env = await CreateEnvAsync([CreateAll]);
        using var db = env.Db;
        var work = await CreateWorkAsync(env, "A");

        var marked = await env.Service.MarkOverdueAsync(new DateOnly(2026, 11, 1));

        Assert.Equal(1, marked);
        var entry = (await env.Service.GetActivitiesAsync(work.Id, env.Manager.Id))![0];
        Assert.Equal("Công việc quá hạn", entry.Message);
        Assert.Null(entry.ActorName);
    }
}
