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

    private ResultSubmission AddSubmission(SubmissionStatus status, DateTimeOffset submittedAt)
    {
        var submission = new ResultSubmission
        {
            Id = Guid.NewGuid(),
            WorkId = Guid.NewGuid(),
            SubmitterEmployeeId = Guid.NewGuid(),
            SubmissionNo = 1,
            Status = status,
            SubmittedAt = submittedAt,
            UpdatedAt = Now,
        };
        _db.ResultSubmissions.Add(submission);
        return submission;
    }

    private void AddAppeal(AppealStatus status, ResultSubmission submission) =>
        _db.Appeals.Add(new Appeal
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            ResultSubmissionId = submission.Id,
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
        AddSubmission(SubmissionStatus.UnderReview, Now);
        AddSubmission(SubmissionStatus.Submitted, Now);
        var rejected = AddSubmission(SubmissionStatus.Rejected, Now);
        var outside = AddSubmission(SubmissionStatus.Rejected, Now.AddMonths(-3));
        AddAppeal(AppealStatus.Open, rejected);
        AddAppeal(AppealStatus.Accepted, rejected);
        AddAppeal(AppealStatus.Open, outside);
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

    [Fact]
    public async Task GetPeriods_DefaultFilter_ReturnsPagedResult()
    {
        var p1 = await AddPeriodAsync();
        var p2 = await AddPeriodAsync();

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto());

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task GetPeriods_FilterByKeyword_MatchesCodeOrName()
    {
        var p1 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "SPECIAL-2026",
            Name = "Kỳ đặc biệt",
            PeriodType = PeriodType.Year,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        var p2 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "NORMAL-2026",
            Name = "Kỳ thông thường",
            PeriodType = PeriodType.Year,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        var codeResult = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Keyword = "special" });
        Assert.Single(codeResult.Items);
        Assert.Equal(p1.Id, codeResult.Items[0].Id);

        var nameResult = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Keyword = "thông thường" });
        Assert.Single(nameResult.Items);
        Assert.Equal(p2.Id, nameResult.Items[0].Id);
    }

    [Fact]
    public async Task GetPeriods_FilterByPeriodType_ReturnsMatchingOnly()
    {
        var year = await AddPeriodAsync();
        var month = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "2026-M10",
            Name = "Tháng 10/2026",
            PeriodType = PeriodType.Month,
            ParentPeriodId = year.Id,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(month);
        await _db.SaveChangesAsync();

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { PeriodType = "MONTH" });

        Assert.Single(result.Items);
        Assert.Equal(month.Id, result.Items[0].Id);
        Assert.Equal("MONTH", result.Items[0].PeriodType);
    }

    [Theory]
    [InlineData("INVALID_TYPE")]
    [InlineData("CENTURY")]
    public async Task GetPeriods_InvalidPeriodType_ThrowsValidationException(string type) =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { PeriodType = type }));

    [Fact]
    public async Task GetPeriods_FilterByStatus_ReturnsMatchingOnly()
    {
        var open = await AddPeriodAsync(PeriodStatus.Open);
        var closed = await AddPeriodAsync(PeriodStatus.Closed);

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Status = "CLOSED" });

        Assert.Single(result.Items);
        Assert.Equal(closed.Id, result.Items[0].Id);
        Assert.Equal("CLOSED", result.Items[0].Status);
    }

    [Fact]
    public async Task GetPeriods_InvalidStatus_ThrowsValidationException() =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Status = "UNKNOWN_STATUS" }));

    [Fact]
    public async Task GetPeriods_FilterByParentPeriodId_ReturnsDirectChildren()
    {
        var parent1 = await AddPeriodAsync();
        var parent2 = await AddPeriodAsync();

        var child1 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "CHILD-1",
            Name = "Child 1",
            PeriodType = PeriodType.Month,
            ParentPeriodId = parent1.Id,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        var child2 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "CHILD-2",
            Name = "Child 2",
            PeriodType = PeriodType.Month,
            ParentPeriodId = parent2.Id,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.AddRange(child1, child2);
        await _db.SaveChangesAsync();

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { ParentPeriodId = parent1.Id });

        Assert.Single(result.Items);
        Assert.Equal(child1.Id, result.Items[0].Id);
        Assert.Equal(parent1.Id, result.Items[0].ParentPeriodId);
        Assert.Equal(parent1.Code, result.Items[0].ParentPeriodCode);
    }

    [Fact]
    public async Task GetPeriods_FilterByYear_ReturnsMatchingPeriods()
    {
        var p2025 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "P-2025",
            Name = "2025",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(2025, 1, 1),
            EndDate = new DateOnly(2025, 12, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        var p2026 = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "P-2026",
            Name = "2026",
            PeriodType = PeriodType.Year,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.AddRange(p2025, p2026);
        await _db.SaveChangesAsync();

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Year = 2026 });

        Assert.Single(result.Items);
        Assert.Equal(p2026.Id, result.Items[0].Id);
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(2101)]
    public async Task GetPeriods_InvalidYear_ThrowsValidationException(int year) =>
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Year = year }));

    [Fact]
    public async Task GetPeriods_Pagination_RespectsPageAndPageSize()
    {
        for (var i = 1; i <= 5; i++)
        {
            _db.EvaluationPeriods.Add(new EvaluationPeriod
            {
                Id = Guid.NewGuid(),
                Code = $"P-{i:D2}",
                Name = $"Period {i}",
                PeriodType = PeriodType.Year,
                StartDate = Start.AddDays(i),
                EndDate = End.AddDays(i),
                Status = PeriodStatus.Open,
                UpdatedAt = Now,
            });
        }
        await _db.SaveChangesAsync();

        var page1 = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Page = 1, PageSize = 2 });
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);

        var page2 = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Page = 2, PageSize = 2 });
        Assert.Equal(2, page2.Items.Count);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);

        var page3 = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto { Page = 3, PageSize = 2 });
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task GetPeriods_DeterministicOrdering_OrdersByStartDateDescThenCode()
    {
        var sameDateEarlierCode = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "A-PERIOD",
            Name = "A Period",
            PeriodType = PeriodType.Year,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        var sameDateLaterCode = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "Z-PERIOD",
            Name = "Z Period",
            PeriodType = PeriodType.Year,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        var laterDate = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "M-PERIOD",
            Name = "M Period",
            PeriodType = PeriodType.Year,
            StartDate = Start.AddDays(10),
            EndDate = End.AddDays(10),
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.AddRange(sameDateLaterCode, sameDateEarlierCode, laterDate);
        await _db.SaveChangesAsync();

        var result = await Service.GetPeriodsAsync(new EvaluationPeriodFilterDto());

        Assert.Equal(3, result.Items.Count);
        Assert.Equal("M-PERIOD", result.Items[0].Code);
        Assert.Equal("A-PERIOD", result.Items[1].Code);
        Assert.Equal("Z-PERIOD", result.Items[2].Code);
    }

    [Fact]
    public async Task GetById_ExistingPeriod_ReturnsItemDtoWithParentDetails()
    {
        var parent = await AddPeriodAsync();
        var child = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = "CHILD-10",
            Name = "Child 10",
            PeriodType = PeriodType.Month,
            ParentPeriodId = parent.Id,
            StartDate = Start,
            EndDate = End,
            Status = PeriodStatus.Open,
            UpdatedAt = Now,
        };
        _db.EvaluationPeriods.Add(child);
        await _db.SaveChangesAsync();

        var result = await Service.GetByIdAsync(child.Id);

        Assert.NotNull(result);
        Assert.Equal(child.Id, result.Id);
        Assert.Equal("CHILD-10", result.Code);
        Assert.Equal("MONTH", result.PeriodType);
        Assert.Equal(parent.Id, result.ParentPeriodId);
        Assert.Equal(parent.Code, result.ParentPeriodCode);
        Assert.Equal(parent.Name, result.ParentPeriodName);
    }

    [Fact]
    public async Task GetById_NonExistentPeriod_ReturnsNull()
    {
        var result = await Service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
