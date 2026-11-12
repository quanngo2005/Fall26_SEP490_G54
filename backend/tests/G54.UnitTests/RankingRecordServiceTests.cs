using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// HR ranking records (GB-650): only FINAL evaluations of a LOCKED period, ranked per department and job position,
/// with the calibrated tier when one was saved. Branch -> Sales (Team 1) and Support, two job positions.
/// </summary>
public sealed class RankingRecordServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private Department _branch = null!;
    private Department _sales = null!;
    private Department _team1 = null!;
    private Department _support = null!;
    private JobPosition _teller = null!;
    private JobPosition _officer = null!;
    private EvaluationPeriod _period = null!;

    public RankingRecordServiceTests()
    {
        DowngradeTestData.Seed(_db);
        _branch = AddDepartment("BR", DepartmentLevelType.Branch, null);
        _sales = AddDepartment("SALES", DepartmentLevelType.Department, _branch.Id);
        _team1 = AddDepartment("T1", DepartmentLevelType.Team, _sales.Id);
        _support = AddDepartment("SUP", DepartmentLevelType.Department, _branch.Id);
        _teller = AddPosition("TELLER", "Teller");
        _officer = AddPosition("OFFICER", "Officer");
        _period = AddPeriod("Q3", PeriodStatus.Locked);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private RankingRecordService Service => new(_db, NullLogger<RankingRecordService>.Instance);

    private Department AddDepartment(string code, DepartmentLevelType level, Guid? parentId)
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Unit {code}",
            LevelType = level,
            ParentDepartmentId = parentId,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        _db.Departments.Add(department);
        return department;
    }

    private JobPosition AddPosition(string code, string title)
    {
        var position = new JobPosition { Id = Guid.NewGuid(), Code = code, Title = title, Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _db.JobPositions.Add(position);
        return position;
    }

    private EvaluationPeriod AddPeriod(string code, PeriodStatus status)
    {
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = $"Period {code}",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 9, 30),
            PeriodType = PeriodType.Quarter,
            Status = status,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        return period;
    }

    private (Employee Employee, PerformanceEvaluation Evaluation) AddRecord(
        string code,
        Department department,
        JobPosition position,
        decimal? score,
        PerformanceEvaluationStatus status = PerformanceEvaluationStatus.Final,
        EvaluationPeriod? period = null,
        string? comment = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = department.Id,
            JobPositionId = position.Id,
            Status = EmployeeStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var profile = _db.EvaluationProfiles.Local.FirstOrDefault(p => p.JobPositionId == position.Id) ?? new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = position.Id,
            Code = $"EP-{position.Code}",
            Name = "Profile",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Active,
            UpdatedAt = Now,
        };
        if (_db.EvaluationProfiles.Local.All(p => p.Id != profile.Id))
        {
            _db.EvaluationProfiles.Add(profile);
        }

        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = (period ?? _period).Id,
            EvaluationProfileId = profile.Id,
            TotalScore = score,
            Status = status,
            ApprovedAt = Now,
            Comment = comment,
            UpdatedAt = Now,
        };
        _db.Employees.Add(employee);
        _db.PerformanceEvaluations.Add(evaluation);
        _db.SaveChanges();
        return (employee, evaluation);
    }

    private static string CalibrationMarker(string tier, string? reason) =>
        EvaluationService.BuildStoredComment(null, null, null, new EvaluationService.InternalCalibrationData(tier, reason, false))!;

    [Fact]
    public async Task GetRecords_RanksEachDepartmentAndPositionGroupSeparately()
    {
        AddRecord("S1", _sales, _teller, 92m);
        AddRecord("S2", _sales, _teller, 96m);
        AddRecord("S3", _sales, _officer, 80m);
        AddRecord("U1", _support, _teller, 70m);

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        Assert.Equal(4, result.TotalCount);
        var byCode = result.Items.ToDictionary(r => r.EmployeeCode);
        Assert.Equal((1, 2), (byCode["S2"].Rank, byCode["S2"].GroupSize));
        Assert.Equal((2, 2), (byCode["S1"].Rank, byCode["S1"].GroupSize));
        Assert.Equal((1, 1), (byCode["S3"].Rank, byCode["S3"].GroupSize));
        Assert.Equal((1, 1), (byCode["U1"].Rank, byCode["U1"].GroupSize));
    }

    [Fact]
    public async Task GetRecords_BreaksAScoreTieByEmployeeCode()
    {
        AddRecord("B2", _sales, _teller, 90m);
        AddRecord("B1", _sales, _teller, 90m);

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        Assert.Equal(["B1", "B2"], result.Items.Select(r => r.EmployeeCode));
        Assert.Equal([1, 2], result.Items.Select(r => r.Rank));
    }

    [Fact]
    public async Task GetRecords_ListsOnlyFinalScoredEvaluationsOfThePeriod()
    {
        AddRecord("OK", _sales, _teller, 90m);
        AddRecord("INREVIEW", _sales, _teller, 91m, PerformanceEvaluationStatus.InReview);
        AddRecord("APPROVED", _sales, _teller, 92m, PerformanceEvaluationStatus.Approved);
        AddRecord("NOSCORE", _sales, _teller, null);
        AddRecord("OTHER", _sales, _teller, 99m, period: AddPeriodSaved("Q2"));

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        Assert.Equal(["OK"], result.Items.Select(r => r.EmployeeCode));
        Assert.Equal(1, result.Items[0].GroupSize);
    }

    private EvaluationPeriod AddPeriodSaved(string code)
    {
        var period = AddPeriod(code, PeriodStatus.Locked);
        _db.SaveChanges();
        return period;
    }

    [Fact]
    public async Task GetRecords_UsesTheCalibratedTierAndFlagsTheAdjustment()
    {
        AddRecord("TOP", _sales, _teller, 96m);
        AddRecord("ADJ", _sales, _teller, 96m, comment: CalibrationMarker("B", "Điều chỉnh"));

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        var top = result.Items.Single(r => r.EmployeeCode == "TOP");
        Assert.Equal(("A1", "A1", false), (top.ProposedTier, top.Tier, top.IsAdjusted));
        var adjusted = result.Items.Single(r => r.EmployeeCode == "ADJ");
        Assert.Equal(("A1", "B", true), (adjusted.ProposedTier, adjusted.Tier, adjusted.IsAdjusted));
    }

    [Fact]
    public async Task GetRecords_CapsTheProposedTierOfAnEmployeeWithACriticalRiskEvent()
    {
        var (employee, _) = AddRecord("RISKY", _sales, _teller, 97m);
        _db.RiskEvents.Add(new RiskEvent
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            RecordedBy = employee.Id,
            Code = "RE-1",
            EventType = "FRAUD",
            Description = "Severe violation",
            Severity = RiskEventSeverity.Critical,
            Status = RiskEventStatus.Confirmed,
            OccurredAt = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            RecordedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        Assert.Equal("B", result.Items.Single().ProposedTier);
    }

    [Fact]
    public async Task GetRecords_FiltersByDepartmentSubtreeButKeepsTheGroupRank()
    {
        AddRecord("S1", _sales, _teller, 80m);
        AddRecord("T1A", _team1, _teller, 95m);
        AddRecord("U1", _support, _teller, 99m);

        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id, DepartmentId = _sales.Id });

        Assert.Equal(["S1", "T1A"], result.Items.Select(r => r.EmployeeCode).Order());
        Assert.All(result.Items, r => Assert.Equal(1, r.Rank));
    }

    [Fact]
    public async Task GetRecords_FiltersByPositionTierAndKeyword()
    {
        AddRecord("AAA", _sales, _teller, 96m);
        AddRecord("BBB", _sales, _teller, 75m);
        AddRecord("CCC", _sales, _officer, 96m);

        Assert.Equal(["AAA", "BBB"], (await Service.GetRecordsAsync(
            new RankingRecordFilterDto { PeriodId = _period.Id, JobPositionId = _teller.Id })).Items.Select(r => r.EmployeeCode));
        Assert.Equal(["CCC", "AAA"], (await Service.GetRecordsAsync(
            new RankingRecordFilterDto { PeriodId = _period.Id, Tier = "a1" })).Items.Select(r => r.EmployeeCode));
        Assert.Equal(["BBB"], (await Service.GetRecordsAsync(
            new RankingRecordFilterDto { PeriodId = _period.Id, Keyword = " name bb " })).Items.Select(r => r.EmployeeCode));
    }

    [Fact]
    public async Task GetRecords_PagesTheResultAndReturnsEmptyBeyondTheLastPage()
    {
        for (var i = 1; i <= 5; i++)
        {
            AddRecord($"E{i}", _sales, _teller, 90m - i);
        }

        var second = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id, Page = 2, PageSize = 2 });
        var beyond = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id, Page = int.MaxValue, PageSize = 100 });

        Assert.Equal(["E3", "E4"], second.Items.Select(r => r.EmployeeCode));
        Assert.Equal((5, 2, 2), (second.TotalCount, second.Page, second.PageSize));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Fact]
    public async Task GetRecords_ReturnsAnEmptyListForALockedPeriodWithoutRecords()
    {
        var result = await Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetRecords_RejectsAMissingPeriodAnUnknownTierAndAnUnknownDepartment()
    {
        var missing = await Assert.ThrowsAsync<ValidationException>(() => Service.GetRecordsAsync(new RankingRecordFilterDto()));
        var tier = await Assert.ThrowsAsync<ValidationException>(() =>
            Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id, Tier = "Z9" }));
        var department = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = _period.Id, DepartmentId = Guid.NewGuid() }));
        var period = await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = Guid.NewGuid() }));

        Assert.Equal(ErrorCodes.ValidationError, missing.ErrorCode);
        Assert.Equal(ErrorCodes.ValidationError, tier.ErrorCode);
        Assert.Equal(ErrorCodes.NotFound, department.ErrorCode);
        Assert.Equal(ErrorCodes.NotFound, period.ErrorCode);
    }

    [Theory]
    [InlineData(PeriodStatus.Open)]
    [InlineData(PeriodStatus.InReview)]
    [InlineData(PeriodStatus.Closed)]
    public async Task GetRecords_RefusesAPeriodThatIsNotLocked(PeriodStatus status)
    {
        var period = AddPeriod("NOTLOCKED", status);
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            Service.GetRecordsAsync(new RankingRecordFilterDto { PeriodId = period.Id }));

        Assert.Equal(ErrorCodes.EvaluationPeriodInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task GetRecord_ReturnsTheDetailWithTheReviewInformation()
    {
        var acknowledgedAt = new DateTimeOffset(2026, 10, 12, 2, 15, 0, TimeSpan.Zero);
        var comment = EvaluationService.BuildStoredComment(
            "Nhận xét", "Học thêm kỹ năng tư vấn", null,
            new EvaluationService.InternalCalibrationData("B", "Điều chỉnh do tác phong", false),
            approvalComments: "Phê duyệt");
        comment += $"\n<!-- ACKNOWLEDGED_AT: {acknowledgedAt:O} -->";
        var (_, evaluation) = AddRecord("DET", _sales, _teller, 96m, comment: comment);

        var detail = await Service.GetRecordAsync(evaluation.Id);

        Assert.Equal(evaluation.Id, detail.Record.EvaluationId);
        Assert.Equal(("A1", "B", true), (detail.Record.ProposedTier, detail.Record.Tier, detail.Record.IsAdjusted));
        Assert.Equal((_period.Id, "Q3", "Period Q3"), (detail.PeriodId, detail.PeriodCode, detail.PeriodName));
        Assert.Equal("Điều chỉnh do tác phong", detail.AdjustmentReason);
        Assert.Equal("Học thêm kỹ năng tư vấn", detail.CoachingPlan);
        Assert.Equal("Phê duyệt", detail.ApprovalComments);
        Assert.Equal(acknowledgedAt, detail.AcknowledgedAt);
    }

    [Fact]
    public async Task GetRecord_ReturnsNullReviewFieldsWhenNothingWasSaved()
    {
        var (_, evaluation) = AddRecord("PLAIN", _sales, _teller, 80m);

        var detail = await Service.GetRecordAsync(evaluation.Id);

        Assert.Null(detail.AdjustmentReason);
        Assert.Null(detail.CoachingPlan);
        Assert.Null(detail.ApprovalComments);
        Assert.Null(detail.AcknowledgedAt);
    }

    [Fact]
    public async Task GetRecord_IsNotFoundForAnUnknownIdANonFinalRecordOrAnUnlockedPeriod()
    {
        var (_, inReview) = AddRecord("REV", _sales, _teller, 80m, PerformanceEvaluationStatus.InReview);
        var open = AddPeriod("OPEN", PeriodStatus.Open);
        _db.SaveChanges();
        var (_, inOpenPeriod) = AddRecord("OPENREC", _sales, _teller, 80m, period: open);

        foreach (var id in new[] { Guid.NewGuid(), inReview.Id, inOpenPeriod.Id })
        {
            var ex = await Assert.ThrowsAsync<NotFoundException>(() => Service.GetRecordAsync(id));
            Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
        }
    }
}
