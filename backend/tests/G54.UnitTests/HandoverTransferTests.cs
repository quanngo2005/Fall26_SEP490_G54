using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// Two handover flows told apart by role (GB-779): a peer swap <c>A -&gt; A1</c> (same roles, the recipient answers) and a
/// work transfer <c>A -&gt; B</c> (different roles, needs enough progress, goes to a random <c>B</c> or waits in the pool).
/// </summary>
public sealed class HandoverTransferTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingNotifier _notifier = new();

    public void Dispose() => _db.Dispose();

    private HandoverService ServiceWith(string? mode = null, string? minProgress = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Handover:TransferAssignmentMode"] = mode,
            ["Handover:TransferMinProgressPercent"] = minProgress,
        };

        return new HandoverService(
            _db,
            new FakePermissionService(null),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            _notifier,
            NullLogger<HandoverService>.Instance,
            new FixedTimeProvider());
    }

    private async Task<Employee> AddEmployeeAsync(string code, string roleCode, Guid? departmentId = null, Guid? managerId = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = departmentId ?? Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            ManagerId = managerId,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var account = new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "hash", LastSyncAt = Now, UpdatedAt = Now };
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Code == roleCode)
            ?? _db.Roles.Add(new Role { Id = Guid.NewGuid(), Code = roleCode, Name = roleCode, Status = RecordStatus.Active, UpdatedAt = Now }).Entity;
        _db.Employees.Add(employee);
        _db.Accounts.Add(account);
        _db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), AccountId = account.Id, RoleId = role.Id, UpdatedAt = Now });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<Work> AddWorkAsync(Employee assignee, decimal progress)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{Guid.NewGuid():N}"[..10],
            Title = "Work",
            AssigneeEmployeeId = assignee.Id,
            Status = WorkStatus.InProgress,
            Priority = WorkPriority.Medium,
            ProgressPercent = progress,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return work;
    }

    private static CreateHandoverRequestDto ToEmployee(Guid toEmployeeId) =>
        new() { ToEmployeeId = toEmployeeId, Reason = "Khach hang o khu vuc khac", Amount = 1_000m };

    private static CreateHandoverRequestDto ToRole(string roleCode) =>
        new() { TargetRoleCode = roleCode, Reason = "Chuyen sang bo phan xu ly", Amount = 1_000m };

    [Fact]
    public async Task Create_SameRole_IsAPendingPeerSwapThatNotifiesTheRecipientAndTheManager()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        var a1 = await AddEmployeeAsync("EMP-A1", "EMPLOYEE", managerId: manager.Id);
        var work = await AddWorkAsync(a, progress: 0m);

        var created = await ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToEmployee(a1.Id));

        Assert.Equal("PENDING", created.Status);
        Assert.Equal(a1.Id, created.ToEmployeeId);
        Assert.Equal([a1.Id, manager.Id], _notifier.Drafts.Select(d => d.RecipientEmployeeId).ToArray());
        Assert.Equal(a.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Reject_SameRolePeerSwap_KeepsTheWorkWithTheRequester()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var a1 = await AddEmployeeAsync("EMP-A1", "EMPLOYEE");
        var work = await AddWorkAsync(a, progress: 0m);
        var service = ServiceWith();
        var created = await service.CreateHandoverAsync(work.Id, a.Id, ToEmployee(a1.Id));

        var rejected = await service.RejectHandoverAsync(created.Id, a1.Id);

        Assert.Equal("REJECTED", rejected.Status);
        var stored = await _db.Works.SingleAsync();
        Assert.Equal((a.Id, WorkStatus.InProgress), (stored.AssigneeEmployeeId, stored.Status));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(89.99)]
    public async Task Create_CrossRole_BelowTheDefaultMinimumProgress_Returns422AndWritesNothing(double progress)
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, (decimal)progress);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToEmployee(b.Id)));

        Assert.Equal((ErrorCodes.HandoverProgressBelowThreshold, 422), (ex.ErrorCode, ex.StatusCode));
        Assert.Empty(_db.TaskHandovers);
    }

    [Fact]
    public async Task Create_CrossRole_ExactlyAtTheMinimumProgress_IsAllowed()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);

        var result = await ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToEmployee(b.Id));

        Assert.Equal("ACCEPTED", result.Status);
    }

    [Fact]
    public async Task Create_CrossRole_UsesTheConfiguredMinimumProgress()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 60m);

        var allowed = await ServiceWith(minProgress: "50").CreateHandoverAsync(work.Id, a.Id, ToEmployee(b.Id));

        Assert.Equal("ACCEPTED", allowed.Status);
    }

    [Fact]
    public async Task Create_CrossRole_RandomMode_TransfersAtOnceAndTheRecipientCannotReject()
    {
        var departmentId = Guid.NewGuid();
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", departmentId);
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER", departmentId);
        var work = await AddWorkAsync(a, progress: 95m);
        var service = ServiceWith();

        var result = await service.CreateHandoverAsync(work.Id, a.Id, ToEmployee(b.Id));

        Assert.Equal(("ACCEPTED", b.Id, (DateTimeOffset?)Now), (result.Status, result.ToEmployeeId, result.AcceptedAt));
        var stored = await _db.Works.SingleAsync();
        Assert.Equal((b.Id, WorkStatus.HandedOver), (stored.AssigneeEmployeeId, stored.Status));
        var reject = await Assert.ThrowsAsync<ConflictException>(() => service.RejectHandoverAsync(result.Id, b.Id));
        Assert.Equal(ErrorCodes.HandoverNotPending, reject.ErrorCode);
    }

    [Fact]
    public async Task Create_CrossRole_ByTargetRole_PicksAnActiveHolderOfThatRoleFromTheSameDepartment()
    {
        var departmentId = Guid.NewGuid();
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", departmentId);
        var sameDepartment = await AddEmployeeAsync("EMP-B1", "VERIFIER", departmentId);
        await AddEmployeeAsync("EMP-B2", "VERIFIER");
        var inactive = await AddEmployeeAsync("EMP-B3", "VERIFIER", departmentId);
        inactive.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(a, progress: 100m);

        var result = await ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToRole("verifier"));

        Assert.Equal(sameDepartment.Id, result.ToEmployeeId);
    }

    [Fact]
    public async Task Create_CrossRole_ByTargetRole_FallsBackToAnotherDepartmentWhenNobodyHoldsTheRoleInTheSameOne()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var elsewhere = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 100m);

        var result = await ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        Assert.Equal(elsewhere.Id, result.ToEmployeeId);
    }

    [Fact]
    public async Task Create_CrossRole_ByTargetRole_WithNoEligibleEmployee_Returns422()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        _db.Roles.Add(new Role { Id = Guid.NewGuid(), Code = "VERIFIER", Name = "Verifier", Status = RecordStatus.Active, UpdatedAt = Now });
        var work = await AddWorkAsync(a, progress: 100m);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER")));

        Assert.Equal((ErrorCodes.HandoverNoEligibleRecipient, 422), (ex.ErrorCode, ex.StatusCode));
        Assert.Empty(_db.TaskHandovers);
    }

    [Fact]
    public async Task Create_ByUnknownTargetRole_Returns400()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var work = await AddWorkAsync(a, progress: 100m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, a.Id, ToRole("NO-SUCH-ROLE")));

        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_WithoutRecipientNorTargetRole_Returns400()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        var work = await AddWorkAsync(a, progress: 100m);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, a.Id, new CreateHandoverRequestDto { Reason = "x", Amount = 1m }));

        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_CrossRole_PoolMode_WaitsForTheManagerAndNotifiesOnlyTheManager()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);

        var pooled = await ServiceWith(mode: "assigned").CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        Assert.Equal("IN_POOL", pooled.Status);
        Assert.Null(pooled.ToEmployeeId);
        Assert.Null(pooled.ToEmployeeName);
        Assert.Equal(a.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(manager.Id, Assert.Single(_notifier.Drafts).RecipientEmployeeId);
    }

    [Fact]
    public async Task GetPool_ListsOnlyThePooledRequestsOfTheCallersDirectReports()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var otherManager = await AddEmployeeAsync("MGR2", "MANAGER");
        var mine = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        var theirs = await AddEmployeeAsync("EMP-C", "EMPLOYEE", managerId: otherManager.Id);
        await AddEmployeeAsync("EMP-B", "VERIFIER");
        var service = ServiceWith(mode: "ASSIGNED");
        var pooled = await service.CreateHandoverAsync((await AddWorkAsync(mine, 100m)).Id, mine.Id, ToRole("VERIFIER"));
        await service.CreateHandoverAsync((await AddWorkAsync(theirs, 100m)).Id, theirs.Id, ToRole("VERIFIER"));

        var list = await service.GetPoolAsync(manager.Id);

        Assert.Equal(pooled.Id, Assert.Single(list).Id);
        Assert.Empty(await service.GetPoolAsync(mine.Id));
    }

    [Fact]
    public async Task ReassignByManager_AssignsAPooledRequestToB()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);
        var service = ServiceWith(mode: "ASSIGNED");
        var pooled = await service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        var assigned = await service.ReassignByManagerAsync(pooled.Id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = b.Id });

        Assert.Equal(("ACCEPTED", b.Id), (assigned.Status, assigned.ToEmployeeId));
        var stored = await _db.Works.SingleAsync();
        Assert.Equal((b.Id, WorkStatus.HandedOver), (stored.AssigneeEmployeeId, stored.Status));
    }

    [Fact]
    public async Task ApproveByManager_OnAPooledRequestWithoutRecipient_Returns400()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);
        var service = ServiceWith(mode: "ASSIGNED");
        var pooled = await service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.ApproveByManagerAsync(pooled.Id, manager.Id));

        Assert.Equal(ErrorCodes.ValidationError, ex.ErrorCode);
        Assert.Equal(HandoverStatus.InPool, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task RejectByManager_OnAPooledRequest_KeepsTheWorkWithA()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);
        var service = ServiceWith(mode: "ASSIGNED");
        var pooled = await service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        var rejected = await service.RejectByManagerAsync(pooled.Id, manager.Id);

        Assert.Equal("REJECTED", rejected.Status);
        Assert.Equal(a.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
    }

    [Fact]
    public async Task ReassignByManager_ByAnotherManager_Returns403()
    {
        var manager = await AddEmployeeAsync("MGR", "MANAGER");
        var stranger = await AddEmployeeAsync("MGR2", "MANAGER");
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE", managerId: manager.Id);
        var b = await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);
        var service = ServiceWith(mode: "ASSIGNED");
        var pooled = await service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.ReassignByManagerAsync(pooled.Id, stranger.Id, new ReassignHandoverRequestDto { ToEmployeeId = b.Id }));

        Assert.Equal(HandoverStatus.InPool, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Create_WhileAPooledRequestExists_Returns409()
    {
        var a = await AddEmployeeAsync("EMP-A", "EMPLOYEE");
        await AddEmployeeAsync("EMP-B", "VERIFIER");
        var work = await AddWorkAsync(a, progress: 90m);
        var service = ServiceWith(mode: "ASSIGNED");
        await service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHandoverAsync(work.Id, a.Id, ToRole("VERIFIER")));

        Assert.Equal(ErrorCodes.HandoverAlreadyPending, ex.ErrorCode);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
