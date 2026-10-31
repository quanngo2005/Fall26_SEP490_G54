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
    public async Task Create_PersistsPendingHandover_AndDefaultsRecipientToFullShare()
    {
        var (from, to, work) = await ArrangeAsync();

        var result = await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        Assert.Equal("PENDING", result.Status);
        Assert.Equal(0m, result.ReferrerSharePercent);
        Assert.Equal(from.Id, result.FromEmployeeId);
        Assert.Equal(to.Id, result.ToEmployeeId);
        Assert.Equal(work.Code, result.WorkCode);
        Assert.Equal(Now, result.HandoverAt);

        var stored = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(HandoverStatus.Pending, stored.Status);
        Assert.Equal(0m, stored.ReferrerSharePercent);
        Assert.Equal("Khach hang o khu vuc khac", stored.Reason);
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
    public async Task Create_DoesNotChangeWorkStatus()
    {
        var (from, to, work) = await ArrangeAsync();

        await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        Assert.Equal(WorkStatus.InProgress, (await _db.Works.SingleAsync()).Status);
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

        Assert.Equal("PENDING", result.Status);
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
        Assert.Equal("PENDING", atLimit.Status);

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
            Assert.Equal("PENDING", (await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id))).Status);
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
    public async Task Create_RejectsSecondPendingHandover_ButAllowsAfterItIsClosed()
    {
        var (from, to, work) = await ArrangeAsync();
        var service = ServiceWith();
        var first = await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id)));
        Assert.Equal(ErrorCodes.HandoverAlreadyPending, ex.ErrorCode);

        (await _db.TaskHandovers.SingleAsync(h => h.Id == first.Id)).Status = HandoverStatus.Rejected;
        await _db.SaveChangesAsync();

        Assert.Equal("PENDING", (await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id))).Status);
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

    private async Task<(Employee From, Employee To, Work Work, Guid HandoverId)> ArrangePendingAsync(
        WorkStatus status = WorkStatus.InProgress)
    {
        var (from, to, work) = await ArrangeAsync(status);
        var created = await ServiceWith().CreateHandoverAsync(work.Id, from.Id, Request(to.Id));
        return (from, to, work, created.Id);
    }

    [Fact]
    public async Task Accept_MovesWorkToRecipient_AndMarksHandedOver()
    {
        var (from, to, work, id) = await ArrangePendingAsync();

        var result = await ServiceWith().AcceptHandoverAsync(id, to.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(Now, result.AcceptedAt);
        Assert.Equal(from.Id, result.FromEmployeeId);
        Assert.Equal(to.Id, result.ToEmployeeId);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(to.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, storedWork.Status);

        var stored = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(HandoverStatus.Accepted, stored.Status);
        Assert.Equal(Now, stored.AcceptedAt);
    }

    [Fact]
    public async Task Accept_WorksWhenWorkIsStillNew()
    {
        var (_, to, work, id) = await ArrangePendingAsync(WorkStatus.New);

        await ServiceWith().AcceptHandoverAsync(id, to.Id);

        Assert.Equal(WorkStatus.HandedOver, (await _db.Works.SingleAsync(w => w.Id == work.Id)).Status);
    }

    [Fact]
    public async Task Accept_ResolvesCallerByAccountId()
    {
        var (_, to, _, id) = await ArrangePendingAsync();
        var accountId = (await _db.Accounts.SingleAsync(a => a.EmployeeId == to.Id)).Id;

        var result = await ServiceWith().AcceptHandoverAsync(id, accountId);

        Assert.Equal("ACCEPTED", result.Status);
    }

    [Fact]
    public async Task Accept_ForbidsAnyoneButTheRecipient()
    {
        var (from, _, work, id) = await ArrangePendingAsync();
        var stranger = await AddEmployeeAsync("OTHER");

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceWith().AcceptHandoverAsync(id, from.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceWith().AcceptHandoverAsync(id, stranger.Id));

        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
        Assert.Equal(from.Id, (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Accept_ThrowsNotFound_WhenHandoverMissing()
    {
        var (_, to, _, _) = await ArrangePendingAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceWith().AcceptHandoverAsync(Guid.NewGuid(), to.Id));
    }

    [Fact]
    public async Task Accept_ThrowsUnauthorized_WhenCallerUnknownOrInactive()
    {
        var (_, _, _, id) = await ArrangePendingAsync();
        var inactive = await AddEmployeeAsync("OFF", EmployeeStatus.Inactive);

        await Assert.ThrowsAsync<UnauthorizedException>(() => ServiceWith().AcceptHandoverAsync(id, Guid.NewGuid()));
        await Assert.ThrowsAsync<UnauthorizedException>(() => ServiceWith().AcceptHandoverAsync(id, inactive.Id));
    }

    [Fact]
    public async Task Accept_RejectsAHandoverThatIsNoLongerPending()
    {
        var (_, to, _, id) = await ArrangePendingAsync();
        var service = ServiceWith();
        await service.AcceptHandoverAsync(id, to.Id);

        var again = await Assert.ThrowsAsync<ConflictException>(() => service.AcceptHandoverAsync(id, to.Id));
        var reject = await Assert.ThrowsAsync<ConflictException>(() => service.RejectHandoverAsync(id, to.Id));

        Assert.Equal(ErrorCodes.HandoverNotPending, again.ErrorCode);
        Assert.Equal(ErrorCodes.HandoverNotPending, reject.ErrorCode);
    }

    [Theory]
    [InlineData(WorkStatus.Cancelled)]
    [InlineData(WorkStatus.Done)]
    [InlineData(WorkStatus.Overdue)]
    [InlineData(WorkStatus.HandedOver)]
    public async Task Accept_Conflicts_WhenWorkCanNoLongerBeHandedOver(WorkStatus status)
    {
        var (from, to, work, id) = await ArrangePendingAsync();
        (await _db.Works.SingleAsync(w => w.Id == work.Id)).Status = status;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceWith().AcceptHandoverAsync(id, to.Id));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
        Assert.Equal(from.Id, (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Accept_Conflicts_WhenWorkWasReassignedMeanwhile()
    {
        var (_, to, work, id) = await ArrangePendingAsync();
        var someoneElse = await AddEmployeeAsync("ELSE");
        (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId = someoneElse.Id;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceWith().AcceptHandoverAsync(id, to.Id));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Reject_ClosesTheRequest_AndLeavesTheWorkWithTheRequester()
    {
        var (from, to, work, id) = await ArrangePendingAsync();

        var result = await ServiceWith().RejectHandoverAsync(id, to.Id);

        Assert.Equal("REJECTED", result.Status);
        Assert.Null(result.AcceptedAt);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(from.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.InProgress, storedWork.Status);
        Assert.Equal(HandoverStatus.Rejected, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reject_AllowsTheRequesterToAskAgain()
    {
        var (from, to, work, id) = await ArrangePendingAsync();
        var service = ServiceWith();
        await service.RejectHandoverAsync(id, to.Id);

        var again = await service.CreateHandoverAsync(work.Id, from.Id, Request(to.Id));

        Assert.Equal("PENDING", again.Status);
    }

    [Fact]
    public async Task Reject_StillWorks_WhenWorkWasCancelledMeanwhile()
    {
        var (_, to, work, id) = await ArrangePendingAsync();
        (await _db.Works.SingleAsync(w => w.Id == work.Id)).Status = WorkStatus.Cancelled;
        await _db.SaveChangesAsync();

        var result = await ServiceWith().RejectHandoverAsync(id, to.Id);

        Assert.Equal("REJECTED", result.Status);
    }

    [Fact]
    public async Task Reject_ForbidsAnyoneButTheRecipient()
    {
        var (from, _, _, id) = await ArrangePendingAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceWith().RejectHandoverAsync(id, from.Id));

        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task Reject_ThrowsNotFound_WhenHandoverMissing()
    {
        var (_, to, _, _) = await ArrangePendingAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceWith().RejectHandoverAsync(Guid.NewGuid(), to.Id));
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
        Assert.Equal(manager.Id, stored.ApprovedBy);
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
    public async Task Transfer_LeavesNothingForTheRecipientToDecide()
    {
        var (manager, _, to, work) = await ArrangeTransferAsync();
        var service = ServiceWith(canSetShare: true);
        var result = await service.CreateHandoverAsync(work.Id, manager.Id, Request(to.Id));

        var accept = await Assert.ThrowsAsync<ConflictException>(() => service.AcceptHandoverAsync(result.Id, to.Id));
        var reject = await Assert.ThrowsAsync<ConflictException>(() => service.RejectHandoverAsync(result.Id, to.Id));

        Assert.Equal(ErrorCodes.HandoverNotPending, accept.ErrorCode);
        Assert.Equal(ErrorCodes.HandoverNotPending, reject.ErrorCode);
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
    public async Task Transfer_StillAppliesTheWorkStatusThresholdAndPendingRules()
    {
        var (manager, sub, to, work) = await ArrangeTransferAsync();
        var service = ServiceWith(canSetShare: true, threshold: "100");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.CreateHandoverAsync(work.Id, manager.Id, Request(to.Id, amount: 101m)));

        // A pending request from the assignee blocks a transfer until it is answered.
        await service.CreateHandoverAsync(work.Id, sub.Id, Request(to.Id, amount: 1m));
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateHandoverAsync(work.Id, manager.Id, Request(to.Id, amount: 1m)));
        Assert.Equal(ErrorCodes.HandoverAlreadyPending, ex.ErrorCode);
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
    public async Task ManagerHandingOverTheirOwnWork_StillNeedsTheRecipientToAccept()
    {
        var manager = await AddEmployeeAsync("BOSS");
        var to = await AddEmployeeAsync("TO");
        var work = await AddWorkAsync(manager);

        var result = await ServiceWith(canSetShare: true).CreateHandoverAsync(work.Id, manager.Id, Request(to.Id));

        Assert.Equal("PENDING", result.Status);
        Assert.Equal(manager.Id, (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId);
    }

    private async Task<(Employee Manager, Employee Subordinate, Employee To, Work Work, Guid HandoverId)> ArrangePendingForManagerAsync(
        WorkStatus status = WorkStatus.InProgress)
    {
        var (manager, sub, to, work) = await ArrangeTransferAsync(status);
        var created = await ServiceWith().CreateHandoverAsync(work.Id, sub.Id, Request(to.Id));
        return (manager, sub, to, work, created.Id);
    }

    [Fact]
    public async Task ManagerApprove_MovesTheWorkToTheRecipientAtOnce()
    {
        var (manager, sub, to, work, id) = await ArrangePendingForManagerAsync();

        var result = await ServiceWith().ApproveByManagerAsync(id, manager.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(Now, result.AcceptedAt);
        Assert.Equal(sub.Id, result.FromEmployeeId);
        Assert.Equal(to.Id, result.ToEmployeeId);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(to.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, storedWork.Status);
        Assert.Equal(manager.Id, (await _db.TaskHandovers.SingleAsync()).ApprovedBy);
    }

    [Fact]
    public async Task ManagerReassign_GivesTheWorkToTheNewRecipient()
    {
        var (manager, sub, to, work, id) = await ArrangePendingForManagerAsync();
        var newRecipient = await AddEmployeeAsync("NEW");

        var result = await ServiceWith().ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = newRecipient.Id });

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(newRecipient.Id, result.ToEmployeeId);
        Assert.Equal(newRecipient.FullName, result.ToEmployeeName);
        Assert.Equal(sub.Id, result.FromEmployeeId);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(newRecipient.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, storedWork.Status);

        var stored = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(newRecipient.Id, stored.ToEmployeeId);
        Assert.Equal(manager.Id, stored.ApprovedBy);
        Assert.NotEqual(to.Id, stored.ToEmployeeId);
    }

    [Fact]
    public async Task ManagerReassign_ValidatesTheNewRecipient()
    {
        var (manager, sub, _, _, id) = await ArrangePendingForManagerAsync();
        var inactive = await AddEmployeeAsync("GONE", EmployeeStatus.Inactive);
        var service = ServiceWith();

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto()));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = Guid.Empty }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = sub.Id }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = inactive.Id }));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.ReassignByManagerAsync(id, manager.Id, null!));

        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task ManagerReject_ClosesTheRequest_AndLeavesTheWorkWithTheRequester()
    {
        var (manager, sub, _, work, id) = await ArrangePendingForManagerAsync();

        var result = await ServiceWith().RejectByManagerAsync(id, manager.Id);

        Assert.Equal("REJECTED", result.Status);
        Assert.Null(result.AcceptedAt);

        var storedWork = await _db.Works.SingleAsync(w => w.Id == work.Id);
        Assert.Equal(sub.Id, storedWork.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.InProgress, storedWork.Status);
        Assert.Null((await _db.TaskHandovers.SingleAsync()).ApprovedBy);
    }

    [Fact]
    public async Task ManagerDecisions_AreForbiddenForAnyoneButTheRequestersDirectManager()
    {
        var (_, sub, to, _, id) = await ArrangePendingForManagerAsync();
        var otherManager = await AddEmployeeAsync("BOSS2");
        var service = ServiceWith();

        foreach (var caller in new[] { otherManager.Id, sub.Id, to.Id })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveByManagerAsync(id, caller));
            await Assert.ThrowsAsync<ForbiddenException>(() => service.RejectByManagerAsync(id, caller));
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                service.ReassignByManagerAsync(id, caller, new ReassignHandoverRequestDto { ToEmployeeId = otherManager.Id }));
        }

        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
    }

    [Fact]
    public async Task ManagerDecisions_RejectAHandoverThatIsNoLongerPending()
    {
        var (manager, _, to, _, id) = await ArrangePendingForManagerAsync();
        var service = ServiceWith();
        await service.AcceptHandoverAsync(id, to.Id);

        var approve = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveByManagerAsync(id, manager.Id));
        var reject = await Assert.ThrowsAsync<ConflictException>(() => service.RejectByManagerAsync(id, manager.Id));
        var reassign = await Assert.ThrowsAsync<ConflictException>(() =>
            service.ReassignByManagerAsync(id, manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = Guid.NewGuid() }));

        Assert.All(new[] { approve, reject, reassign }, e => Assert.Equal(ErrorCodes.HandoverNotPending, e.ErrorCode));
    }

    [Fact]
    public async Task ManagerApprove_Conflicts_WhenTheWorkCanNoLongerBeHandedOver_ButRejectStillWorks()
    {
        var (manager, sub, _, work, id) = await ArrangePendingForManagerAsync();
        (await _db.Works.SingleAsync(w => w.Id == work.Id)).Status = WorkStatus.Cancelled;
        await _db.SaveChangesAsync();
        var service = ServiceWith();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ApproveByManagerAsync(id, manager.Id));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
        Assert.Equal(sub.Id, (await _db.Works.SingleAsync(w => w.Id == work.Id)).AssigneeEmployeeId);
        Assert.Equal("REJECTED", (await service.RejectByManagerAsync(id, manager.Id)).Status);
    }

    [Fact]
    public async Task ManagerDecisions_ThrowNotFoundOrUnauthorized()
    {
        var (manager, _, _, _, _) = await ArrangePendingForManagerAsync();
        var service = ServiceWith();

        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveByManagerAsync(Guid.NewGuid(), manager.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RejectByManagerAsync(Guid.NewGuid(), manager.Id));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ApproveByManagerAsync(Guid.NewGuid(), Guid.NewGuid()));
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
