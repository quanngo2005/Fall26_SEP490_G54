using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Appeal;
using G54.BLL.Dtos.Permission;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace G54.UnitTests;

/// <summary>Appeals against an evaluation score (UC114-117) and the list filters the appeal screens use.</summary>
public sealed class ScoreAppealServiceTests : IDisposable
{
    private static readonly DateTimeOffset CalculatedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PdfBytes = "%PDF-1.7 appeal"u8.ToArray();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "G54_ScoreAppeal_" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _salesId = Guid.NewGuid();
    private readonly Guid _tellerId = Guid.NewGuid();

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private StorageService Storage => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:EvidencePath"] = _folder })
            .Build(),
        NullLogger<StorageService>.Instance);

    private AppealService ServiceAt(DateTimeOffset now, params (string Resource, string Action, string Scope)[] grants) =>
        new(_db, new GrantingPermissions(grants), Storage, NullLogger<AppealService>.Instance, new FixedTime(now),
            Options.Create(new SystemParametersOptions()));

    private async Task SeedDepartmentsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Departments.AddRange(
            new Department { Id = _branchId, Code = "BR", Name = "Branch", LevelType = DepartmentLevelType.Branch, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _salesId, Code = "SA", Name = "Sales", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now },
            new Department { Id = _tellerId, Code = "TE", Name = "Teller", ParentDepartmentId = _branchId, LevelType = DepartmentLevelType.Department, Status = RecordStatus.Active, UpdatedAt = now });
        await _db.SaveChangesAsync();
    }

    private async Task<Employee> AddEmployeeAsync(string code, Guid departmentId, Guid? jobPositionId = null)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = departmentId,
            JobPositionId = jobPositionId ?? Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            Username = code,
            PasswordHash = "hash",
            LastSyncAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        return employee;
    }

    private async Task<PerformanceEvaluation> AddScoreAsync(
        Employee employee,
        PerformanceEvaluationStatus status = PerformanceEvaluationStatus.Calculated,
        PeriodStatus periodStatus = PeriodStatus.Open,
        DateTimeOffset? calculatedAt = null,
        decimal? totalScore = 88m,
        EvaluationPeriod? period = null)
    {
        period ??= new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"P-{Guid.NewGuid():N}"[..10],
            Name = "Period",
            PeriodType = PeriodType.Month,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 31),
            Status = periodStatus,
            UpdatedAt = CalculatedAt,
        };
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriod = period,
            EvaluationProfileId = Guid.NewGuid(),
            TotalScore = totalScore,
            CalculatedAt = calculatedAt ?? CalculatedAt,
            Status = status,
            UpdatedAt = CalculatedAt,
        };
        _db.PerformanceEvaluations.Add(evaluation);
        await _db.SaveChangesAsync();
        return evaluation;
    }

    private static CreateAppealRequestDto Request(string? reason = "Điểm chưa phản ánh đủ hồ sơ", params IFormFile[] files) =>
        new() { Reason = reason, Files = files.ToList() };

    private static FormFile Pdf(string name = "bang chung.pdf") =>
        new(new MemoryStream(PdfBytes), 0, PdfBytes.Length, "Files", name);

    private async Task<(Employee Employee, PerformanceEvaluation Score)> EmployeeWithScoreAsync(
        PerformanceEvaluationStatus status = PerformanceEvaluationStatus.Calculated)
    {
        await SeedDepartmentsAsync();
        var employee = await AddEmployeeAsync("EMP", _salesId);
        return (employee, await AddScoreAsync(employee, status));
    }

    // The window of a score appeal is fixed in the service; the tests read it from the same place.
    private static TimeSpan Window => (TimeSpan)typeof(AppealService)
        .GetField("ScoreAppealWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
        .GetValue(null)!;

    // ----- Create ----------------------------------------------------------------------------

    [Theory]
    [InlineData(PerformanceEvaluationStatus.Calculated)]
    [InlineData(PerformanceEvaluationStatus.InReview)]
    public async Task Create_FreezesTheScoreAsDisputed_RecordsHistoryAndKeepsTheScore(PerformanceEvaluationStatus before)
    {
        var (employee, score) = await EmployeeWithScoreAsync(before);
        var now = CalculatedAt.AddHours(1);

        var created = await ServiceAt(now).CreateScoreAppealAsync(score.Id, employee.Id, Request(files: Pdf()));

        Assert.Equal("SCORE", created.Type);
        Assert.Equal("OPEN", created.Status);
        Assert.Equal(score.Id, created.PerformanceEvaluationId);
        Assert.Null(created.ResultSubmissionId);
        Assert.Null(created.WorkCode);
        Assert.Single(created.Evidences);
        Assert.Equal(score.EvaluationPeriod.Code, created.PeriodCode);

        var stored = await _db.PerformanceEvaluations.SingleAsync(e => e.Id == score.Id);
        Assert.Equal(PerformanceEvaluationStatus.Disputed, stored.Status);
        Assert.Equal(before, stored.PreDisputeStatus);
        Assert.Equal(now, stored.DisputedAt);
        Assert.Equal(88m, stored.TotalScore);

        var history = await _db.PerformanceEvaluationHistories.SingleAsync();
        Assert.Equal(PerformanceEvaluationHistoryAction.Disputed, history.Action);
        Assert.Equal(before, history.FromStatus);
        Assert.Equal(PerformanceEvaluationStatus.Disputed, history.ToStatus);
        Assert.Equal(employee.Id, history.ChangedBy);
        Assert.Empty(await _db.ScoreEntries.ToListAsync());
    }

    [Fact]
    public async Task Create_ExactlyAtTheWindowIsStillAllowedAndOneSecondLaterIsNot()
    {
        var (employee, score) = await EmployeeWithScoreAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => ServiceAt(CalculatedAt + Window + TimeSpan.FromSeconds(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request()));
        var created = await ServiceAt(CalculatedAt + Window).CreateScoreAppealAsync(score.Id, employee.Id, Request());

        Assert.Equal("OPEN", created.Status);
    }

    [Fact]
    public async Task Create_AfterTheWindow_Is422WithTheDedicatedCodeAndChangesNothing()
    {
        var (employee, score) = await EmployeeWithScoreAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => ServiceAt(CalculatedAt + Window + TimeSpan.FromHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request(files: Pdf())));

        Assert.Equal(ErrorCodes.AppealScoreWindowExpired, ex.ErrorCode);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ex.StatusCode);
        Assert.Empty(await _db.Appeals.ToListAsync());
        Assert.Equal(PerformanceEvaluationStatus.Calculated, (await _db.PerformanceEvaluations.SingleAsync()).Status);
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Create_ForAnotherEmployeesScore_IsForbidden()
    {
        var (_, score) = await EmployeeWithScoreAsync();
        var other = await AddEmployeeAsync("OTHER", _salesId);

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, other.Id, Request()));
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Fact]
    public async Task Create_ForAnUnknownScore_IsNotFound()
    {
        var (employee, _) = await EmployeeWithScoreAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(Guid.NewGuid(), employee.Id, Request()));
    }

    [Theory]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task Create_InALockedPeriod_IsPeriodLockedAndNoFileIsStored(PeriodStatus periodStatus)
    {
        await SeedDepartmentsAsync();
        var employee = await AddEmployeeAsync("EMP", _salesId);
        var score = await AddScoreAsync(employee, periodStatus: periodStatus);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request(files: Pdf())));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Empty(await _db.Appeals.ToListAsync());
        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories).Any());
    }

    [Theory]
    [InlineData(PerformanceEvaluationStatus.Draft)]
    [InlineData(PerformanceEvaluationStatus.Approved)]
    [InlineData(PerformanceEvaluationStatus.Final)]
    [InlineData(PerformanceEvaluationStatus.Disputed)]
    public async Task Create_OnAScoreThatIsNotCalculatedOrInReview_IsInvalidState(PerformanceEvaluationStatus status)
    {
        var (employee, score) = await EmployeeWithScoreAsync(status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request()));

        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
        Assert.Empty(await _db.Appeals.ToListAsync());
    }

    [Fact]
    public async Task Create_WithoutATotalScore_IsInvalidState()
    {
        await SeedDepartmentsAsync();
        var employee = await AddEmployeeAsync("EMP", _salesId);
        var score = await AddScoreAsync(employee, totalScore: null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request()));

        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_WhileAnotherAppealIsOpen_IsAlreadyOpen()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        var service = ServiceAt(CalculatedAt.AddHours(1));
        await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());

        // The first appeal froze the score, so a second one is refused before the status is even looked at.
        var stored = await _db.PerformanceEvaluations.SingleAsync();
        stored.Status = PerformanceEvaluationStatus.Calculated;
        stored.PreDisputeStatus = null;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateScoreAppealAsync(score.Id, employee.Id, Request()));
        Assert.Equal(ErrorCodes.AppealAlreadyOpen, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_AfterAnAppealWasDecided_IsInvalidState()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            PerformanceEvaluationId = score.Id,
            Reason = "Earlier",
            Status = AppealStatus.Rejected,
            FiledAt = CalculatedAt,
            UpdatedAt = CalculatedAt,
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request()));
        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Create_AfterAWithdrawnAppeal_IsAllowedAgain()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        var service = ServiceAt(CalculatedAt.AddHours(1));
        var first = await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());
        await service.WithdrawAppealAsync(first.Id, employee.Id);

        var second = await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(PerformanceEvaluationStatus.Disputed, (await _db.PerformanceEvaluations.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Create_WithoutAReason_IsAValidationErrorAndChangesNothing(string? reason)
    {
        var (employee, score) = await EmployeeWithScoreAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(1))
            .CreateScoreAppealAsync(score.Id, employee.Id, Request(reason, Pdf())));
        Assert.Empty(await _db.Appeals.ToListAsync());
        Assert.Equal(PerformanceEvaluationStatus.Calculated, (await _db.PerformanceEvaluations.SingleAsync()).Status);
    }

    // ----- Withdraw --------------------------------------------------------------------------

    [Fact]
    public async Task Withdraw_ReturnsTheScoreToItsPreviousStatusAndRecordsHistory()
    {
        var (employee, score) = await EmployeeWithScoreAsync(PerformanceEvaluationStatus.InReview);
        var service = ServiceAt(CalculatedAt.AddHours(1));
        var created = await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());

        var withdrawn = await service.WithdrawAppealAsync(created.Id, employee.Id);

        Assert.Equal("WITHDRAWN", withdrawn.Status);
        var stored = await _db.PerformanceEvaluations.SingleAsync();
        Assert.Equal(PerformanceEvaluationStatus.InReview, stored.Status);
        Assert.Null(stored.PreDisputeStatus);
        Assert.Null(stored.DisputedAt);
        var actions = await _db.PerformanceEvaluationHistories.OrderBy(h => h.CreatedAt).Select(h => h.Action).ToListAsync();
        Assert.Contains(PerformanceEvaluationHistoryAction.DisputeWithdrawn, actions);
    }

    [Fact]
    public async Task Withdraw_ByAnotherEmployee_IsForbidden()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        var other = await AddEmployeeAsync("OTHER", _salesId);
        var service = ServiceAt(CalculatedAt.AddHours(1));
        var created = await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());

        await Assert.ThrowsAsync<ForbiddenException>(() => service.WithdrawAppealAsync(created.Id, other.Id));
        Assert.Equal(PerformanceEvaluationStatus.Disputed, (await _db.PerformanceEvaluations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Withdraw_KeepsTheScoreDisputed_WhileAnotherAppealOfTheSamePeriodIsInProgress()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        var service = ServiceAt(CalculatedAt.AddHours(1));
        var created = await service.CreateScoreAppealAsync(score.Id, employee.Id, Request());
        // A second, independent appeal (about a result submitted inside the same period) is still in progress.
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-OTHER",
            Title = "Work",
            AssigneeEmployeeId = employee.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = CalculatedAt,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = employee.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = CalculatedAt,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            ResultSubmissionId = submission.Id,
            Reason = "Other",
            Status = AppealStatus.Open,
            FiledAt = CalculatedAt,
            UpdatedAt = CalculatedAt,
        });
        await _db.SaveChangesAsync();

        await service.WithdrawAppealAsync(created.Id, employee.Id);

        Assert.Equal(PerformanceEvaluationStatus.Disputed, (await _db.PerformanceEvaluations.SingleAsync()).Status);
        // The score did not change status, so the withdrawal adds no history row of its own.
        Assert.DoesNotContain(await _db.PerformanceEvaluationHistories.ToListAsync(),
            h => h.Action == PerformanceEvaluationHistoryAction.DisputeWithdrawn);
    }

    // ----- Decide ----------------------------------------------------------------------------

    private async Task<(Employee Manager, AppealDetailDto Appeal, PerformanceEvaluation Score)> OpenScoreAppealAsync(
        PerformanceEvaluationStatus status = PerformanceEvaluationStatus.Calculated)
    {
        var (employee, score) = await EmployeeWithScoreAsync(status);
        var manager = await AddEmployeeAsync("MGR", _branchId);
        var appeal = await ServiceAt(CalculatedAt.AddHours(1)).CreateScoreAppealAsync(score.Id, employee.Id, Request());
        return (manager, appeal, score);
    }

    private static DecideAppealRequestDto Decision(string decision, string? note = "Điểm đánh giá đã đúng", decimal? points = null) =>
        new() { Decision = decision, Note = note, Points = points };

    [Fact]
    public async Task Decide_Uphold_RejectsTheAppealKeepsTheScoreAndReturnsItToItsStatus()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync(PerformanceEvaluationStatus.InReview);
        var now = CalculatedAt.AddHours(5);

        var result = await ServiceAt(now, ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision(" uphold "));

        Assert.Equal("UPHOLD", result.Decision);
        Assert.Equal("REJECTED", result.Appeal.Status);
        Assert.Equal(manager.Id, result.Appeal.ResolvedByEmployeeId);
        Assert.Equal(now, result.Appeal.DecidedAt);
        Assert.Null(result.SubmissionStatus);
        Assert.Null(result.WorkStatus);
        Assert.Null(result.VerificationId);
        Assert.Null(result.ScoreEntryId);
        var stored = await _db.PerformanceEvaluations.SingleAsync();
        Assert.Equal(PerformanceEvaluationStatus.InReview, stored.Status);
        Assert.Equal(88m, stored.TotalScore);
        Assert.Empty(await _db.ScoreEntries.ToListAsync());
        var history = await _db.PerformanceEvaluationHistories.OrderBy(h => h.CreatedAt).ThenBy(h => h.Id).ToListAsync();
        Assert.Contains(history, h => h.Action == PerformanceEvaluationHistoryAction.AppealResolved && h.OldTotalScore == 88m && h.NewTotalScore == 88m);
    }

    [Fact]
    public async Task Decide_Adjust_AddsAPositiveLedgerEntryAndChangesTheScoreOnce()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        var result = await ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST", "Điều chỉnh sau khi đối chiếu", 2.5m));

        Assert.Equal("ACCEPTED", result.Appeal.Status);
        var entry = await _db.ScoreEntries.SingleAsync();
        Assert.Equal(result.ScoreEntryId, entry.Id);
        Assert.Equal(ScoreEntryKind.Adjustment, entry.Kind);
        Assert.Equal(ScoreEntrySource.Manager, entry.Source);
        Assert.Equal(2.5m, entry.Points);
        Assert.Equal(appeal.Id, entry.AppealId);
        Assert.Null(entry.WorkId);
        Assert.Equal(90.5m, (await _db.PerformanceEvaluations.SingleAsync()).TotalScore);
        Assert.Equal(PerformanceEvaluationStatus.Calculated, (await _db.PerformanceEvaluations.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1000.5)]
    [InlineData(1.00001)]
    public async Task Decide_Adjust_WithAnInvalidAmount_IsAValidationErrorAndChangesNothing(double points)
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST", points: (decimal)points)));
        Assert.Empty(await _db.ScoreEntries.ToListAsync());
        Assert.Equal(88m, (await _db.PerformanceEvaluations.SingleAsync()).TotalScore);
        Assert.Equal(AppealStatus.Open, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Decide_Adjust_WithoutPoints_IsAValidationError()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST")));
    }

    [Fact]
    public async Task Decide_Uphold_WithPoints_IsAValidationError()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD", points: 1m)));
    }

    [Theory]
    [InlineData("ACCEPT")]
    [InlineData("REJECT")]
    public async Task Decide_OnAScoreAppeal_WithAResultDecision_IsAValidationError(string decision)
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision(decision)));
    }

    [Theory]
    [InlineData("UPHOLD")]
    public async Task Decide_OnAResultAppeal_WithAScoreOnlyDecision_IsAValidationError(string decision)
    {
        await SeedDepartmentsAsync();
        var employee = await AddEmployeeAsync("EMP", _salesId);
        var manager = await AddEmployeeAsync("MGR", _branchId);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-1",
            Title = "Work",
            AssigneeEmployeeId = employee.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = CalculatedAt,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = employee.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = CalculatedAt,
            UpdatedAt = CalculatedAt,
        };
        var appeal = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            ResultSubmissionId = submission.Id,
            Reason = "r",
            Status = AppealStatus.Open,
            FiledAt = CalculatedAt,
            UpdatedAt = CalculatedAt,
        };
        _db.Works.Add(work);
        _db.ResultSubmissions.Add(submission);
        _db.Appeals.Add(appeal);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision(decision)));
    }

    [Fact]
    public async Task Decide_OnAnUnknownAppeal_IsNotFoundEvenWithAScoreOnlyDecision()
    {
        var (manager, _, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(Guid.NewGuid(), manager.Id, Decision("UPHOLD")));
    }

    [Fact]
    public async Task Decide_BelowBranchScope_IsForbidden()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Dept"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD")));
        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(CalculatedAt.AddHours(5))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD")));
        Assert.Equal(AppealStatus.Open, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Decide_OnYourOwnAppeal_IsMakerCannotBeChecker()
    {
        var (employee, score) = await EmployeeWithScoreAsync();
        var appeal = await ServiceAt(CalculatedAt.AddHours(1)).CreateScoreAppealAsync(score.Id, employee.Id, Request());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"))
            .DecideAppealAsync(appeal.Id, employee.Id, Decision("UPHOLD")));

        Assert.Equal(ErrorCodes.MakerCannotBeChecker, ex.ErrorCode);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, ex.StatusCode);
    }

    [Fact]
    public async Task Decide_AnEmployeeOutsideTheBranchScope_IsForbidden()
    {
        var (_, appeal, _) = await OpenScoreAppealAsync();
        var otherBranch = new Department
        {
            Id = Guid.NewGuid(),
            Code = "BR2",
            Name = "Other branch",
            LevelType = DepartmentLevelType.Branch,
            Status = RecordStatus.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _db.Departments.Add(otherBranch);
        await _db.SaveChangesAsync();
        var outsider = await AddEmployeeAsync("OUT", otherBranch.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, outsider.Id, Decision("UPHOLD")));
    }

    [Fact]
    public async Task Decide_AnAlreadyDecidedAppeal_IsInvalidState()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"));
        await service.DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD")));
        Assert.Equal(ErrorCodes.AppealInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Decide_InALockedPeriod_IsPeriodLockedAndChangesNothing()
    {
        var (manager, appeal, score) = await OpenScoreAppealAsync();
        var period = await _db.EvaluationPeriods.SingleAsync(p => p.Id == score.EvaluationPeriodId);
        period.Status = PeriodStatus.Locked;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST", points: 1m)));

        Assert.Equal(ErrorCodes.PeriodLocked, ex.ErrorCode);
        Assert.Empty(await _db.ScoreEntries.ToListAsync());
        Assert.Equal(AppealStatus.Open, (await _db.Appeals.SingleAsync()).Status);
    }

    [Fact]
    public async Task Decide_RequiresANote()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        await Assert.ThrowsAsync<ValidationException>(() => ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("UPHOLD", note: "short")));
    }

    [Fact]
    public async Task AScoreAdjustmentSurvivesARecalculationOfTheScore()
    {
        var (manager, appeal, score) = await OpenScoreAppealAsync();
        await ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST", "Điều chỉnh sau khi đối chiếu", 4m));

        var net = await AppealScoreLedger.NetByEmployeeAsync(_db, [score.EmployeeId], score.EvaluationPeriodId, CancellationToken.None);

        Assert.Equal(4m, net[score.EmployeeId]);
    }

    // ----- Detail ----------------------------------------------------------------------------

    [Fact]
    public async Task Detail_OfAScoreAppeal_HasNoSubmissionFieldsAndNamesThePeriod()
    {
        var (_, appeal, score) = await OpenScoreAppealAsync();
        var employee = await _db.Employees.SingleAsync(e => e.Id == appeal.EmployeeId);

        var detail = await ServiceAt(CalculatedAt.AddHours(2)).GetAppealByIdAsync(appeal.Id, employee.Id);

        Assert.Equal("SCORE", detail.Type);
        Assert.Null(detail.ResultSubmissionId);
        Assert.Null(detail.WorkId);
        Assert.Null(detail.SubmissionNo);
        Assert.Equal(score.Id, detail.PerformanceEvaluationId);
        Assert.Equal(score.EvaluationPeriod.Code, detail.PeriodCode);
        Assert.Null(detail.SubmittedAt);
        Assert.Null(detail.WorkTypeName);
        Assert.Null(detail.ReferrerEmployeeCode);
        Assert.Null(detail.ReferrerEmployeeName);
    }

    [Fact]
    public async Task DetailAndList_OfAScoreAppeal_CarryTheCurrentScore()
    {
        var (manager, appeal, score) = await OpenScoreAppealAsync();
        var employee = await _db.Employees.SingleAsync(e => e.Id == appeal.EmployeeId);
        var service = ServiceAt(CalculatedAt.AddHours(2), ("verification", "decide", "Branch"));

        var detail = await service.GetAppealByIdAsync(appeal.Id, employee.Id);
        var listed = Assert.Single((await service.GetAppealsAsync(manager.Id, new AppealFilterDto())).Items);

        Assert.Equal(score.TotalScore, detail.EvaluationScore);
        Assert.Equal(score.TotalScore, listed.EvaluationScore);
        Assert.NotNull(detail.EvaluationScore);
    }

    [Fact]
    public async Task Decide_Adjust_ReturnsTheDetailWithTheScoreAfterTheAdjustment()
    {
        var (manager, appeal, _) = await OpenScoreAppealAsync();

        var result = await ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"))
            .DecideAppealAsync(appeal.Id, manager.Id, Decision("ADJUST", "Điều chỉnh sau khi đối chiếu", 2.5m));

        Assert.Equal((await _db.PerformanceEvaluations.SingleAsync()).TotalScore, result.Appeal.EvaluationScore);
        Assert.Equal("ACCEPTED", result.Appeal.Status);
    }

    // ----- List filters ----------------------------------------------------------------------

    private sealed record ListOrg(Employee Seller, Employee Teller, Employee Manager, Appeal SellerScore, Appeal TellerResult, Appeal ManagerOwn);

    private async Task<ListOrg> SeedListAsync(Guid? sellerPosition = null, Guid? tellerPosition = null)
    {
        await SeedDepartmentsAsync();
        var seller = await AddEmployeeAsync("SELLER", _salesId, sellerPosition);
        var teller = await AddEmployeeAsync("TELLER", _tellerId, tellerPosition);
        var manager = await AddEmployeeAsync("MGR", _branchId);
        var score = await AddScoreAsync(seller);
        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-T",
            Title = "Teller work",
            AssigneeEmployeeId = teller.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = CalculatedAt,
        };
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            SubmitterEmployeeId = teller.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = new DateTimeOffset(2026, 10, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = CalculatedAt,
        };
        var managerWork = new Work
        {
            Id = Guid.NewGuid(),
            Code = "W-M",
            Title = "Manager work",
            AssigneeEmployeeId = manager.Id,
            Status = WorkStatus.Rejected,
            Priority = WorkPriority.Medium,
            UpdatedAt = CalculatedAt,
        };
        var managerSubmission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = managerWork.Id,
            SubmitterEmployeeId = manager.Id,
            SubmissionNo = 1,
            Status = SubmissionStatus.Rejected,
            SubmittedAt = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero),
            UpdatedAt = CalculatedAt,
        };
        var sellerScore = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = seller.Id,
            PerformanceEvaluationId = score.Id,
            Reason = "score",
            Status = AppealStatus.Open,
            FiledAt = CalculatedAt.AddHours(1),
            UpdatedAt = CalculatedAt,
        };
        var tellerResult = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = teller.Id,
            ResultSubmissionId = submission.Id,
            Reason = "result",
            Status = AppealStatus.Accepted,
            Note = "Chấp nhận",
            FiledAt = CalculatedAt.AddHours(2),
            UpdatedAt = CalculatedAt,
        };
        var managerOwn = new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = manager.Id,
            ResultSubmissionId = managerSubmission.Id,
            Reason = "own",
            Status = AppealStatus.UnderReview,
            FiledAt = CalculatedAt.AddHours(3),
            UpdatedAt = CalculatedAt,
        };
        _db.Works.AddRange(work, managerWork);
        _db.ResultSubmissions.AddRange(submission, managerSubmission);
        _db.Appeals.AddRange(sellerScore, tellerResult, managerOwn);
        await _db.SaveChangesAsync();
        return new ListOrg(seller, teller, manager, sellerScore, tellerResult, managerOwn);
    }

    private static List<Guid> Ids(G54.BLL.Common.Models.PagedResult<AppealListItemDto> result) => result.Items.Select(i => i.Id).ToList();

    [Fact]
    public async Task List_ScopeMineAndTeam_SplitTheCallersOwnAppealsFromTheOthers()
    {
        var org = await SeedListAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5), ("verification", "decide", "Branch"));

        var mine = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Scope = " mine " });
        var team = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Scope = "TEAM" });
        var both = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto());

        Assert.Equal([org.ManagerOwn.Id], Ids(mine));
        Assert.Equal(2, team.TotalCount);
        Assert.DoesNotContain(org.ManagerOwn.Id, Ids(team));
        Assert.Equal(3, both.TotalCount);
    }

    [Fact]
    public async Task List_ScopeTeam_OfAnEmployeeWithoutAnyScope_IsEmptyAndMineStillShowsOwn()
    {
        var org = await SeedListAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5));

        Assert.Empty((await service.GetAppealsAsync(org.Seller.Id, new AppealFilterDto { Scope = "TEAM" })).Items);
        Assert.Equal([org.SellerScore.Id], Ids(await service.GetAppealsAsync(org.Seller.Id, new AppealFilterDto { Scope = "MINE" })));
    }

    [Fact]
    public async Task List_AnUnknownScopeOrTypeOrStatus_IsAValidationError()
    {
        var org = await SeedListAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"));

        await Assert.ThrowsAsync<ValidationException>(() => service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Scope = "ALL" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Type = "OTHER" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Status = "OPEN,DONE" }));
    }

    [Fact]
    public async Task List_FiltersByTypeAndByAListOfStatuses()
    {
        var org = await SeedListAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"));

        var scores = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Type = "score" });
        var results = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Type = "RESULT" });
        var openOrReview = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Status = "open, under_review" });
        var resolved = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Status = "ACCEPTED,REJECTED,WITHDRAWN" });

        Assert.Equal([org.SellerScore.Id], Ids(scores));
        Assert.Equal(2, results.TotalCount);
        Assert.Equal(2, openOrReview.TotalCount);
        Assert.Equal([org.TellerResult.Id], Ids(resolved));
    }

    [Fact]
    public async Task List_FiltersByJobPosition()
    {
        var sellerPosition = Guid.NewGuid();
        var org = await SeedListAsync(sellerPosition, Guid.NewGuid());
        var service = ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"));

        var result = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { JobPositionId = sellerPosition });

        Assert.Equal([org.SellerScore.Id], Ids(result));
    }

    [Fact]
    public async Task List_FiltersByPeriod_ScoreAppealsOfThePeriodAndResultsSubmittedInIt()
    {
        var org = await SeedListAsync();
        var period = await _db.EvaluationPeriods.SingleAsync();
        var service = ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"));

        var result = await service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { PeriodId = period.Id });

        // October: the seller's score appeal and the teller's result (submitted 10 Oct); the manager's was submitted in September.
        Assert.Equal(2, result.TotalCount);
        Assert.DoesNotContain(org.ManagerOwn.Id, Ids(result));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAppealsAsync(org.Manager.Id, new AppealFilterDto { PeriodId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task List_ShowsThePositionTitleHandlerAndNoteOfEachAppeal()
    {
        var positionId = Guid.NewGuid();
        var org = await SeedListAsync(tellerPosition: positionId);
        _db.JobPositions.Add(new JobPosition { Id = positionId, Code = "TLR", Title = "Giao dịch viên", Status = RecordStatus.Active, LastSyncAt = CalculatedAt, UpdatedAt = CalculatedAt });
        var stored = await _db.Appeals.SingleAsync(a => a.Id == org.TellerResult.Id);
        stored.ResolvedBy = org.Manager.Id;
        await _db.SaveChangesAsync();

        var result = await ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"))
            .GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Type = "RESULT", Status = "ACCEPTED" });

        var row = Assert.Single(result.Items);
        Assert.Equal("Giao dịch viên", row.JobPositionTitle);
        Assert.Equal(org.Manager.FullName, row.ResolvedByEmployeeName);
        Assert.Equal("Chấp nhận", row.Note);
        Assert.Equal("RESULT", row.Type);
    }

    [Fact]
    public async Task List_AJobPositionThatDoesNotExist_ShowsNoTitleInsteadOfFailing()
    {
        var org = await SeedListAsync();

        var result = await ServiceAt(CalculatedAt.AddHours(5), ("*", "*", "All"))
            .GetAppealsAsync(org.Manager.Id, new AppealFilterDto { Type = "SCORE" });

        Assert.Null(Assert.Single(result.Items).JobPositionTitle);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class GrantingPermissions((string Resource, string Action, string Scope)[] grants) : IPermissionService
    {
        public Task<UserPermissionsResponseDto?> GetUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) =>
            Task.FromResult<UserPermissionsResponseDto?>(null);

        public Task<IReadOnlyList<UserPermissionDto>> GetEffectivePermissionsAsync(Guid accountId, CancellationToken cancellationToken)
        {
            IReadOnlyList<UserPermissionDto> result = grants.Select(g => new UserPermissionDto(g.Resource, g.Action, g.Scope)).ToList();
            return Task.FromResult(result);
        }

        public Task<bool> HasPermissionAsync(Guid accountId, string resource, string action, string requiredScope, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task InvalidateUserPermissionsAsync(Guid accountId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InvalidateRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
