using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>
/// Only the assignee's direct manager hands a work over, and the work's expected result (<c>Work.TargetValue</c>) moves
/// from the giver's monthly KPI target to the recipient's, in the handover month and the next one.
/// </summary>
public sealed class HandoverTransferTests : IDisposable
{
    /// <summary>27 Oct 2026, 15:00 in Vietnam: the handover month is October and the next one November.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly RecordingNotifier _notifier = new();
    private readonly WorkType _workType;
    private Employee _manager = null!;
    private Employee _giver = null!;
    private Employee _recipient = null!;

    public HandoverTransferTests()
    {
        _workType = new WorkType { Id = Guid.NewGuid(), Code = "WT", Name = "Work type", Status = RecordStatus.Active, UpdatedAt = Now };
        _db.WorkTypes.Add(_workType);
    }

    public void Dispose() => _db.Dispose();

    private async Task ArrangeEmployeesAsync()
    {
        _manager = AddEmployee("MGR");
        _giver = AddEmployee("A", _manager.Id);
        _recipient = AddEmployee("B", _manager.Id);
        await _db.SaveChangesAsync();
    }

    private Employee AddEmployee(string code, Guid? managerId = null, EmployeeStatus status = EmployeeStatus.Active)
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
        _db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "hash", LastSyncAt = Now, UpdatedAt = Now });
        return employee;
    }

    private KpiDefinition AddKpi(KpiAggregationType aggregation = KpiAggregationType.Sum, bool mapped = true)
    {
        var group = new KpiGroup { Id = Guid.NewGuid(), Code = "G", Name = "Group", Status = KpiStatus.Active, UpdatedAt = Now };
        var kpi = new KpiDefinition
        {
            Id = Guid.NewGuid(),
            KpiGroupId = group.Id,
            Code = "KPI",
            Name = "KPI",
            Unit = "unit",
            AggregationType = aggregation,
            Direction = KpiDirection.HigherBetter,
            Status = KpiStatus.Active,
            UpdatedAt = Now,
        };
        _db.KpiGroups.Add(group);
        _db.KpiDefinitions.Add(kpi);
        if (mapped)
        {
            _db.WorkTypeKpis.Add(new WorkTypeKpi
            {
                Id = Guid.NewGuid(),
                WorkTypeId = _workType.Id,
                KpiDefinitionId = kpi.Id,
                IsRequired = true,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                UpdatedAt = Now,
            });
        }

        return kpi;
    }

    private EvaluationPeriod AddMonth(int year, int month, PeriodStatus status = PeriodStatus.Open)
    {
        var start = new DateOnly(year, month, 1);
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{year}-{month:00}",
            Name = $"{year}-{month:00}",
            PeriodType = PeriodType.Month,
            StartDate = start,
            EndDate = start.AddMonths(1).AddDays(-1),
            Status = status,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        return period;
    }

    private TargetAssignment AddTarget(
        KpiDefinition kpi,
        EvaluationPeriod period,
        Employee owner,
        decimal value = 100m,
        TargetAssignmentStatus status = TargetAssignmentStatus.Active)
    {
        var target = new TargetAssignment
        {
            Id = Guid.NewGuid(),
            KpiDefinitionId = kpi.Id,
            EmployeeId = owner.Id,
            EvaluationPeriodId = period.Id,
            Version = 1,
            TargetValue = value,
            Status = status,
            CreatedAt = Now,
            AssignedAt = Now,
            ApprovedAt = Now,
            UpdatedAt = Now,
            EffectiveFrom = period.StartDate,
        };
        _db.TargetAssignments.Add(target);
        return target;
    }

    /// <summary>A KPI with a monthly target of <paramref name="value"/> for the giver and the recipient in October and November.</summary>
    private async Task<KpiDefinition> ArrangeTargetsAsync(decimal value = 100m)
    {
        var kpi = AddKpi();
        var october = AddMonth(2026, 10);
        var november = AddMonth(2026, 11);
        foreach (var month in new[] { october, november })
        {
            AddTarget(kpi, month, _giver, value);
            AddTarget(kpi, month, _recipient, value);
        }

        await _db.SaveChangesAsync();
        return kpi;
    }

    private async Task<Work> AddWorkAsync(decimal? targetValue = 30m, WorkStatus status = WorkStatus.InProgress)
    {
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = $"W-{Guid.NewGuid():N}"[..10],
            Title = "A1",
            WorkTypeId = _workType.Id,
            AssigneeEmployeeId = _giver.Id,
            TargetValue = targetValue,
            Status = status,
            Priority = WorkPriority.Medium,
            UpdatedAt = Now,
        };
        _db.Works.Add(work);
        await _db.SaveChangesAsync();
        return work;
    }

    private HandoverService ServiceAt(DateTimeOffset now, bool canSetShare = true) => new(
        _db,
        new SetSharePermission(canSetShare),
        _notifier,
        Options.Create(new SystemParametersOptions()),
        NullLogger<HandoverService>.Instance,
        new FixedTime(now));

    private Task<HandoverDetailDto> HandOverAsync(Work work, Guid callerId, bool canSetShare = true, DateTimeOffset? now = null) =>
        ServiceAt(now ?? Now, canSetShare).CreateHandoverAsync(
            work.Id,
            callerId,
            new CreateHandoverRequestDto { ToEmployeeId = _recipient.Id, Reason = "Giu tien do KPI", Amount = 1_000m });

    private Task<TargetAssignment> ActiveTargetAsync(KpiDefinition kpi, Employee owner, int month) =>
        _db.TargetAssignments
            .Include(t => t.EvaluationPeriod)
            .SingleAsync(t => t.KpiDefinitionId == kpi.Id
                && t.EmployeeId == owner.Id
                && t.Status == TargetAssignmentStatus.Active
                && t.EvaluationPeriod.StartDate.Month == month);

    // ----- Who may hand over ------------------------------------------------------------------

    [Fact]
    public async Task Create_ByTheAssignee_AsksForApproval_AndMovesNeitherTheWorkNorAnyTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync();

        var result = await HandOverAsync(work, _giver.Id, canSetShare: false);

        Assert.Equal("PENDING", result.Status);
        Assert.Equal(_giver.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_ByTheAssignee_TellsTheirManager_AndTheRecipientOnlyAfterTheDecision()
    {
        await ArrangeEmployeesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _giver.Id, canSetShare: false);

        Assert.Equal(_manager.Id, Assert.Single(_notifier.Drafts, d => d.Type == NotificationType.HandoverRequested && d.RecipientEmployeeId == _manager.Id).RecipientEmployeeId);
        Assert.DoesNotContain(_notifier.Drafts, d => d.RecipientEmployeeId == _recipient.Id);
    }

    [Fact]
    public async Task Create_ByAnEmployeeWhoIsNotTheAssigneeOrTheirManager_IsForbidden()
    {
        await ArrangeEmployeesAsync();
        var stranger = AddEmployee("OTHER");
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => HandOverAsync(work, stranger.Id, canSetShare: false));
        await Assert.ThrowsAsync<ForbiddenException>(() => HandOverAsync(work, stranger.Id, canSetShare: true));

        Assert.Empty(await _db.TaskHandovers.ToListAsync());
    }

    [Fact]
    public async Task Create_ByTheDirectManagerWithoutTheManagerPermission_IsForbidden()
    {
        await ArrangeEmployeesAsync();
        var work = await AddWorkAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => HandOverAsync(work, _manager.Id, canSetShare: false));

        Assert.Empty(await _db.TaskHandovers.ToListAsync());
    }

    [Fact]
    public async Task ManagerApprove_MovesTheWorkAndTheExpectedResultBetweenTheTargets()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(30m);
        var pending = await HandOverAsync(work, _giver.Id, canSetShare: false);

        var result = await ServiceAt(Now).ApproveByManagerAsync(pending.Id, _manager.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(_recipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        foreach (var month in new[] { 10, 11 })
        {
            Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, month)).TargetValue);
            Assert.Equal(130m, (await ActiveTargetAsync(kpi, _recipient, month)).TargetValue);
        }

        Assert.Equal(_manager.Id, (await _db.TaskHandovers.SingleAsync()).ApprovedBy);
    }

    [Fact]
    public async Task ManagerReassign_MovesTheExpectedResultToTheNewRecipient_NotTheRequestedOne()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var newRecipient = AddEmployee("NEW", _manager.Id);
        var october = await _db.EvaluationPeriods.SingleAsync(p => p.StartDate.Month == 10);
        AddTarget(kpi, october, newRecipient, 50m);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);
        var pending = await HandOverAsync(work, _giver.Id, canSetShare: false);

        await ServiceAt(Now).ReassignByManagerAsync(pending.Id, _manager.Id, new ReassignHandoverRequestDto { ToEmployeeId = newRecipient.Id });

        Assert.Equal(newRecipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(80m, (await ActiveTargetAsync(kpi, newRecipient, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task ManagerReject_LeavesTheWorkAndEveryTargetAlone()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(30m);
        var pending = await HandOverAsync(work, _giver.Id, canSetShare: false);

        var result = await ServiceAt(Now).RejectByManagerAsync(pending.Id, _manager.Id);

        Assert.Equal("REJECTED", result.Status);
        Assert.Equal(_giver.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(4, await _db.TargetAssignments.CountAsync());
    }

    [Fact]
    public async Task ManagerApprove_WhenTheWorkMeanwhileCannotBeHandedOver_Conflicts_AndMovesNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(30m);
        var pending = await HandOverAsync(work, _giver.Id, canSetShare: false);
        (await _db.Works.SingleAsync()).Status = WorkStatus.Cancelled;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(Now).ApproveByManagerAsync(pending.Id, _manager.Id));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
    }

    [Fact]
    public async Task ManagerApprove_ByAnyoneButTheRequestersDirectManager_IsForbidden()
    {
        await ArrangeEmployeesAsync();
        var work = await AddWorkAsync();
        var pending = await HandOverAsync(work, _giver.Id, canSetShare: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(Now).ApproveByManagerAsync(pending.Id, _giver.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(Now).ApproveByManagerAsync(pending.Id, _recipient.Id));

        Assert.Equal(HandoverStatus.Pending, (await _db.TaskHandovers.SingleAsync()).Status);
    }
    [Fact]
    public async Task Create_ByTheDirectManager_MovesTheWorkAtOnceWithNothingLeftToAccept()
    {
        await ArrangeEmployeesAsync();
        var work = await AddWorkAsync();

        var result = await HandOverAsync(work, _manager.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(_giver.Id, result.FromEmployeeId);
        Assert.Equal(_recipient.Id, result.ToEmployeeId);
        var stored = await _db.Works.SingleAsync();
        Assert.Equal(_recipient.Id, stored.AssigneeEmployeeId);
        Assert.Equal(WorkStatus.HandedOver, stored.Status);
        var handover = await _db.TaskHandovers.SingleAsync();
        Assert.Equal(HandoverStatus.Accepted, handover.Status);
        Assert.Equal(_manager.Id, handover.ApprovedBy);
        Assert.DoesNotContain(await _db.TaskHandovers.ToListAsync(), h => h.Status == HandoverStatus.Pending);
    }

    // ----- Expected result moves between the two employees' targets ---------------------------

    [Fact]
    public async Task Create_MovesTheExpectedResultFromTheGiverToTheRecipient_InThisMonthAndTheNext()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        foreach (var month in new[] { 10, 11 })
        {
            Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, month)).TargetValue);
            Assert.Equal(130m, (await ActiveTargetAsync(kpi, _recipient, month)).TargetValue);
        }
    }

    [Fact]
    public async Task Create_KeepsTheOldVersionOfThisMonthsTargetUntilYesterday_ButChangesNextMonthInPlace()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        var octoberVersions = await _db.TargetAssignments
            .Where(t => t.KpiDefinitionId == kpi.Id && t.EmployeeId == _giver.Id && t.EvaluationPeriod.StartDate.Month == 10)
            .OrderBy(t => t.Version)
            .ToListAsync();
        Assert.Equal(2, octoberVersions.Count);
        Assert.Equal(TargetAssignmentStatus.Superseded, octoberVersions[0].Status);
        Assert.Equal(100m, octoberVersions[0].TargetValue);
        Assert.Equal(new DateOnly(2026, 10, 26), octoberVersions[0].EffectiveTo);
        Assert.Equal(new DateOnly(2026, 10, 27), octoberVersions[1].EffectiveFrom);
        Assert.Equal(octoberVersions[0].Id, octoberVersions[1].PreviousVersionId);
        Assert.Equal(_manager.Id, octoberVersions[1].ChangedByEmployeeId);
        Assert.Contains("Bàn giao", octoberVersions[1].ChangeReason);

        var novemberVersions = await _db.TargetAssignments
            .Where(t => t.KpiDefinitionId == kpi.Id && t.EmployeeId == _giver.Id && t.EvaluationPeriod.StartDate.Month == 11)
            .ToListAsync();
        Assert.Single(novemberVersions);
        Assert.Equal(1, novemberVersions[0].Version);
    }

    [Fact]
    public async Task Create_NeverTakesMoreThanTheGiversTargetHas()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync(20m);
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(0m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(40m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_MovesEverySumKpiOfTheWorkType()
    {
        await ArrangeEmployeesAsync();
        var first = await ArrangeTargetsAsync();
        var second = AddKpi();
        var october = await _db.EvaluationPeriods.SingleAsync(p => p.StartDate.Month == 10);
        AddTarget(second, october, _giver, 50m);
        AddTarget(second, october, _recipient, 50m);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(10m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(90m, (await ActiveTargetAsync(first, _giver, 10)).TargetValue);
        Assert.Equal(40m, (await ActiveTargetAsync(second, _giver, 10)).TargetValue);
        Assert.Equal(60m, (await ActiveTargetAsync(second, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_AtTheEndOfTheYear_MovesTheNextMonthOfTheNextYear()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi();
        foreach (var (year, month) in new[] { (2026, 12), (2027, 1) })
        {
            var period = AddMonth(year, month);
            AddTarget(kpi, period, _giver);
            AddTarget(kpi, period, _recipient);
        }

        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id, now: new DateTimeOffset(2026, 12, 20, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, 12)).TargetValue);
        Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, 1)).TargetValue);
        Assert.Equal(130m, (await ActiveTargetAsync(kpi, _recipient, 1)).TargetValue);
    }

    [Fact]
    public async Task Create_LeavesOtherEmployeesAndDraftTargetsAlone()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var bystander = AddEmployee("C", _manager.Id);
        var october = await _db.EvaluationPeriods.SingleAsync(p => p.StartDate.Month == 10);
        var bystanderTarget = AddTarget(kpi, october, bystander);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await _db.TargetAssignments.SingleAsync(t => t.Id == bystanderTarget.Id)).TargetValue);
    }

    // ----- Nothing to move: the handover still goes through -----------------------------------

    [Fact]
    public async Task Create_WorkWithoutAnExpectedResult_MovesTheWorkButNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(targetValue: null);

        var result = await HandOverAsync(work, _manager.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(_recipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 11)).TargetValue);
        Assert.Equal(4, await _db.TargetAssignments.CountAsync());
    }

    [Fact]
    public async Task Create_WorkWithAZeroExpectedResult_MovesNoTarget()
    {
        await ArrangeEmployeesAsync();
        await ArrangeTargetsAsync();
        var work = await AddWorkAsync(0m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(4, await _db.TargetAssignments.CountAsync());
    }

    [Fact]
    public async Task Create_WorkTypeWithoutKpi_MovesTheWorkButNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi(mapped: false);
        var october = AddMonth(2026, 10);
        AddTarget(kpi, october, _giver);
        AddTarget(kpi, october, _recipient);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(_recipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_WorkWithoutAWorkType_MovesTheWorkButNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync();
        work.WorkTypeId = null;
        await _db.SaveChangesAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(_recipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_NonSumKpi_IsNotMoved()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi(KpiAggregationType.Ratio);
        var october = AddMonth(2026, 10);
        AddTarget(kpi, october, _giver);
        AddTarget(kpi, october, _recipient);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_RecipientWithoutATargetInAMonth_LeavesThatMonthAloneAndMovesTheOther()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi();
        var october = AddMonth(2026, 10);
        var november = AddMonth(2026, 11);
        AddTarget(kpi, october, _giver);
        AddTarget(kpi, november, _giver);
        AddTarget(kpi, november, _recipient);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, 11)).TargetValue);
        Assert.Equal(130m, (await ActiveTargetAsync(kpi, _recipient, 11)).TargetValue);
    }

    [Fact]
    public async Task Create_GiverWithoutATarget_LeavesTheRecipientsTargetAlone()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi();
        var october = AddMonth(2026, 10);
        AddTarget(kpi, october, _recipient);
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    [Fact]
    public async Task Create_LockedMonth_IsLeftAloneWhileTheOpenMonthMoves()
    {
        await ArrangeEmployeesAsync();
        var kpi = AddKpi();
        var october = AddMonth(2026, 10, PeriodStatus.Locked);
        var november = AddMonth(2026, 11);
        foreach (var month in new[] { october, november })
        {
            AddTarget(kpi, month, _giver);
            AddTarget(kpi, month, _recipient);
        }

        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(70m, (await ActiveTargetAsync(kpi, _giver, 11)).TargetValue);
        Assert.Equal(_recipient.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Create_TargetsUnderDifferentParents_AreLeftAlone()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var october = await _db.EvaluationPeriods.SingleAsync(p => p.StartDate.Month == 10);
        var giverTarget = await ActiveTargetAsync(kpi, _giver, 10);
        giverTarget.ParentAssignmentId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
        Assert.Equal(october.Id, (await ActiveTargetAsync(kpi, _recipient, 10)).EvaluationPeriodId);
    }

    [Fact]
    public async Task Create_TargetsWithDifferentSplitDimensions_AreLeftAlone()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        (await ActiveTargetAsync(kpi, _giver, 10)).SplitDimension = TargetSplitDimension.Period;
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _recipient, 10)).TargetValue);
    }

    // ----- Quarter above the months (what the development seed holds) -------------------------

    private readonly Guid _departmentQuarterId = Guid.NewGuid();

    private EvaluationPeriod AddQuarter(int year, int firstMonth, PeriodStatus status = PeriodStatus.Open)
    {
        var start = new DateOnly(year, firstMonth, 1);
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"{year}-Q{(firstMonth - 1) / 3 + 1}",
            Name = $"{year}-Q{(firstMonth - 1) / 3 + 1}",
            PeriodType = PeriodType.Quarter,
            StartDate = start,
            EndDate = start.AddMonths(3).AddDays(-1),
            Status = status,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        return period;
    }

    /// <summary>
    /// Each employee holds a quarter target of 300 (a share of the department's quarter) split into the monthly targets
    /// of October and November. Returns the quarter targets of the giver and the recipient.
    /// </summary>
    private async Task<(KpiDefinition Kpi, TargetAssignment GiverQuarter, TargetAssignment RecipientQuarter)> ArrangeQuarterTargetsAsync(
        decimal monthly = 100m)
    {
        var kpi = AddKpi();
        var quarter = AddQuarter(2026, 10);
        var months = new[] { AddMonth(2026, 10), AddMonth(2026, 11) };
        var quarters = new List<TargetAssignment>();
        foreach (var employee in new[] { _giver, _recipient })
        {
            var employeeQuarter = AddTarget(kpi, quarter, employee, 300m);
            employeeQuarter.ParentAssignmentId = _departmentQuarterId;
            employeeQuarter.SplitDimension = TargetSplitDimension.Org;
            quarters.Add(employeeQuarter);
            foreach (var month in months)
            {
                var monthTarget = AddTarget(kpi, month, employee, monthly);
                monthTarget.ParentAssignmentId = employeeQuarter.Id;
                monthTarget.SplitDimension = TargetSplitDimension.Period;
            }
        }

        await _db.SaveChangesAsync();
        return (kpi, quarters[0], quarters[1]);
    }

    private async Task<decimal> ValueAsync(KpiDefinition kpi, Employee owner, PeriodType type, int? startMonth = null) =>
        (await _db.TargetAssignments
            .Include(t => t.EvaluationPeriod)
            .SingleAsync(t => t.KpiDefinitionId == kpi.Id
                && t.EmployeeId == owner.Id
                && t.Status == TargetAssignmentStatus.Active
                && t.EvaluationPeriod.PeriodType == type
                && (startMonth == null || t.EvaluationPeriod.StartDate.Month == startMonth))).TargetValue;

    [Fact]
    public async Task Create_MovesTheQuarterAsWellAsTheMonths_SoTheMonthsStillAddUpToIt()
    {
        await ArrangeEmployeesAsync();
        var (kpi, _, _) = await ArrangeQuarterTargetsAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(70m, await ValueAsync(kpi, _giver, PeriodType.Month, 10));
        Assert.Equal(70m, await ValueAsync(kpi, _giver, PeriodType.Month, 11));
        Assert.Equal(240m, await ValueAsync(kpi, _giver, PeriodType.Quarter));
        Assert.Equal(130m, await ValueAsync(kpi, _recipient, PeriodType.Month, 10));
        Assert.Equal(130m, await ValueAsync(kpi, _recipient, PeriodType.Month, 11));
        Assert.Equal(360m, await ValueAsync(kpi, _recipient, PeriodType.Quarter));
    }

    [Fact]
    public async Task Create_KeepsWhatTheGiverAndTheRecipientTogetherHold()
    {
        await ArrangeEmployeesAsync();
        var (kpi, _, _) = await ArrangeQuarterTargetsAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        var quarters = await ValueAsync(kpi, _giver, PeriodType.Quarter) + await ValueAsync(kpi, _recipient, PeriodType.Quarter);
        Assert.Equal(600m, quarters);
        Assert.Equal(
            200m,
            await ValueAsync(kpi, _giver, PeriodType.Month, 10) + await ValueAsync(kpi, _recipient, PeriodType.Month, 10));
    }

    [Fact]
    public async Task Create_ChangesTheQuarterInPlace_ButFreezesThisMonthsOldVersion()
    {
        await ArrangeEmployeesAsync();
        var (kpi, giverQuarter, _) = await ArrangeQuarterTargetsAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        var quarters = await _db.TargetAssignments
            .Where(t => t.EmployeeId == _giver.Id && t.EvaluationPeriod.PeriodType == PeriodType.Quarter)
            .ToListAsync();
        Assert.Equal(giverQuarter.Id, Assert.Single(quarters).Id);
        Assert.Equal(1, quarters[0].Version);
        Assert.Contains("Bàn giao", quarters[0].ChangeReason);
        Assert.Equal(_manager.Id, quarters[0].ChangedByEmployeeId);
        var octoberVersions = await _db.TargetAssignments
            .Where(t => t.EmployeeId == _giver.Id && t.KpiDefinitionId == kpi.Id && t.EvaluationPeriod.PeriodType == PeriodType.Month
                && t.EvaluationPeriod.StartDate.Month == 10)
            .ToListAsync();
        Assert.Equal(2, octoberVersions.Count);
    }

    [Fact]
    public async Task Create_QuartersUnderDifferentDepartmentTargets_AreLeftAlone()
    {
        await ArrangeEmployeesAsync();
        var (kpi, _, recipientQuarter) = await ArrangeQuarterTargetsAsync();
        recipientQuarter.ParentAssignmentId = Guid.NewGuid();
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        var result = await HandOverAsync(work, _manager.Id);

        Assert.Equal("ACCEPTED", result.Status);
        Assert.Equal(100m, await ValueAsync(kpi, _giver, PeriodType.Month, 10));
        Assert.Equal(300m, await ValueAsync(kpi, _giver, PeriodType.Quarter));
        Assert.Equal(300m, await ValueAsync(kpi, _recipient, PeriodType.Quarter));
    }

    [Fact]
    public async Task Create_WhenOnlyOneSideHasAQuarter_LeavesEveryTargetAlone()
    {
        await ArrangeEmployeesAsync();
        var (kpi, _, recipientQuarter) = await ArrangeQuarterTargetsAsync();
        foreach (var month in await _db.TargetAssignments.Where(t => t.ParentAssignmentId == recipientQuarter.Id).ToListAsync())
        {
            month.ParentAssignmentId = null;
            month.SplitDimension = null;
        }

        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, await ValueAsync(kpi, _giver, PeriodType.Month, 10));
        Assert.Equal(300m, await ValueAsync(kpi, _giver, PeriodType.Quarter));
        Assert.Equal(100m, await ValueAsync(kpi, _recipient, PeriodType.Month, 10));
    }

    [Fact]
    public async Task Create_QuarterThatIsNotActive_LeavesTheMonthsAlone()
    {
        await ArrangeEmployeesAsync();
        var (kpi, giverQuarter, _) = await ArrangeQuarterTargetsAsync();
        giverQuarter.Status = TargetAssignmentStatus.Superseded;
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.Equal(100m, await ValueAsync(kpi, _giver, PeriodType.Month, 10));
        Assert.Equal(100m, await ValueAsync(kpi, _recipient, PeriodType.Month, 10));
        Assert.Equal(300m, await ValueAsync(kpi, _recipient, PeriodType.Quarter));
    }

    [Fact]
    public async Task Create_NeverTakesMoreThanTheSmallestTargetInTheGiversChain()
    {
        await ArrangeEmployeesAsync();
        var (kpi, giverQuarter, _) = await ArrangeQuarterTargetsAsync();
        giverQuarter.TargetValue = 20m;
        await _db.SaveChangesAsync();
        var work = await AddWorkAsync(30m);

        await HandOverAsync(work, _manager.Id);

        Assert.True(await ValueAsync(kpi, _giver, PeriodType.Quarter) >= 0m);
        Assert.Equal(80m, await ValueAsync(kpi, _giver, PeriodType.Month, 10));
        Assert.Equal(120m, await ValueAsync(kpi, _recipient, PeriodType.Month, 10));
    }
    // ----- Rules that stay -------------------------------------------------------------------

    [Fact]
    public async Task Create_WorkThatCannotBeHandedOver_Conflicts_AndMovesNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync(status: WorkStatus.Done);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => HandOverAsync(work, _manager.Id));

        Assert.Equal(ErrorCodes.WorkInvalidState, ex.ErrorCode);
        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(4, await _db.TargetAssignments.CountAsync());
    }

    [Fact]
    public async Task Create_ToAnInactiveRecipient_IsRejected_AndMovesNoTarget()
    {
        await ArrangeEmployeesAsync();
        var kpi = await ArrangeTargetsAsync();
        var work = await AddWorkAsync();
        _recipient.Status = EmployeeStatus.Inactive;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => HandOverAsync(work, _manager.Id));

        Assert.Equal(100m, (await ActiveTargetAsync(kpi, _giver, 10)).TargetValue);
        Assert.Equal(_giver.Id, (await _db.Works.SingleAsync()).AssigneeEmployeeId);
    }

    [Fact]
    public async Task Create_TellsBothEmployeesButNotTheManager()
    {
        await ArrangeEmployeesAsync();
        var work = await AddWorkAsync();

        await HandOverAsync(work, _manager.Id);

        Assert.Equivalent(new[] { _giver.Id, _recipient.Id }, _notifier.Drafts.Select(d => d.RecipientEmployeeId).ToArray());
        Assert.DoesNotContain(_notifier.Drafts, d => d.RecipientEmployeeId == _manager.Id);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
