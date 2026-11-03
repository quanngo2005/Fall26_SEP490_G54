using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.BusinessObjective;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Business objective CRUD and its read scope. World: Branch BR contains D1 (the caller's department) and D2.
/// Objectives are readable by a Dept caller only through a target on D1 (or an employee of D1).
/// </summary>
public sealed class BusinessObjectiveServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    private sealed record World(
        AppDbContext Db,
        Department Branch,
        Department D1,
        Department D2,
        Employee Caller,
        Account CallerAccount,
        KpiDefinition Kpi,
        EvaluationPeriod Period);

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static UserPermissionDto Grant(string scope, string action = "read") =>
        new("objective", action, scope, $"OBJECTIVE_{action.ToUpperInvariant()}");

    private static BusinessObjectiveService ServiceFor(AppDbContext db, params UserPermissionDto[] grants) =>
        new(db, new FakePermissionService(null, grants), new FixedTime(Now));

    private static async Task<World> CreateWorldAsync(AppDbContext db)
    {
        var branch = NewDepartment("BR", DepartmentLevelType.Branch, null);
        var d1 = NewDepartment("D1", DepartmentLevelType.Department, branch.Id);
        var d2 = NewDepartment("D2", DepartmentLevelType.Department, branch.Id);
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "CV", Title = "Officer", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI-1",
            Name = "Kpi",
            Unit = "VND",
            AggregationType = KpiAggregationType.Sum,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026",
            Name = "2026",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
        };
        var caller = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "CALLER",
            FullName = "Caller",
            Email = "caller@bank.com",
            DepartmentId = d1.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = caller.Id, Username = "caller", PasswordHash = "hash", LastSyncAt = Now, UpdatedAt = Now };

        db.AddRange(branch, d1, d2, position, group, kpi, period, caller, account);
        await db.SaveChangesAsync();
        return new World(db, branch, d1, d2, caller, account, kpi, period);
    }

    private static Department NewDepartment(string code, DepartmentLevelType level, Guid? parentId) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        LevelType = level,
        ParentDepartmentId = parentId,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static BusinessObjective NewObjective(
        string code,
        BusinessObjectiveStatus status = BusinessObjectiveStatus.Active,
        Guid? parentId = null) => new()
        {
            Id = Guid.NewGuid(),
            ParentObjectiveId = parentId,
            Code = code,
            Name = $"Objective {code}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = status,
            UpdatedAt = Now,
        };

    private static TargetAssignment NewTarget(World world, BusinessObjective objective, Guid? departmentId, Guid? employeeId = null) => new()
    {
        Id = Guid.NewGuid(),
        BusinessObjectiveId = objective.Id,
        KpiDefinitionId = world.Kpi.Id,
        DepartmentId = departmentId,
        EmployeeId = employeeId,
        EvaluationPeriodId = world.Period.Id,
        TargetValue = 10m,
        Status = TargetAssignmentStatus.Active,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static BusinessObjectiveSaveDto Save(string code = "OBJ-NEW") => new()
    {
        Code = code,
        Name = "New objective",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
    };

    // ---- read scope ---------------------------------------------------------------------------------

    [Fact]
    public async Task List_BranchScope_ReturnsEveryNonCancelledObjectiveByCode()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        db.BusinessObjectives.AddRange(NewObjective("B"), NewObjective("A"), NewObjective("C", BusinessObjectiveStatus.Cancelled));
        await db.SaveChangesAsync();

        var list = await ServiceFor(db, Grant("Branch")).GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto());

        Assert.Equal(["A", "B"], list.Select(o => o.Code).ToArray());
    }

    [Fact]
    public async Task List_StatusFilter_IsParsedAndShowsCancelledWhenAsked()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        db.BusinessObjectives.AddRange(NewObjective("A"), NewObjective("C", BusinessObjectiveStatus.Cancelled));
        await db.SaveChangesAsync();
        var service = ServiceFor(db, Grant("Branch"));

        var cancelled = await service.GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto { Status = "cancelled" });

        Assert.Equal("C", Assert.Single(cancelled).Code);
        Assert.Equal("CANCELLED", cancelled[0].Status);
        await Assert.ThrowsAsync<ValidationException>(
            () => service.GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto { Status = "9" }));
    }

    [Fact]
    public async Task List_KeywordMatchesCodeOrName()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var byName = NewObjective("X1");
        byName.Name = "Deposit growth";
        db.BusinessObjectives.AddRange(byName, NewObjective("DEP-2"), NewObjective("OTHER"));
        await db.SaveChangesAsync();

        var service = ServiceFor(db, Grant("Branch"));

        // The in-memory provider matches case-sensitively, so the keywords use the stored casing.
        var byCode = await service.GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto { Keyword = " DEP" });
        var byNameText = await service.GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto { Keyword = "Deposit" });

        Assert.Equal("DEP-2", Assert.Single(byCode).Code);
        Assert.Equal("X1", Assert.Single(byNameText).Code);
    }

    [Fact]
    public async Task List_DeptScope_ReturnsOnlyObjectivesWithATargetInTheCallersSubtree()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var inDepartment = NewObjective("IN-DEPT");
        var inEmployee = NewObjective("IN-EMP");
        var otherDepartment = NewObjective("OTHER-DEPT");
        var noTarget = NewObjective("NO-TARGET");
        db.BusinessObjectives.AddRange(inDepartment, inEmployee, otherDepartment, noTarget);
        db.TargetAssignments.AddRange(
            NewTarget(world, inDepartment, world.D1.Id),
            NewTarget(world, inEmployee, null, world.Caller.Id),
            NewTarget(world, otherDepartment, world.D2.Id));
        await db.SaveChangesAsync();

        var list = await ServiceFor(db, Grant("Dept")).GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto());

        Assert.Equal(["IN-DEPT", "IN-EMP"], list.Select(o => o.Code).ToArray());
    }

    [Fact]
    public async Task List_WithoutReadPermissionOrBelowDept_IsForbidden()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => ServiceFor(db).GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto()));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => ServiceFor(db, Grant("Own")).GetObjectivesAsync(world.CallerAccount.Id, new BusinessObjectiveFilterDto()));
    }

    [Fact]
    public async Task Detail_ReturnsKpisAndUsage_AndDeptScopeIsForbiddenWithoutATargetInScope()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var objective = NewObjective("OBJ");
        var child = NewObjective("CHILD", parentId: objective.Id);
        db.BusinessObjectives.AddRange(objective, child);
        db.ObjectiveKpis.Add(new ObjectiveKpi { Id = Guid.NewGuid(), BusinessObjectiveId = objective.Id, KpiDefinitionId = world.Kpi.Id, UpdatedAt = Now });
        db.TargetAssignments.Add(NewTarget(world, objective, world.D2.Id));
        await db.SaveChangesAsync();

        var branchView = await ServiceFor(db, Grant("Branch")).GetObjectiveAsync(world.CallerAccount.Id, objective.Id);

        Assert.Equal(new BusinessObjectiveUsageDto(1, 1, 0, 1), branchView.Usage);
        Assert.Equal("KPI-1", Assert.Single(branchView.Kpis).KpiCode);
        await Assert.ThrowsAsync<ForbiddenException>(
            () => ServiceFor(db, Grant("Dept")).GetObjectiveAsync(world.CallerAccount.Id, objective.Id));
        await Assert.ThrowsAsync<NotFoundException>(
            () => ServiceFor(db, Grant("Branch")).GetObjectiveAsync(world.CallerAccount.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Detail_DeptScope_CountsOnlyTheTargetsTheCallerMaySee()
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var objective = NewObjective("OBJ");
        db.BusinessObjectives.Add(objective);
        db.TargetAssignments.AddRange(NewTarget(world, objective, world.D1.Id), NewTarget(world, objective, world.D2.Id));
        await db.SaveChangesAsync();

        var view = await ServiceFor(db, Grant("Dept")).GetObjectiveAsync(world.CallerAccount.Id, objective.Id);

        Assert.Equal(1, view.Usage.TargetCount);
    }

    // ---- create ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_TrimsAndStoresTheObjectiveAsActive()
    {
        using var db = CreateDb();
        var dto = Save() with { Code = "  OBJ-1  ", Name = "  Growth ", Description = "  ", PriorityWeight = 12.5m };

        var created = await ServiceFor(db).CreateObjectiveAsync(dto);

        Assert.Equal("OBJ-1", created.Code);
        Assert.Equal("Growth", created.Name);
        Assert.Null(created.Description);
        Assert.Equal(12.5m, created.PriorityWeight);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(Now, created.UpdatedAt);
        Assert.Single(db.BusinessObjectives);
    }

    [Fact]
    public async Task Create_DuplicateCode_IsConflictAndStoresNothing()
    {
        using var db = CreateDb();
        db.BusinessObjectives.Add(NewObjective("OBJ-1"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => ServiceFor(db).CreateObjectiveAsync(Save("OBJ-1")));

        Assert.Single(db.BusinessObjectives);
    }

    [Fact]
    public async Task Create_InvalidInput_IsValidationErrorAndStoresNothing()
    {
        using var db = CreateDb();
        var service = ServiceFor(db);
        var codeMax = db.Model.FindEntityType(typeof(BusinessObjective))!.FindProperty(nameof(BusinessObjective.Code))!.GetMaxLength()!.Value;
        var nameMax = db.Model.FindEntityType(typeof(BusinessObjective))!.FindProperty(nameof(BusinessObjective.Name))!.GetMaxLength()!.Value;

        var invalid = new[]
        {
            Save() with { Code = " " },
            Save() with { Name = "" },
            Save() with { Code = new string('c', codeMax + 1) },
            Save() with { Name = new string('n', nameMax + 1) },
            Save() with { StartDate = null },
            Save() with { EndDate = null },
            Save() with { StartDate = new DateOnly(2026, 6, 2), EndDate = new DateOnly(2026, 6, 1) },
            Save() with { PriorityWeight = -0.0001m },
            Save() with { PriorityWeight = 1_000_000m },
        };
        foreach (var dto in invalid)
        {
            await Assert.ThrowsAsync<ValidationException>(() => service.CreateObjectiveAsync(dto));
        }

        Assert.Empty(db.BusinessObjectives);
    }

    [Fact]
    public async Task Create_CodeAndNameAtTheColumnLimit_AreAccepted_AndTheMessageStatesTheRealLimit()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(BusinessObjective))!;
        var codeMax = entity.FindProperty(nameof(BusinessObjective.Code))!.GetMaxLength()!.Value;
        var nameMax = entity.FindProperty(nameof(BusinessObjective.Name))!.GetMaxLength()!.Value;
        var service = ServiceFor(db);

        await service.CreateObjectiveAsync(Save(new string('c', codeMax)) with { Name = new string('n', nameMax) });
        var error = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateObjectiveAsync(Save(new string('d', codeMax + 1))));

        Assert.Contains(codeMax.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Contains(nameMax.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ParentMustExist()
    {
        using var db = CreateDb();

        await Assert.ThrowsAsync<NotFoundException>(
            () => ServiceFor(db).CreateObjectiveAsync(Save() with { ParentObjectiveId = Guid.NewGuid() }));

        Assert.Empty(db.BusinessObjectives);
    }

    [Fact]
    public async Task Create_WithAnExistingParent_LinksIt()
    {
        using var db = CreateDb();
        var parent = NewObjective("PARENT");
        db.BusinessObjectives.Add(parent);
        await db.SaveChangesAsync();

        var created = await ServiceFor(db).CreateObjectiveAsync(Save() with { ParentObjectiveId = parent.Id });

        Assert.Equal(parent.Id, created.ParentObjectiveId);
    }

    // ---- update ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ReplacesTheFieldsAndKeepsTheStatus()
    {
        using var db = CreateDb();
        var objective = NewObjective("OBJ-1", BusinessObjectiveStatus.Draft);
        db.BusinessObjectives.Add(objective);
        await db.SaveChangesAsync();

        var updated = await ServiceFor(db).UpdateObjectiveAsync(
            objective.Id,
            Save("OBJ-1") with { Name = "Renamed", PriorityWeight = 3m });

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(3m, updated.PriorityWeight);
        Assert.Equal("DRAFT", updated.Status);
    }

    [Theory]
    [InlineData(BusinessObjectiveStatus.Closed)]
    [InlineData(BusinessObjectiveStatus.Cancelled)]
    public async Task Update_ClosedOrCancelledObjective_IsConflictAndNothingChanges(BusinessObjectiveStatus status)
    {
        using var db = CreateDb();
        var objective = NewObjective("OBJ-1", status);
        db.BusinessObjectives.Add(objective);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => ServiceFor(db).UpdateObjectiveAsync(objective.Id, Save("OBJ-1") with { Name = "Renamed" }));

        Assert.Equal("Objective OBJ-1", (await db.BusinessObjectives.SingleAsync()).Name);
    }

    [Fact]
    public async Task Update_UnknownObjective_IsNotFound()
    {
        using var db = CreateDb();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceFor(db).UpdateObjectiveAsync(Guid.NewGuid(), Save()));
    }

    [Fact]
    public async Task Update_CodeOfAnotherObjective_IsConflict_ButKeepingItsOwnCodeIsFine()
    {
        using var db = CreateDb();
        var first = NewObjective("A");
        var second = NewObjective("B");
        db.BusinessObjectives.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = ServiceFor(db);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateObjectiveAsync(second.Id, Save("A")));
        var kept = await service.UpdateObjectiveAsync(second.Id, Save("B"));

        Assert.Equal("B", kept.Code);
    }

    [Fact]
    public async Task Update_ParentCannotBeItselfOrADescendant()
    {
        using var db = CreateDb();
        var root = NewObjective("ROOT");
        var child = NewObjective("CHILD", parentId: root.Id);
        var grandChild = NewObjective("GRAND", parentId: child.Id);
        db.BusinessObjectives.AddRange(root, child, grandChild);
        await db.SaveChangesAsync();
        var service = ServiceFor(db);

        await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateObjectiveAsync(root.Id, Save("ROOT") with { ParentObjectiveId = root.Id }));
        await Assert.ThrowsAsync<ValidationException>(
            () => service.UpdateObjectiveAsync(root.Id, Save("ROOT") with { ParentObjectiveId = grandChild.Id }));

        Assert.Null((await db.BusinessObjectives.SingleAsync(o => o.Id == root.Id)).ParentObjectiveId);
    }

    // ---- delete ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_UnusedObjective_IsRemoved()
    {
        using var db = CreateDb();
        var objective = NewObjective("OBJ-1");
        db.BusinessObjectives.Add(objective);
        await db.SaveChangesAsync();

        await ServiceFor(db).DeleteObjectiveAsync(objective.Id);

        Assert.Empty(db.BusinessObjectives);
    }

    [Fact]
    public async Task Delete_UnknownObjective_IsNotFound()
    {
        using var db = CreateDb();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceFor(db).DeleteObjectiveAsync(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("kpi")]
    [InlineData("target")]
    [InlineData("campaign")]
    [InlineData("child")]
    public async Task Delete_ObjectiveInUse_IsConflictWithTheInUseCodeAndKeepsTheObjective(string usedBy)
    {
        using var db = CreateDb();
        var world = await CreateWorldAsync(db);
        var objective = NewObjective("OBJ-1");
        db.BusinessObjectives.Add(objective);
        switch (usedBy)
        {
            case "kpi":
                db.ObjectiveKpis.Add(new ObjectiveKpi { Id = Guid.NewGuid(), BusinessObjectiveId = objective.Id, KpiDefinitionId = world.Kpi.Id, UpdatedAt = Now });
                break;
            case "target":
                db.TargetAssignments.Add(NewTarget(world, objective, world.D1.Id));
                break;
            case "campaign":
                db.BusinessCampaigns.Add(new BusinessCampaign
                {
                    Id = Guid.NewGuid(),
                    BusinessObjectiveId = objective.Id,
                    Code = "CMP",
                    Name = "Campaign",
                    StartDate = new DateOnly(2026, 1, 1),
                    EndDate = new DateOnly(2026, 2, 1),
                    Status = BusinessCampaignStatus.Planned,
                    UpdatedAt = Now,
                });
                break;
            default:
                db.BusinessObjectives.Add(NewObjective("CHILD", parentId: objective.Id));
                break;
        }

        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ConflictException>(() => ServiceFor(db).DeleteObjectiveAsync(objective.Id));

        Assert.Equal(ErrorCodes.BusinessObjectiveInUse, error.ErrorCode);
        Assert.True(await db.BusinessObjectives.AnyAsync(o => o.Id == objective.Id));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
