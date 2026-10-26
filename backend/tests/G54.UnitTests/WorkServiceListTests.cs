using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class WorkServiceListTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Department _branch = null!;
    private Department _sales = null!;
    private Department _team = null!;
    private Department _teller = null!;
    private Employee _manager = null!;      // in Sales
    private Employee _member = null!;       // in Team (under Sales)
    private Employee _teller1 = null!;      // in Teller (sibling of Sales)
    private Account _managerAccount = null!;
    private Account _memberAccount = null!;

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        _branch = Dept("CN", DepartmentLevelType.Branch, null);
        _sales = Dept("CN-KD", DepartmentLevelType.Department, _branch.Id);
        _team = Dept("CN-KD-N1", DepartmentLevelType.Team, _sales.Id);
        _teller = Dept("CN-GD", DepartmentLevelType.Department, _branch.Id);
        _manager = Emp("M1", _sales.Id);
        _member = Emp("E1", _team.Id);
        _teller1 = Emp("T1", _teller.Id);
        _managerAccount = Acc(_manager);
        _memberAccount = Acc(_member);

        _db.Departments.AddRange(_branch, _sales, _team, _teller);
        _db.Employees.AddRange(_manager, _member, _teller1);
        _db.Accounts.AddRange(_managerAccount, _memberAccount);
        await _db.SaveChangesAsync();
    }

    private static Department Dept(string code, DepartmentLevelType level, Guid? parent) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = $"Tên {code}",
        LevelType = level,
        ParentDepartmentId = parent,
        Status = RecordStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static Employee Emp(string code, Guid departmentId) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeCode = code,
        FullName = $"NV {code}",
        Email = $"{code.ToLowerInvariant()}@x.local",
        DepartmentId = departmentId,
        JobPositionId = Guid.NewGuid(),
        Status = EmployeeStatus.Active,
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private static Account Acc(Employee e) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = e.Id,
        Username = e.EmployeeCode,
        PasswordHash = "h",
        LastSyncAt = Now,
        UpdatedAt = Now,
    };

    private async Task<Work> AddWorkAsync(
        string code,
        Employee assignee,
        WorkStatus status = WorkStatus.New,
        WorkPriority priority = WorkPriority.Medium,
        string? title = null,
        int updatedMinutesAgo = 0,
        Guid? workTypeId = null)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = title ?? $"Công việc {code}",
            AssigneeEmployeeId = assignee.Id,
            CreatedBy = _manager.Id,
            Status = status,
            Priority = priority,
            WorkTypeId = workTypeId,
            UpdatedAt = Now.AddMinutes(-updatedMinutesAgo),
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return work;
    }

    private WorkService Service(string? scope) =>
        new(new UnitOfWork(_db), NullLogger<WorkService>.Instance, TimeProvider.System, new FakePermissionService(scope));

    private static WorkFilterDto Filter(Action<WorkFilterDto>? change = null)
    {
        var filter = new WorkFilterDto();
        change?.Invoke(filter);
        return filter;
    }

    // ----- Scope ----------------------------------------------------------------------------

    [Fact]
    public async Task AllScope_SeesEveryWork_NewestFirst()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member, updatedMinutesAgo: 30);
        await AddWorkAsync("W2", _teller1, updatedMinutesAgo: 10);
        await AddWorkAsync("W3", _manager, updatedMinutesAgo: 20);

        var result = await Service("All").GetWorksAsync(_managerAccount.Id, Filter());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["W2", "W3", "W1"], result.Items.Select(i => i.Code));
        var item = result.Items[0];
        Assert.Equal("T1", item.AssigneeEmployeeCode);
        Assert.Equal("CN-GD", item.DepartmentCode);
        Assert.Equal("New", item.Status);
        Assert.Equal("Medium", item.Priority);
    }

    [Fact]
    public async Task DepartmentScope_SeesTheOwnSubtreeOnly()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member);
        await AddWorkAsync("W2", _teller1);
        await AddWorkAsync("W3", _manager);

        var result = await Service("Department").GetWorksAsync(_managerAccount.Id, Filter());

        Assert.Equal(["W1", "W3"], result.Items.Select(i => i.Code).Order());
    }

    [Fact]
    public async Task BranchScope_SeesTheWholeBranch()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member);
        await AddWorkAsync("W2", _teller1);

        var result = await Service("Branch").GetWorksAsync(_managerAccount.Id, Filter());

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task OwnScope_SeesOnlyTheCallersWorks_EvenWhenFilteringForSomeoneElse()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member);
        await AddWorkAsync("W2", _manager);

        var own = await Service("Own").GetWorksAsync(_memberAccount.Id, Filter());
        var other = await Service("Own").GetWorksAsync(_memberAccount.Id, Filter(f => f.AssigneeEmployeeId = _manager.Id));

        Assert.Equal(["W1"], own.Items.Select(i => i.Code));
        Assert.Empty(other.Items);
    }

    [Fact]
    public async Task NoPermissionLockedAccountOrUnknownAccount_AreForbidden()
    {
        await SeedAsync();
        var locked = await _db.Accounts.SingleAsync(a => a.Id == _memberAccount.Id);
        locked.IsLocked = true;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(null).GetWorksAsync(_managerAccount.Id, Filter()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service("All").GetWorksAsync(_memberAccount.Id, Filter()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Service("All").GetWorksAsync(Guid.NewGuid(), Filter()));
    }

    // ----- Filters --------------------------------------------------------------------------

    [Fact]
    public async Task FiltersByOneOrMoreStatuses_InAnyCasing()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member, WorkStatus.New);
        await AddWorkAsync("W2", _member, WorkStatus.InProgress);
        await AddWorkAsync("W3", _member, WorkStatus.Overdue);
        await AddWorkAsync("W4", _member, WorkStatus.Done);
        var service = Service("All");

        var one = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Status = ["InProgress"]));
        var snake = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Status = ["in_progress"]));
        var many = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Status = ["New", "overdue,DONE"]));

        Assert.Equal(["W2"], one.Items.Select(i => i.Code));
        Assert.Equal(["W2"], snake.Items.Select(i => i.Code));
        Assert.Equal(["W1", "W3", "W4"], many.Items.Select(i => i.Code).Order());
        await Assert.ThrowsAsync<ValidationException>(() => service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Status = ["Nope"])));
    }

    [Fact]
    public async Task FiltersByAssignee()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member);
        await AddWorkAsync("W2", _manager);

        var result = await Service("All").GetWorksAsync(_managerAccount.Id, Filter(f => f.AssigneeEmployeeId = _member.Id));

        Assert.Equal(["W1"], result.Items.Select(i => i.Code));
    }

    [Fact]
    public async Task FiltersByDepartment_IncludingOrExcludingSubDepartments()
    {
        await SeedAsync();
        await AddWorkAsync("W1", _member);   // in Team (under Sales)
        await AddWorkAsync("W2", _manager);  // in Sales
        await AddWorkAsync("W3", _teller1);  // elsewhere
        var service = Service("All");

        var withChildren = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.DepartmentId = _sales.Id));
        var direct = await service.GetWorksAsync(
            _managerAccount.Id, Filter(f => { f.DepartmentId = _sales.Id; f.IncludeChildDepartments = false; }));

        Assert.Equal(["W1", "W2"], withChildren.Items.Select(i => i.Code).Order());
        Assert.Equal(["W2"], direct.Items.Select(i => i.Code));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetWorksAsync(_managerAccount.Id, Filter(f => f.DepartmentId = Guid.NewGuid())));
    }

    [Fact]
    public async Task DepartmentFilter_OutsideTheCallersScope_IsForbidden()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => Service("Department").GetWorksAsync(_managerAccount.Id, Filter(f => f.DepartmentId = _teller.Id)));
    }

    [Fact]
    public async Task FiltersByPriorityWorkTypeAndKeyword_AndCombinesFilters()
    {
        await SeedAsync();
        var type = Guid.NewGuid();
        await AddWorkAsync("W1", _member, priority: WorkPriority.High, title: "Mở tài khoản Alpha", workTypeId: type);
        await AddWorkAsync("W2", _member, priority: WorkPriority.Low, title: "Huy động Beta");
        await AddWorkAsync("W3", _manager, priority: WorkPriority.High, title: "Mở tài khoản Gamma");
        var service = Service("All");

        var byPriority = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Priority = "high"));
        var byType = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.WorkTypeId = type));
        var byTitle = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Keyword = "  ALPHA "));
        var byCode = await service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Keyword = "w2"));
        var combined = await service.GetWorksAsync(
            _managerAccount.Id, Filter(f => { f.Priority = "HIGH"; f.AssigneeEmployeeId = _manager.Id; f.Keyword = "tài khoản"; }));

        Assert.Equal(["W1", "W3"], byPriority.Items.Select(i => i.Code).Order());
        Assert.Equal(["W1"], byType.Items.Select(i => i.Code));
        Assert.Equal(["W1"], byTitle.Items.Select(i => i.Code));
        Assert.Equal(["W2"], byCode.Items.Select(i => i.Code));
        Assert.Equal(["W3"], combined.Items.Select(i => i.Code));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetWorksAsync(_managerAccount.Id, Filter(f => f.Priority = "URGENT")));
    }

    // ----- Paging ---------------------------------------------------------------------------

    [Fact]
    public async Task Paginates_AndClampsThePageSize()
    {
        await SeedAsync();
        for (var i = 1; i <= 5; i++)
        {
            await AddWorkAsync($"W{i}", _member, updatedMinutesAgo: i);
        }

        var service = Service("All");
        var second = await service.GetWorksAsync(_managerAccount.Id, Filter(f => { f.Page = 2; f.PageSize = 2; }));
        var beyond = await service.GetWorksAsync(_managerAccount.Id, Filter(f => { f.Page = 9; f.PageSize = 2; }));
        var clamped = await service.GetWorksAsync(_managerAccount.Id, Filter(f => { f.Page = 0; f.PageSize = 1000; }));

        Assert.Equal(["W3", "W4"], second.Items.Select(i => i.Code));
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(2, second.Page);
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(100, clamped.PageSize);
    }
}
