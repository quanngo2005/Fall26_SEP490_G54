using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class HandoverServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private RecordingNotifier Notifier { get; } = new();

    private HandoverService ServiceWith(bool canSetShare = false, string? threshold = null)
    {
        var settings = new Dictionary<string, string?>();
        if (threshold is not null)
        {
            settings["Handover:AmountThreshold"] = threshold;
        }

        return new HandoverService(
            _db,
            new SetSharePermission(canSetShare),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            Notifier,
            NullLogger<HandoverService>.Instance,
            new FixedTime());
    }

    private async Task<Employee> AddEmployeeAsync(string code, EmployeeStatus status = EmployeeStatus.Active, Guid? managerId = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            ManagerId = managerId,
            Status = status,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<Work> AddWorkAsync(Employee assignee, WorkStatus status = WorkStatus.InProgress)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{Guid.NewGuid():N}"[..10],
            Title = "Work",
            AssigneeEmployeeId = assignee.Id,
            Status = status,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return work;
    }

    private static CreateHandoverRequestDto Request(
        Guid toEmployeeId,
        decimal amount = 1_000m,
        decimal? share = null,
        string? reason = "Khach hang o khu vuc khac") =>
        new() { ToEmployeeId = toEmployeeId, Reason = reason, Amount = amount, ReferrerSharePercent = share };

    private async Task<(Employee From, Employee To, Work Work)> ArrangeAsync(
        WorkStatus status = WorkStatus.InProgress,
        string suffix = "")
    {
        var from = await AddEmployeeAsync("FROM" + suffix);
        var to = await AddEmployeeAsync("TO" + suffix);
        var work = await AddWorkAsync(from, status);
        return (from, to, work);
    }

    [Fact]
    public async Task Create_MovesTheWorkAtOnce_AndDefaultsRecipientToFullShare()
    {
        var (from, to, work) = await ArrangeAsync();

        var result = await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(Now, result.AcceptedAt);
        Assert.Equal(0m, result.ReferrerSharePercent);
        Assert.Equal(from.Id, result.FromEmployeeId);
        Assert.Equal(to.Id, result.ToEmployeeId);
        Assert.Equal(work.Code, result.WorkCode);
        Assert.Equal(Now, result.HandoverAt);

        var stored = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(HandoverStatus.Accepted, stored.Status);
        Assert.Equal(0m, stored.ReferrerSharePercent);
        Assert.Equal("Khach hang o khu vuc khac", stored.Reason);
        Assert.Null(stored.ApprovedBy);

        var storedWork = await _db.Works.SingleAsync();
        Assert.Equal(to.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, storedWork.Status);
    }

    [Fact]
    public async Task Create_ResolvesCallerByAccountId()
    {
        var (from, to, work) = await ArrangeAsync();
        var accountId = (await _db.Accounts.SingleAsync(a => a.EmployeeId == from.Id)).Id;

        var result = await ServiceWith().CreateHandoverAsync(work.Id, accountId, Request(to.Id));

        Assert.Equal(from.Id, result.FromEmployeeId);
    }

    [Fact]
    public async Task Create_TrimsReason()
    {
        var (from, to, work) = await ArrangeAsync();

        var result = await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id, reason: "  ly do  "));

        Assert.Equal("ly do", result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(35.5)]
    [InlineData(100)]
    public async Task Create_UsesGivenShare_WhenCallerMaySetShare(double share)
    {
        var (from, to, work) = await ArrangeAsync();

        var result = await ServiceWith(canSetShare: true)
            .CreateHandoverAsync(work.Id, from.Id, Request(to.Id, share: (decimal)share));

        Assert.Equal((decimal)share, result.ReferrerSharePercent);
    }

    [Fact]
    public async Task Create_ForbidsShare_WhenCallerLacksSetSharePermission()
    {
        var (from, to, work) = await ArrangeAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceWith(canSetShare: false).CreateHandoverAsync(work.Id, from.Id, Request(to.Id, share: 100m)));

        Assert.Empty(_db.TaskHandovers);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public async Task Create_RejectsShareOutOfRange(double share)
    {
        var (from, to, work) = await ArrangeAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith(canSetShare: true)
                .CreateHandoverAsync(work.Id, from.Id, Request(to.Id, share: (decimal)share)));

        Assert.Contains(ex.Errors!, e => e.Contains("điểm chia sẻ"));
    }

    [Fact]
    public async Task Create_AllowsAmountEqualToThreshold()
    {
        var (from, to, work) = await ArrangeAsync();

        var result = await ServiceWith(threshold: "1000")
            .CreateHandoverAsync(work.Id, from.Id, Request(to.Id, amount: 1000m));

        Assert.Equal("ACCEPTED", result.Status);
    }

    [Fact]
    public async Task Create_RejectsAmountAboveThreshold_AndStoresNothing()
    {
        var (from, to, work) = await ArrangeAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            ServiceWith(threshold: "1000").CreateHandoverAsync(work.Id, from.Id, Request(to.Id, amount: 1000.01m)));

        Assert.Equal(ErrorCodes.HandoverAmountExceedsThreshold, ex.ErrorCode);
        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("1000", ex.Message);
        Assert.Empty(_db.TaskHandovers);
    }

    [Fact]
    public async Task Create_UsesDefaultThreshold_WhenNotConfigured()
    {
        var (from, to, work) = await ArrangeAsync();
        var service = ServiceWith();

        var atLimit = await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id, amount: 500_000_000m));
        Assert.Equal("ACCEPTED", atLimit.Status);

        var (from2, to2, work2) = await ArrangeAsync(suffix: "2");
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateHandoverAsync(work2.Id, from2.Id, Request(to2.Id, amount: 500_000_001m)));
    }

    [Fact]
    public async Task Create_ForbidsNonAssignee()
    {
        var (_, to, work) = await ArrangeAsync();
        var stranger = await AddEmployeeAsync("OTHER");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, stranger.Id, Request(to.Id)));
    }

    [Fact]
    public async Task Create_ThrowsNotFound_WhenWorkMissing()
    {
        var (from, to, _) = await ArrangeAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceWith().CreateHandoverAsync(Guid.NewGuid(), from.Id, Request(to.Id)));
    }

    [Fact]
    public async Task Create_ThrowsNotFound_WhenRecipientMissing()
    {
        var (from, _, work) = await ArrangeAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(Guid.NewGuid())));
    }

    [Theory]
    [InlineData(WorkStatus.New, true)]
    [InlineData(WorkStatus.InProgress, true)]
    [InlineData(WorkStatus.HandedOver, false)]
    [InlineData(WorkStatus.Done, false)]
    [InlineData(WorkStatus.Cancelled, false)]
    [InlineData(WorkStatus.Overdue, false)]
    public async Task Create_RespectsWorkStatus(WorkStatus status, bool allowed)
    {
        var (from, to, work) = await ArrangeAsync(status);
        var service = ServiceWith();

        if (allowed)
        {
            Assert.Equal("ACCEPTED", (await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id))).Status);
            return;
        }

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id)));
        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsHandoverToSelf()
    {
        var (from, _, work) = await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(from.Id)));
    }

    [Fact]
    public async Task Create_RejectsInactiveRecipient()
    {
        var from = await AddEmployeeAsync("FROM");
        var inactive = await AddEmployeeAsync("GONE", EmployeeStatus.Inactive);
        var work = await AddWorkAsync(from);

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(inactive.Id)));
    }

    [Fact]
    public async Task Create_ThrowsUnauthorized_WhenCallerUnknownOrInactive()
    {
        var (_, to, work) = await ArrangeAsync();
        var inactive = await AddEmployeeAsync("OFF", EmployeeStatus.Inactive);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, Guid.NewGuid(), Request(to.Id)));
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, inactive.Id, Request(to.Id)));
    }

    [Fact]
    public async Task Create_ReportsEveryMissingField()
    {
        var (from, _, work) = await ArrangeAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, new CreateHandoverRequestDto()));

        Assert.Equal(3, ex.Errors!.Count);
        Assert.Contains(ex.Errors!, e => e.Contains("Người nhận"));
        Assert.Contains(ex.Errors!, e => e.Contains("Lí do"));
        Assert.Contains(ex.Errors!, e => e.Contains("Số lượng"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RejectsBlankReason(string reason)
    {
        var (from, to, work) = await ArrangeAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id, reason: reason)));

        Assert.Contains(ex.Errors!, e => e.Contains("Lí do"));
    }

    [Fact]
    public async Task Create_RejectsReasonLongerThan2000_ButAcceptsExactly2000()
    {
        var (from, to, work) = await ArrangeAsync();
        var service = ServiceWith();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id, reason: new string('a', 2001))));

        var ok = await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id, reason: new string('a', 2000)));
        Assert.Equal(2000, ok.Reason.Length);
    }

    [Fact]
    public async Task Create_RejectsNegativeAmount()
    {
        var (from, to, work) = await ArrangeAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id, amount: -1m)));

        Assert.Contains(ex.Errors!, e => e.Contains("Số lượng"));
    }

    [Fact]
    public async Task Create_RejectsEmptyRecipientId()
    {
        var (from, _, work) = await ArrangeAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(Guid.Empty)));
    }

    [Fact]
    public async Task Create_ThrowsOnNullRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ServiceWith().CreateHandoverAsync(Guid.NewGuid(), Guid.NewGuid(), null!));
    }

    private async Task<(Employee Manager, Employee Subordinate, Employee To, Work Work)> ArrangeTransferAsync(
        WorkStatus status = WorkStatus.InProgress)
    {
        var manager = await AddEmployeeAsync("BOSS");
        var subordinate = await AddEmployeeAsync("SUB", managerId: manager.Id);
        var to = await AddEmployeeAsync("TO");
        var work = await AddWorkAsync(subordinate, status);
        return (manager, subordinate, to, work);
    }

    [Fact]
    public async Task Transfer_ByDirectManager_MovesTheWorkAtOnce()
    {
        var (manager, sub, to, work) = await ArrangeTransferAsync();

        var result = await ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id));

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(Now, result.AcceptedAt);
        Assert.Equal(sub.Id, result.FromEmployeeId);
        Assert.Equal(to.Id, result.ToEmployeeId);
        Assert.Equal(0m, result.ReferrerSharePercent);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(to.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, storedWork.Status);

        var stored = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(HandoverStatus.Accepted, stored.Status);
        Assert.Equal(sub.Id, stored.FromEmployeeId);
    }

    [Fact]
    public async Task Transfer_ByDirectManager_MayChooseTheReferrerShare()
    {
        var (manager, _, to, work) = await ArrangeTransferAsync();

        var result = await ServiceWith(canSetShare: true)
            .CreateHandoverAsync(work.Id, manager.Id, Request(to.Id, share: 25m));

        Assert.Equal(25m, result.ReferrerSharePercent);
    }

    [Fact]
    public async Task Transfer_ForbidsAManagerWhoIsNotTheDirectManager()
    {
        var (_, _, to, work) = await ArrangeTransferAsync();
        var otherManager = await AddEmployeeAsync("BOSS2");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, otherManager.Id, Request(to.Id)));

        Assert.Empty(_db.TaskHandovers);
    }

    [Fact]
    public async Task Transfer_ForbidsTheDirectManagerWithoutTheManagerPermission()
    {
        var (manager, _, to, work) = await ArrangeTransferAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ServiceWith(canSetShare: false).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id)));

        Assert.Equal(WorkStatus.InProgress, (await _db.Works.SingleAsync(w => w.Id == work.Id)).Status);
    }

    [Fact]
    public async Task Transfer_RejectsHandingTheWorkToItsCurrentAssignee()
    {
        var (manager, sub, _, work) = await ArrangeTransferAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(sub.Id)));
    }

    [Fact]
    public async Task Transfer_StillAppliesTheThreshold()
    {
        var (manager, _, to, work) = await ArrangeTransferAsync();
        var service = ServiceWith(canSetShare: true, threshold: "100");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateHandoverAsync(work.Id, manager.Id, Request(to.Id, amount: 101m)));
    }

    [Fact]
    public async Task Transfer_Conflicts_WhenWorkCannotBeHandedOver()
    {
        var (manager, _, to, work) = await ArrangeTransferAsync(WorkStatus.Done);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id)));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task ManagerHandingOverTheirOwnWork_MovesItAtOnce()
    {
        var manager = await AddEmployeeAsync("BOSS");
        var to = await AddEmployeeAsync("TO");
        var work = await AddWorkAsync(manager);

        var result = await ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id));

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(to.Id, (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Create_TellsTheRecipientOnly_WhenTheAssigneeHandsOverTheirOwnWork()
    {
        var (from, to, work) = await ArrangeAsync();

        var handover = await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        var draft = Assert.Single(Notifier.Drafts);
        Assert.Equal(to.Id, draft.RecipientEmployeeId);
        Assert.Equal(NotificationType.HandoverTransferred, draft.Type);
        Assert.Equal(NotificationSeverity.Important, draft.Severity);
        Assert.Equal(NotificationRelatedType.Handover, draft.RelatedType);
        Assert.Equal(handover.Id, draft.RelatedId);
    }

    [Fact]
    public async Task ManagerTransfer_TellsBothEmployeesButNotTheManager()
    {
        var (manager, sub, to, work) = await ArrangeTransferAsync();

        await ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id));

        Assert.Equal(
            [(sub.Id, NotificationType.HandoverTransferred), (to.Id, NotificationType.HandoverTransferred)],
            Notifier.Drafts.Select(d => (d.RecipientEmployeeId, d.Type)).ToArray());
    }
    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class SetSharePermission(bool allowed) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> none = [];
            return Task.FromResult(none);
        }

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(allowed && resource == "handover" && action == "set_share");

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
