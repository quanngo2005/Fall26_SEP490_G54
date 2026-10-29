using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.EvaluationPeriod;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.UnitTests;

public sealed class EvaluationPeriodServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static readonly DateOnly End = new(2026, 10, 31);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private EvaluationPeriodService Service => new(_db, new FixedTime(Now));

    private static CreateEvaluationPeriodRequestDto ValidRequest(string? periodType = "YEAR") =>
        new("2026-10", "Năm 2026", periodType, null, Start, End);

    private async Task<EvaluationPeriod> AddPeriodAsync(PeriodStatus status = PeriodStatus.Open)
    {
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = $"P-{Guid.NewGuid():N}"[..12],
            Name = "Period",
            PeriodType = PeriodType.Year,
            StartDate = Start,
            EndDate = End,
            Status = status,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(period);
        await _db.SaveChangesAsync();
        return period;
    }

    private void AddSubmission(SubmissionStatus status, DateTimeOffset submittedAt) =>
        _db.ResultSubmissions.Add(new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            SubmitterEmployeeId = Guid.NewGuid(),
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = submittedAt,
            UpdatedAt = Now,
        });

    private void AddAppeal(AppealStatus status, Guid? evaluationId = null) =>
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            PerformanceEvaluationId = evaluationId,
            Reason = "reason",
            Status = status,
            FiledAt = Now,
            UpdatedAt = Now,
        });

    [Fact]
    public async Task Create_StoresOpenPeriod()
    {
        var result = await Service.CreateAsync(ValidRequest());

        Assert.Equal("OPEN", result.Status);
        Assert.Equal("YEAR", result.PeriodType);
        var stored = await _db.EvaluationPeriods.SingleAsync(p => p.Id == result.Id);
        Assert.Equal(PeriodStatus.Open, stored.Status);
        Assert.Equal(Now, stored.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DECADE")]
    public async Task Create_InvalidPeriodType_IsRejected(string? periodType) =>
        await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(ValidRequest(periodType)));

    [Fact]
    public async Task Create_EndBeforeStart_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.CreateAsync(ValidRequest() with { StartDate = End, EndDate = Start }));

    [Fact]
    public async Task Create_UnknownParent_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service.CreateAsync(ValidRequest() with { ParentPeriodId = Guid.NewGuid() }));

    [Fact]
    public async Task Create_YearWithParent_IsRejected()
    {
        var year = await AddPeriodAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.CreateAsync(ValidRequest() with { ParentPeriodId = year.Id }));
    }

    [Fact]
    public async Task Create_NonYearWithoutParent_IsRejected() =>
        await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(ValidRequest("HALF_YEAR")));

    [Fact]
    public async Task Create_WrongParentType_IsRejected()
    {
        var year = await AddPeriodAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.CreateAsync(ValidRequest("MONTH") with { Code = "m", ParentPeriodId = year.Id }));
    }

    [Fact]
    public async Task Create_OutsideParentDates_IsRejected()
    {
        var year = await AddPeriodAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.CreateAsync(ValidRequest("HALF_YEAR") with { ParentPeriodId = year.Id, EndDate = End.AddDays(1) }));
    }

    [Fact]
    public async Task Create_ChildInsideParent_Succeeds()
    {
        var year = await AddPeriodAsync();

        var result = await Service.CreateAsync(ValidRequest("HALF_YEAR") with { ParentPeriodId = year.Id });

        Assert.Equal(year.Id, result.ParentPeriodId);
    }

    [Fact]
    public async Task Close_MovesToInReview_AndCountsPending()
    {
        var period = await AddPeriodAsync();
        var evaluationId = Guid.NewGuid();
        _db.PerformanceEvaluations.Add(new PerformanceEvaluation
        {
            Id = evaluationId,
            EmployeeId = Guid.NewGuid(),
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = Guid.NewGuid(),
            Status = PerformanceEvaluationStatus.Disputed,
            UpdatedAt = Now,
        });
        AddSubmission(SubmissionStatus.UnderReview, Now);
        AddSubmission(SubmissionStatus.Submitted, Now);
        AddSubmission(SubmissionStatus.Approved, Now);
        AddSubmission(SubmissionStatus.Submitted, Now.AddMonths(-3));
        AddAppeal(AppealStatus.Open, evaluationId);
        AddAppeal(AppealStatus.Accepted, evaluationId);
        AddAppeal(AppealStatus.Open);
        await _db.SaveChangesAsync();

        var result = await Service.CloseAsync(period.Id);

        Assert.Equal("IN_REVIEW", result.Status);
        Assert.Equal(2, result.PendingInReviewResults);
        Assert.Equal(1, result.OpenDisputes);
        Assert.Equal(PeriodStatus.InReview, (await _db.EvaluationPeriods.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(PeriodStatus.InReview)]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task Close_NotOpen_IsConflict(PeriodStatus status)
    {
        var period = await AddPeriodAsync(status);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Service.CloseAsync(period.Id));

        Assert.Equal(ErrorCodes.EvaluationPeriodInvalidState, ex.ErrorCode);
        Assert.Equal(status, (await _db.EvaluationPeriods.SingleAsync()).Status);
    }

    [Fact]
    public async Task Close_UnknownPeriod_IsNotFound() =>
        await Assert.ThrowsAsync<NotFoundException>(() => Service.CloseAsync(Guid.NewGuid()));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
