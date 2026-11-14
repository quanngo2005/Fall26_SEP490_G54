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
/// <c>POST /evaluations/personal/acknowledge</c> (UC105, GB-649): the evaluated employee confirms a finished result. A draft or a
/// score under dispute cannot be confirmed; confirming twice keeps the first time; an edit of the result asks for a new confirmation.
/// </summary>
public sealed class EvaluationAcknowledgeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private EvaluationPeriod _period = null!;
    private Employee _employee = null!;
    private Employee _colleague = null!;

    public EvaluationAcknowledgeTests()
    {
        var department = new Department
        {
            Id = Guid.NewGuid(),
            Code = "D",
            Name = "Department",
            LevelType = DepartmentLevelType.Department,
            Status = RecordStatus.Active,
            LastSyncAt = Now,
            UpdatedAt = Now,
        };
        var position = new JobPosition { Id = Guid.NewGuid(), Code = "J", Title = "Teller", Status = RecordStatus.Active, LastSyncAt = Now, UpdatedAt = Now };
        _period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-11",
            Name = "Month 11",
            StartDate = new DateOnly(2026, 11, 1),
            EndDate = new DateOnly(2026, 11, 30),
            PeriodType = PeriodType.Month,
            Status = PeriodStatus.InReview,
            UpdatedAt = Now,
        };
        DowngradeTestData.Seed(_db);
        _db.Departments.Add(department);
        _db.JobPositions.Add(position);
        _db.EvaluationPeriods.Add(_period);
        _employee = AddEmployee("EMP-1", department, position);
        _colleague = AddEmployee("EMP-2", department, position);
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private EvaluationService Service => new(
        _db,
        NullLogger<EvaluationService>.Instance,
        new FakePermissionService(null),
        new PerformanceScoreCalculator(_db, new PerformanceScoreFormula()));

    private Employee AddEmployee(string code, Department department, JobPosition position)
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
        _db.Employees.Add(employee);
        _db.Accounts.Add(new Account { Id = Guid.NewGuid(), EmployeeId = employee.Id, Username = code, PasswordHash = "h", LastSyncAt = Now, UpdatedAt = Now });
        return employee;
    }

    private PerformanceEvaluation AddEvaluation(
        Employee employee,
        PerformanceEvaluationStatus status,
        decimal? score = 85m,
        string? comment = "Nhan xet cua quan ly")
    {
        var profile = _db.EvaluationProfiles.Local.FirstOrDefault() ?? _db.EvaluationProfiles.Add(new EvaluationProfile
        {
            Id = Guid.NewGuid(),
            JobPositionId = employee.JobPositionId,
            Code = "EP",
            Name = "Profile",
            Version = 1,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Status = EvaluationProfileStatus.Draft,
            UpdatedAt = Now,
        }).Entity;
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = _period.Id,
            EvaluationProfileId = profile.Id,
            TotalScore = score,
            Status = status,
            Comment = comment,
            CalculatedAt = Now,
            UpdatedAt = Now,
        };
        _db.PerformanceEvaluations.Add(evaluation);
        _db.SaveChanges();
        return evaluation;
    }

    private Task<AcknowledgePersonalEvaluationResultDto> AcknowledgeAsync(Employee employee) =>
        Service.AcknowledgePersonalEvaluationAsync(
            new AcknowledgePersonalEvaluationRequestDto(_period.Id),
            _db.Accounts.Single(a => a.EmployeeId == employee.Id).Id);

    [Theory]
    [InlineData(PerformanceEvaluationStatus.Calculated)]
    [InlineData(PerformanceEvaluationStatus.InReview)]
    [InlineData(PerformanceEvaluationStatus.Approved)]
    [InlineData(PerformanceEvaluationStatus.Final)]
    public async Task Acknowledge_OfAFinishedResult_StampsTheTimeAndKeepsTheManagersComment(PerformanceEvaluationStatus status)
    {
        var evaluation = AddEvaluation(_employee, status);

        var result = await AcknowledgeAsync(_employee);

        Assert.True(result.Success);
        var stored = await _db.PerformanceEvaluations.SingleAsync(pe => pe.Id == evaluation.Id);
        Assert.Contains("ACKNOWLEDGED_AT", stored.Comment, StringComparison.Ordinal);
        Assert.StartsWith("Nhan xet cua quan ly", stored.Comment, StringComparison.Ordinal);
        Assert.Equal(status, stored.Status);
        Assert.Equal(85m, stored.TotalScore);
    }

    [Fact]
    public async Task Acknowledge_Twice_IsIdempotentAndKeepsTheFirstTime()
    {
        AddEvaluation(_employee, PerformanceEvaluationStatus.Final);

        var first = await AcknowledgeAsync(_employee);
        var second = await AcknowledgeAsync(_employee);

        Assert.Equal(first.AcknowledgedAt, second.AcknowledgedAt);
        Assert.True(second.Success);
    }

    [Fact]
    public async Task Acknowledge_IsShownInThePersonalViewWithoutChangingTheComment()
    {
        AddEvaluation(_employee, PerformanceEvaluationStatus.Approved);
        var accountId = _db.Accounts.Single(a => a.EmployeeId == _employee.Id).Id;

        var result = await AcknowledgeAsync(_employee);
        var view = await Service.GetPersonalEvaluationAsync(_period.Id, accountId);

        Assert.True(view.IsAcknowledged);
        Assert.Equal(result.AcknowledgedAt, view.AcknowledgedAt);
        Assert.Equal("Nhan xet cua quan ly", view.ManagerFeedback);
    }

    [Fact]
    public async Task Acknowledge_ADraft_Returns409AndWritesNothing()
    {
        var evaluation = AddEvaluation(_employee, PerformanceEvaluationStatus.Draft);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => AcknowledgeAsync(_employee));

        Assert.Equal(ErrorCodes.PerformanceEvaluationInvalidState, ex.ErrorCode);
        Assert.DoesNotContain("ACKNOWLEDGED_AT", (await _db.PerformanceEvaluations.SingleAsync(pe => pe.Id == evaluation.Id)).Comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Acknowledge_AResultWithoutAScore_Returns409()
    {
        AddEvaluation(_employee, PerformanceEvaluationStatus.Calculated, score: null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => AcknowledgeAsync(_employee));

        Assert.Equal(ErrorCodes.PerformanceEvaluationInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task Acknowledge_AScoreUnderDispute_Returns409DisputePending()
    {
        var evaluation = AddEvaluation(_employee, PerformanceEvaluationStatus.Disputed);
        evaluation.PreDisputeStatus = PerformanceEvaluationStatus.Calculated;
        evaluation.DisputedAt = Now;
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => AcknowledgeAsync(_employee));

        Assert.Equal(ErrorCodes.EvaluationDisputePending, ex.ErrorCode);
        Assert.DoesNotContain("ACKNOWLEDGED_AT", (await _db.PerformanceEvaluations.SingleAsync()).Comment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AppealStatus.Open)]
    [InlineData(AppealStatus.UnderReview)]
    public async Task Acknowledge_WithAnAppealInProgress_Returns409DisputePending(AppealStatus status)
    {
        var evaluation = AddEvaluation(_employee, PerformanceEvaluationStatus.Calculated);
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = _employee.Id,
            PerformanceEvaluationId = evaluation.Id,
            Status = status,
            Reason = "Dispute the score",
            FiledAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => AcknowledgeAsync(_employee));

        Assert.Equal(ErrorCodes.EvaluationDisputePending, ex.ErrorCode);
    }

    [Theory]
    [InlineData(AppealStatus.Accepted)]
    [InlineData(AppealStatus.Rejected)]
    [InlineData(AppealStatus.Withdrawn)]
    public async Task Acknowledge_AfterTheAppealIsClosed_IsAllowed(AppealStatus status)
    {
        var evaluation = AddEvaluation(_employee, PerformanceEvaluationStatus.Calculated);
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = _employee.Id,
            PerformanceEvaluationId = evaluation.Id,
            Status = status,
            Reason = "Dispute the score",
            FiledAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        var result = await AcknowledgeAsync(_employee);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Acknowledge_OnlyTouchesTheCallersOwnResult()
    {
        var mine = AddEvaluation(_employee, PerformanceEvaluationStatus.Final, comment: "mine");
        var theirs = AddEvaluation(_colleague, PerformanceEvaluationStatus.Final, comment: "theirs");

        await AcknowledgeAsync(_employee);

        Assert.Contains("ACKNOWLEDGED_AT", (await _db.PerformanceEvaluations.SingleAsync(pe => pe.Id == mine.Id)).Comment, StringComparison.Ordinal);
        Assert.Equal("theirs", (await _db.PerformanceEvaluations.SingleAsync(pe => pe.Id == theirs.Id)).Comment);
    }

    [Fact]
    public async Task Acknowledge_WithoutAResultInThePeriod_Returns404()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => AcknowledgeAsync(_employee));

        Assert.Equal(ErrorCodes.NotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task Acknowledge_ByAnUnknownAccount_Returns401()
    {
        AddEvaluation(_employee, PerformanceEvaluationStatus.Final);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            Service.AcknowledgePersonalEvaluationAsync(new AcknowledgePersonalEvaluationRequestDto(_period.Id), Guid.NewGuid()));

        Assert.Equal(ErrorCodes.Unauthorized, ex.ErrorCode);
    }
}
