using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using G54.Api.Authorization;
using G54.Api.Controllers;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.RiskEvent;
using G54.BLL.Services;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class RiskEventServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset OccurredAt = new(2026, 10, 20, 3, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public void Dispose() => _db.Dispose();

    private RiskEventService Service => new(_db, NullLogger<RiskEventService>.Instance, new FixedTime(Now));

    private async Task<Employee> AddEmployeeAsync(string code)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = code,
            FullName = $"Name {code}",
            Email = $"{code}@bank.test",
            DepartmentId = Guid.NewGuid(),
            JobPositionId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
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

    private async Task<EvaluationPeriod> AddPeriodAsync(
        string code, DateOnly start, DateOnly end, PeriodStatus status = PeriodStatus.Open)
    {
        var period = new EvaluationPeriod
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            PeriodType = PeriodType.Month,
            StartDate = start,
            EndDate = end,
            Status = status,
        };
        _db.EvaluationPeriods.Add(period);
        await _db.SaveChangesAsync();
        return period;
    }

    private async Task<PerformanceEvaluation> AddEvaluationAsync(Employee employee, EvaluationPeriod period, decimal score = 80m)
    {
        var evaluation = new PerformanceEvaluation
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            EvaluationProfileId = Guid.NewGuid(),
            TotalScore = score,
            Status = PerformanceEvaluationStatus.Calculated,
        };
        _db.PerformanceEvaluations.Add(evaluation);
        await _db.SaveChangesAsync();
        return evaluation;
    }

    private async Task<RiskEvent> AddRiskEventAsync(
        Employee employee,
        RiskEventSeverity severity = RiskEventSeverity.High,
        RiskEventStatus status = RiskEventStatus.Open,
        DateTimeOffset? occurredAt = null)
    {
        var riskEvent = new RiskEvent
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.Id,
            RecordedBy = Guid.NewGuid(),
            Code = $"RISK-{Guid.NewGuid():N}"[..12],
            EventType = "FRAUD",
            Description = "Risk event",
            OccurredAt = occurredAt ?? OccurredAt,
            UpdatedAt = Now.AddDays(-1),
            Severity = severity,
            Status = status,
        };
        _db.RiskEvents.Add(riskEvent);
        await _db.SaveChangesAsync();
        return riskEvent;
    }

    private static DateOnly Day(int month, int day) => new(2026, month, day);

    // ----- Confirm ---------------------------------------------------------------------------

    [Theory]
    [InlineData(RiskEventSeverity.Low, -2, "LOW")]
    [InlineData(RiskEventSeverity.Medium, -5, "MEDIUM")]
    [InlineData(RiskEventSeverity.High, -10, "HIGH")]
    [InlineData(RiskEventSeverity.Critical, -20, "CRITICAL")]
    public async Task Confirm_OpenEvent_ConfirmsItAndWritesANegativeContribution(RiskEventSeverity severity, int expected, string wire)
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var period = await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var evaluation = await AddEvaluationAsync(employee, period);
        var riskEvent = await AddRiskEventAsync(employee, severity);

        var result = await Service.ConfirmAsync(riskEvent.Id, qa.Id);

        Assert.Equal(expected, result.ComponentScore);
        Assert.Equal(wire, result.Severity);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal(period.Id, result.EvaluationPeriodId);
        Assert.Equal(evaluation.Id, result.PerformanceEvaluationId);
        Assert.Equal(Now, result.UpdatedAt);
        Assert.Equal(RiskEventStatus.Confirmed, riskEvent.Status);
        Assert.Equal(Now, riskEvent.UpdatedAt);
        var contribution = await _db.PerformanceContributions.SingleAsync();
        Assert.Equal(result.ContributionId, contribution.Id);
        Assert.Equal(riskEvent.Id, contribution.RiskEventId);
        Assert.Equal(employee.Id, contribution.EmployeeId);
        Assert.Equal(expected, contribution.ComponentScore);
        Assert.Null(contribution.ActualProgressId);
        Assert.Null(contribution.TargetValueSnapshot);
        Assert.Null(contribution.WeightSnapshot);
        Assert.Equal(80m, evaluation.TotalScore);
    }

    [Fact]
    public async Task Confirm_WithoutAnEvaluationInThePeriod_LeavesTheEvaluationIdEmpty()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var period = await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var other = await AddEmployeeAsync("OTHER");
        await AddEvaluationAsync(other, period);
        var riskEvent = await AddRiskEventAsync(employee);

        var result = await Service.ConfirmAsync(riskEvent.Id, qa.Id);

        Assert.Null(result.PerformanceEvaluationId);
        Assert.Null((await _db.PerformanceContributions.SingleAsync()).PerformanceEvaluationId);
    }

    [Fact]
    public async Task Confirm_AcceptsTheCallersAccountId()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);
        var accountId = (await _db.Accounts.SingleAsync(a => a.EmployeeId == qa.Id)).Id;

        await Service.ConfirmAsync(riskEvent.Id, accountId);

        Assert.Equal(RiskEventStatus.Confirmed, riskEvent.Status);
    }

    [Theory]
    [InlineData(RiskEventStatus.Confirmed)]
    [InlineData(RiskEventStatus.Dismissed)]
    [InlineData(RiskEventStatus.Resolved)]
    public async Task Confirm_NonOpenEvent_IsAConflictAndWritesNothing(RiskEventStatus status)
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee, status: status);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventInvalidState, error.ErrorCode);
        Assert.Equal(status, riskEvent.Status);
        Assert.Empty(await _db.PerformanceContributions.ToListAsync());
    }

    [Fact]
    public async Task Confirm_Twice_SecondCallIsAConflictAndKeepsOneContribution()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);

        await Service.ConfirmAsync(riskEvent.Id, qa.Id);
        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventInvalidState, error.ErrorCode);
        Assert.Single(await _db.PerformanceContributions.ToListAsync());
    }

    [Fact]
    public async Task Confirm_OpenEventThatAlreadyHasAContribution_IsAConflict()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var period = await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);
        _db.PerformanceContributions.Add(new PerformanceContribution
        {
            Id = Guid.NewGuid(),
            RiskEventId = riskEvent.Id,
            EmployeeId = employee.Id,
            EvaluationPeriodId = period.Id,
            ComponentScore = -10,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(RiskEventStatus.Open, riskEvent.Status);
    }

    [Fact]
    public async Task Confirm_OwnRiskEvent_IsForbidden()
    {
        var employee = await AddEmployeeAsync("EMP");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.ConfirmAsync(riskEvent.Id, employee.Id));

        Assert.Equal(RiskEventStatus.Open, riskEvent.Status);
        Assert.Empty(await _db.PerformanceContributions.ToListAsync());
    }

    [Fact]
    public async Task Confirm_MissingEvent_IsNotFound()
    {
        var qa = await AddEmployeeAsync("QA");

        await Assert.ThrowsAsync<NotFoundException>(() => Service.ConfirmAsync(Guid.NewGuid(), qa.Id));
    }

    [Fact]
    public async Task Confirm_UnknownOrLockedCaller_IsUnauthorized()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);

        await Assert.ThrowsAsync<UnauthorizedException>(() => Service.ConfirmAsync(riskEvent.Id, Guid.NewGuid()));
        (await _db.Accounts.SingleAsync(a => a.EmployeeId == qa.Id)).IsLocked = true;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(RiskEventStatus.Open, riskEvent.Status);
    }

    [Theory]
    [InlineData(PeriodStatus.Locked)]
    [InlineData(PeriodStatus.Closed)]
    public async Task Confirm_WhenThePeriodIsLockedOrClosed_IsAConflictAndTheEventStaysOpen(PeriodStatus status)
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31), status);
        var riskEvent = await AddRiskEventAsync(employee);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventPeriodUnavailable, error.ErrorCode);
        Assert.Equal(RiskEventStatus.Open, riskEvent.Status);
        Assert.Empty(await _db.PerformanceContributions.ToListAsync());
    }

    [Fact]
    public async Task Confirm_WhenNoPeriodCoversTheEventDate_IsAConflict()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-11", Day(11, 1), Day(11, 30));
        var riskEvent = await AddRiskEventAsync(employee);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventPeriodUnavailable, error.ErrorCode);
    }

    [Fact]
    public async Task Confirm_OverlappingPeriods_UsesTheOneWithTheLatestStart()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-Y", Day(1, 1), new DateOnly(2026, 12, 31));
        var month = await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);

        var result = await Service.ConfirmAsync(riskEvent.Id, qa.Id);

        Assert.Equal(month.Id, result.EvaluationPeriodId);
    }

    [Fact]
    public async Task Confirm_UsesTheUtcPlus7CalendarDayOfTheEvent()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var november = await AddPeriodAsync("2026-11", Day(11, 1), Day(11, 30));
        // 18:00 UTC on Oct 31 is already Nov 1 in UTC+7.
        var riskEvent = await AddRiskEventAsync(employee, occurredAt: new DateTimeOffset(2026, 10, 31, 18, 0, 0, TimeSpan.Zero));

        var result = await Service.ConfirmAsync(riskEvent.Id, qa.Id);

        Assert.Equal(november.Id, result.EvaluationPeriodId);
    }

    // ----- Create / Get ----------------------------------------------------------------------

    private static CreateRiskEventRequestDto ValidRequest(Guid employeeId) => new()
    {
        EmployeeId = employeeId,
        EventType = " FRAUD ",
        Description = "  Giao dịch sai quy định  ",
        Severity = "high",
        OccurredAt = OccurredAt,
    };

    [Fact]
    public async Task Create_StoresAnOpenEventRecordedByTheCaller()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");

        var result = await Service.CreateAsync(ValidRequest(employee.Id), qa.Id);

        Assert.Equal("OPEN", result.Status);
        Assert.Equal("HIGH", result.Severity);
        Assert.Equal("FRAUD", result.EventType);
        Assert.Equal("Giao dịch sai quy định", result.Description);
        Assert.Equal(employee.Id, result.EmployeeId);
        Assert.Equal(qa.Id, result.RecordedBy);
        Assert.Equal(OccurredAt, result.OccurredAt);
        Assert.Equal(Now, result.UpdatedAt);
        Assert.Null(result.ResolvedAt);
        Assert.Null(result.ContributionId);
        Assert.Matches("^RISK-20261028-[0-9A-F]{8}$", result.Code);
        var stored = await _db.RiskEvents.SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal(RiskEventStatus.Open, stored.Status);
        Assert.Empty(await _db.PerformanceContributions.ToListAsync());
    }

    [Fact]
    public async Task Create_GivesEveryEventItsOwnCode()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");

        var first = await Service.CreateAsync(ValidRequest(employee.Id), qa.Id);
        var second = await Service.CreateAsync(ValidRequest(employee.Id), qa.Id);

        Assert.NotEqual(first.Code, second.Code);
    }

    [Fact]
    public async Task Create_AcceptsAnEventHappeningRightNow()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");

        var result = await Service.CreateAsync(ValidRequest(employee.Id) with { OccurredAt = Now }, qa.Id);

        Assert.Equal(Now, result.OccurredAt);
    }

    public static TheoryData<string, CreateRiskEventRequestDto> InvalidRequests => new()
    {
        { "employee", new CreateRiskEventRequestDto { EventType = "FRAUD", Description = "d", Severity = "LOW", OccurredAt = OccurredAt } },
        { "empty employee", new CreateRiskEventRequestDto { EmployeeId = Guid.Empty, EventType = "FRAUD", Description = "d", Severity = "LOW", OccurredAt = OccurredAt } },
        { "blank type", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "  ", Description = "d", Severity = "LOW", OccurredAt = OccurredAt } },
        { "long type", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = new string('x', 51), Description = "d", Severity = "LOW", OccurredAt = OccurredAt } },
        { "blank description", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = " ", Severity = "LOW", OccurredAt = OccurredAt } },
        { "long description", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = new string('x', 2001), Severity = "LOW", OccurredAt = OccurredAt } },
        { "missing severity", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = "d", OccurredAt = OccurredAt } },
        { "unknown severity", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = "d", Severity = "EXTREME", OccurredAt = OccurredAt } },
        { "missing date", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = "d", Severity = "LOW" } },
        { "future date", new CreateRiskEventRequestDto { EmployeeId = Guid.NewGuid(), EventType = "FRAUD", Description = "d", Severity = "LOW", OccurredAt = Now.AddSeconds(1) } },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_InvalidRequest_IsRejectedWithoutWriting(string reason, CreateRiskEventRequestDto request)
    {
        var qa = await AddEmployeeAsync("QA");

        await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(request, qa.Id));

        Assert.Empty(await _db.RiskEvents.ToListAsync());
        Assert.NotEmpty(reason);
    }

    [Fact]
    public async Task Create_ReportsEveryProblemAtOnce()
    {
        var qa = await AddEmployeeAsync("QA");

        var error = await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(new CreateRiskEventRequestDto(), qa.Id));

        Assert.Equal(5, error.Errors!.Count);
    }

    [Fact]
    public async Task Create_ForTheCaller_IsForbidden()
    {
        var qa = await AddEmployeeAsync("QA");

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.CreateAsync(ValidRequest(qa.Id), qa.Id));

        Assert.Empty(await _db.RiskEvents.ToListAsync());
    }

    [Fact]
    public async Task Create_ForAnUnknownEmployeeOrByAnUnknownCaller_Fails()
    {
        var qa = await AddEmployeeAsync("QA");
        var employee = await AddEmployeeAsync("EMP");

        await Assert.ThrowsAsync<NotFoundException>(() => Service.CreateAsync(ValidRequest(Guid.NewGuid()), qa.Id));
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service.CreateAsync(ValidRequest(employee.Id), Guid.NewGuid()));

        Assert.Empty(await _db.RiskEvents.ToListAsync());
    }

    [Fact]
    public async Task Create_NullRequest_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service.CreateAsync(null!, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetById_ReturnsTheEventAndItsContributionOnceConfirmed()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee, RiskEventSeverity.Critical);

        var open = await Service.GetByIdAsync(riskEvent.Id);
        var confirmation = await Service.ConfirmAsync(riskEvent.Id, qa.Id);
        var confirmed = await Service.GetByIdAsync(riskEvent.Id);

        Assert.Null(open.ContributionId);
        Assert.Equal("OPEN", open.Status);
        Assert.Equal("CRITICAL", confirmed.Severity);
        Assert.Equal("CONFIRMED", confirmed.Status);
        Assert.Equal(confirmation.ContributionId, confirmed.ContributionId);
        await Assert.ThrowsAsync<NotFoundException>(() => Service.GetByIdAsync(Guid.NewGuid()));
    }

    // ----- Dismiss ---------------------------------------------------------------------------

    [Fact]
    public async Task Dismiss_OpenEvent_IsDismissedAndWritesNoContribution()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var riskEvent = await AddRiskEventAsync(employee);

        var result = await Service.DismissAsync(riskEvent.Id, qa.Id);

        Assert.Equal("DISMISSED", result.Status);
        Assert.Equal(Now, result.UpdatedAt);
        Assert.Null(result.ResolvedAt);
        Assert.Equal(RiskEventStatus.Dismissed, riskEvent.Status);
        Assert.Empty(await _db.PerformanceContributions.ToListAsync());
    }

    [Theory]
    [InlineData(RiskEventStatus.Confirmed)]
    [InlineData(RiskEventStatus.Dismissed)]
    [InlineData(RiskEventStatus.Resolved)]
    public async Task Dismiss_NonOpenEvent_IsAConflict(RiskEventStatus status)
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var riskEvent = await AddRiskEventAsync(employee, status: status);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.DismissAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventInvalidState, error.ErrorCode);
        Assert.Equal(status, riskEvent.Status);
    }

    [Fact]
    public async Task Dismiss_OwnMissingOrUnauthorized_Fails()
    {
        var employee = await AddEmployeeAsync("EMP");
        var riskEvent = await AddRiskEventAsync(employee);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.DismissAsync(riskEvent.Id, employee.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service.DismissAsync(Guid.NewGuid(), employee.Id));
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service.DismissAsync(riskEvent.Id, Guid.NewGuid()));

        Assert.Equal(RiskEventStatus.Open, riskEvent.Status);
    }

    // ----- Resolve ---------------------------------------------------------------------------

    [Fact]
    public async Task Resolve_ConfirmedEvent_IsResolvedAndKeepsItsContribution()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);
        var confirmation = await Service.ConfirmAsync(riskEvent.Id, qa.Id);

        var result = await new RiskEventService(_db, NullLogger<RiskEventService>.Instance, new FixedTime(Now.AddDays(2)))
            .ResolveAsync(riskEvent.Id, qa.Id);

        Assert.Equal("RESOLVED", result.Status);
        Assert.Equal(Now.AddDays(2), result.ResolvedAt);
        Assert.Equal(Now.AddDays(2), result.UpdatedAt);
        Assert.Equal(confirmation.ContributionId, result.ContributionId);
        Assert.Equal(RiskEventStatus.Resolved, riskEvent.Status);
        Assert.Equal(-10, (await _db.PerformanceContributions.SingleAsync()).ComponentScore);
    }

    [Theory]
    [InlineData(RiskEventStatus.Open)]
    [InlineData(RiskEventStatus.Dismissed)]
    [InlineData(RiskEventStatus.Resolved)]
    public async Task Resolve_NonConfirmedEvent_IsAConflict(RiskEventStatus status)
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        var riskEvent = await AddRiskEventAsync(employee, status: status);

        var error = await Assert.ThrowsAsync<ConflictException>(() => Service.ResolveAsync(riskEvent.Id, qa.Id));

        Assert.Equal(ErrorCodes.RiskEventInvalidState, error.ErrorCode);
        Assert.Equal(status, riskEvent.Status);
        Assert.Null(riskEvent.ResolvedAt);
    }

    [Fact]
    public async Task Resolve_OwnMissingOrUnauthorized_Fails()
    {
        var employee = await AddEmployeeAsync("EMP");
        var riskEvent = await AddRiskEventAsync(employee, status: RiskEventStatus.Confirmed);

        await Assert.ThrowsAsync<ForbiddenException>(() => Service.ResolveAsync(riskEvent.Id, employee.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Service.ResolveAsync(Guid.NewGuid(), employee.Id));
        await Assert.ThrowsAsync<UnauthorizedException>(() => Service.ResolveAsync(riskEvent.Id, Guid.NewGuid()));

        Assert.Equal(RiskEventStatus.Confirmed, riskEvent.Status);
    }

    [Fact]
    public async Task Lifecycle_ConfirmedEventCannotBeDismissedAndResolvedEventCannotBeConfirmedAgain()
    {
        var employee = await AddEmployeeAsync("EMP");
        var qa = await AddEmployeeAsync("QA");
        await AddPeriodAsync("2026-10", Day(10, 1), Day(10, 31));
        var riskEvent = await AddRiskEventAsync(employee);

        await Service.ConfirmAsync(riskEvent.Id, qa.Id);
        await Assert.ThrowsAsync<ConflictException>(() => Service.DismissAsync(riskEvent.Id, qa.Id));
        await Service.ResolveAsync(riskEvent.Id, qa.Id);
        await Assert.ThrowsAsync<ConflictException>(() => Service.ConfirmAsync(riskEvent.Id, qa.Id));

        Assert.Single(await _db.PerformanceContributions.ToListAsync());
    }

    // ----- Controller ------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(RiskEventsController.CreateRiskEvent), "", typeof(HttpPostAttribute), "create")]
    [InlineData(nameof(RiskEventsController.GetRiskEvent), "{id:guid}", typeof(HttpGetAttribute), "read")]
    [InlineData(nameof(RiskEventsController.ConfirmRiskEvent), "{id:guid}/confirm", typeof(HttpPostAttribute), "confirm")]
    [InlineData(nameof(RiskEventsController.DismissRiskEvent), "{id:guid}/dismiss", typeof(HttpPostAttribute), "dismiss")]
    [InlineData(nameof(RiskEventsController.ResolveRiskEvent), "{id:guid}/resolve", typeof(HttpPostAttribute), "resolve")]
    public void Controller_ExposesTheDocumentedRoutesBehindTheirPermission(string action, string template, Type verb, string permissionAction)
    {
        var type = typeof(RiskEventsController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("risk-events", type.GetCustomAttribute<RouteAttribute>()?.Template);
        var method = type.GetMethod(action)!;
        var verbAttribute = (HttpMethodAttribute)method.GetCustomAttribute(verb)!;
        Assert.Equal(template, verbAttribute.Template ?? string.Empty);
        var permission = method.GetCustomAttribute<HasPermissionAttribute>();
        Assert.Equal("risk_event", permission?.Resource);
        Assert.Equal(permissionAction, permission?.Action);
        Assert.Equal("All", permission?.Scope);
    }

    private static RiskEventsController ControllerFor(IRiskEventService service, Guid userId) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                TraceIdentifier = "trace-1",
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "TestAuth")),
            },
        },
    };

    private static JsonElement Envelope(object? value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        return document.RootElement.Clone();
    }

    [Fact]
    public async Task Controller_Confirm_ReturnsTheEnvelopeAndPassesTheCaller()
    {
        var userId = Guid.NewGuid();
        var riskEventId = Guid.NewGuid();
        var service = new RecordingService();

        var result = await ControllerFor(service, userId).ConfirmRiskEvent(riskEventId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(userId, service.UserId);
        Assert.Equal(riskEventId, service.RiskEventId);
        var body = Envelope(ok.Value);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(200, body.GetProperty("status").GetInt32());
        Assert.Equal("CONFIRMED", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(-10, body.GetProperty("data").GetProperty("componentScore").GetDecimal());
    }

    [Fact]
    public async Task Controller_Create_Returns201WithALocationAndAMatchingEnvelope()
    {
        var userId = Guid.NewGuid();
        var service = new RecordingService();
        var request = new CreateRiskEventRequestDto { EventType = "FRAUD" };

        var result = await ControllerFor(service, userId).CreateRiskEvent(request, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(RiskEventsController.GetRiskEvent), created.ActionName);
        Assert.Equal(service.RiskEventId, created.RouteValues!["id"]);
        Assert.Same(request, service.Request);
        Assert.Equal(userId, service.UserId);
        var body = Envelope(created.Value);
        Assert.Equal(201, body.GetProperty("status").GetInt32());
        Assert.Equal("OPEN", body.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("trace-1", body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Controller_GetDismissAndResolve_DelegateToTheService()
    {
        var userId = Guid.NewGuid();
        var riskEventId = Guid.NewGuid();
        var service = new RecordingService();
        var controller = ControllerFor(service, userId);

        var get = await controller.GetRiskEvent(riskEventId, CancellationToken.None);
        var dismiss = await controller.DismissRiskEvent(riskEventId, CancellationToken.None);
        var resolve = await controller.ResolveRiskEvent(riskEventId, CancellationToken.None);

        Assert.Equal(["get", "dismiss", "resolve"], service.Calls);
        Assert.Equal(userId, service.UserId);
        Assert.Equal("OPEN", Envelope(Assert.IsType<OkObjectResult>(get.Result).Value).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("DISMISSED", Envelope(Assert.IsType<OkObjectResult>(dismiss.Result).Value).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("RESOLVED", Envelope(Assert.IsType<OkObjectResult>(resolve.Result).Value).GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Controller_WithoutIdentity_NeverReachesTheService()
    {
        var service = new RecordingService();
        var controller = new RiskEventsController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.CreateRiskEvent(new CreateRiskEventRequestDto(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.ConfirmRiskEvent(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.DismissRiskEvent(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => controller.ResolveRiskEvent(Guid.NewGuid(), CancellationToken.None));

        Assert.Empty(service.Calls);
    }

    private sealed class RecordingService : IRiskEventService
    {
        public List<string> Calls { get; } = [];

        public Guid UserId { get; private set; }

        public Guid RiskEventId { get; private set; } = Guid.NewGuid();

        public CreateRiskEventRequestDto? Request { get; private set; }

        private RiskEventDto Dto(string status) => new(
            RiskEventId, "RISK-1", Guid.NewGuid(), Guid.NewGuid(), "FRAUD", "desc", "HIGH", status, DateTimeOffset.UnixEpoch, null, DateTimeOffset.UnixEpoch, null);

        public Task<RiskEventDto> CreateAsync(CreateRiskEventRequestDto request, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("create");
            Request = request;
            UserId = userId;
            return Task.FromResult(Dto("OPEN"));
        }

        public Task<RiskEventDto> GetByIdAsync(Guid riskEventId, CancellationToken cancellationToken = default)
        {
            Calls.Add("get");
            RiskEventId = riskEventId;
            return Task.FromResult(Dto("OPEN"));
        }

        public Task<RiskEventConfirmationDto> ConfirmAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("confirm");
            RiskEventId = riskEventId;
            UserId = userId;
            return Task.FromResult(new RiskEventConfirmationDto(
                riskEventId, "RISK-1", Guid.NewGuid(), "HIGH", "CONFIRMED", Guid.NewGuid(), Guid.NewGuid(), null, -10m, DateTimeOffset.UnixEpoch));
        }

        public Task<RiskEventDto> DismissAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("dismiss");
            RiskEventId = riskEventId;
            UserId = userId;
            return Task.FromResult(Dto("DISMISSED"));
        }

        public Task<RiskEventDto> ResolveAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
        {
            Calls.Add("resolve");
            RiskEventId = riskEventId;
            UserId = userId;
            return Task.FromResult(Dto("RESOLVED"));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
