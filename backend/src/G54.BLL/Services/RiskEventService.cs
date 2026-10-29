using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.RiskEvent;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class RiskEventService(AppDbContext db, ILogger<RiskEventService> logger, TimeProvider time) : IRiskEventService
{
    private const int MaxEventTypeLength = 50;
    private const int MaxDescriptionLength = 2000;

    private static readonly Action<ILogger, string, Guid, Guid, Guid, Exception?> LogAction =
        LoggerMessage.Define<string, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, nameof(LogAction)),
            "Risk event audit: Action={Action}, RiskEventId={RiskEventId}, EmployeeId={EmployeeId}, By={ActorId}");

    public async Task<RiskEventDto> CreateAsync(CreateRiskEventRequestDto request, Guid userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        var (employeeId, eventType, description, severity, occurredAt) = Validate(request, now);
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        if (employeeId == caller.Id)
        {
            throw new ForbiddenException("Bạn không thể ghi nhận sự kiện rủi ro cho chính mình.");
        }

        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            throw new NotFoundException("Nhân viên", employeeId);
        }

        var riskEvent = new RiskEvent
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            RecordedBy = caller.Id,
            Code = $"RISK-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..22].ToUpperInvariant(),
            EventType = eventType,
            Description = description,
            Severity = severity,
            Status = RiskEventStatus.Open,
            OccurredAt = occurredAt,
            UpdatedAt = now,
        };
        db.RiskEvents.Add(riskEvent);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw new ConflictException("Không tạo được mã sự kiện rủi ro duy nhất, vui lòng thử lại.");
        }

        LogAction(logger, "CREATE", riskEvent.Id, riskEvent.EmployeeId, caller.Id, null);
        return ToDto(riskEvent, null);
    }

    public async Task<RiskEventDto> GetByIdAsync(Guid riskEventId, CancellationToken cancellationToken = default)
    {
        var riskEvent = await db.RiskEvents.AsNoTracking().SingleOrDefaultAsync(r => r.Id == riskEventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện rủi ro", riskEventId);
        return ToDto(riskEvent, await ContributionIdAsync(riskEventId, cancellationToken));
    }

    public async Task<RiskEventConfirmationDto> ConfirmAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var riskEvent = await LoadForActionAsync(riskEventId, caller, cancellationToken);
        if (riskEvent.Status != RiskEventStatus.Open || await db.PerformanceContributions.AnyAsync(c => c.RiskEventId == riskEventId, cancellationToken))
        {
            throw InvalidState();
        }

        var period = await FindPeriodAsync(riskEvent.OccurredAt, cancellationToken);
        var evaluationId = await db.PerformanceEvaluations.AsNoTracking()
            .Where(e => e.EmployeeId == riskEvent.EmployeeId && e.EvaluationPeriodId == period.Id)
            .OrderBy(e => e.Id)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = time.GetUtcNow();
        var contribution = new PerformanceContribution
        {
            Id = Guid.NewGuid(),
            RiskEventId = riskEvent.Id,
            EmployeeId = riskEvent.EmployeeId,
            EvaluationPeriodId = period.Id,
            PerformanceEvaluationId = evaluationId,
            ComponentScore = -EvaluationService.MapRiskPenalty(riskEvent.Severity),
            UpdatedAt = now,
        };
        db.PerformanceContributions.Add(contribution);
        riskEvent.Status = RiskEventStatus.Confirmed;
        riskEvent.UpdatedAt = now;
        await SaveTransitionAsync(cancellationToken);

        LogAction(logger, "CONFIRM", riskEvent.Id, riskEvent.EmployeeId, caller.Id, null);
        return new RiskEventConfirmationDto(
            riskEvent.Id,
            riskEvent.Code,
            riskEvent.EmployeeId,
            riskEvent.Severity.ToEnumString(),
            riskEvent.Status.ToEnumString(),
            contribution.Id,
            period.Id,
            evaluationId,
            contribution.ComponentScore!.Value,
            now);
    }

    public async Task<RiskEventDto> DismissAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var riskEvent = await LoadForActionAsync(riskEventId, caller, cancellationToken);
        if (riskEvent.Status != RiskEventStatus.Open)
        {
            throw InvalidState();
        }

        riskEvent.Status = RiskEventStatus.Dismissed;
        riskEvent.UpdatedAt = time.GetUtcNow();
        await SaveTransitionAsync(cancellationToken);

        LogAction(logger, "DISMISS", riskEvent.Id, riskEvent.EmployeeId, caller.Id, null);
        return ToDto(riskEvent, null);
    }

    public async Task<RiskEventDto> ResolveAsync(Guid riskEventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var riskEvent = await LoadForActionAsync(riskEventId, caller, cancellationToken);
        if (riskEvent.Status != RiskEventStatus.Confirmed)
        {
            throw InvalidState();
        }

        var now = time.GetUtcNow();
        riskEvent.Status = RiskEventStatus.Resolved;
        riskEvent.ResolvedAt = now;
        riskEvent.UpdatedAt = now;
        await SaveTransitionAsync(cancellationToken);

        LogAction(logger, "RESOLVE", riskEvent.Id, riskEvent.EmployeeId, caller.Id, null);
        return ToDto(riskEvent, await ContributionIdAsync(riskEventId, cancellationToken));
    }

    private static (Guid EmployeeId, string EventType, string Description, RiskEventSeverity Severity, DateTimeOffset OccurredAt) Validate(
        CreateRiskEventRequestDto request,
        DateTimeOffset now)
    {
        var errors = new List<string>();
        var eventType = request.EventType?.Trim();
        var description = request.Description?.Trim();
        if (request.EmployeeId is null || request.EmployeeId == Guid.Empty)
        {
            errors.Add("Nhân viên bị ghi nhận (employeeId) là bắt buộc.");
        }

        if (string.IsNullOrEmpty(eventType) || eventType.Length > MaxEventTypeLength)
        {
            errors.Add($"Loại sự kiện (eventType) là bắt buộc, tối đa {MaxEventTypeLength} ký tự.");
        }

        if (string.IsNullOrEmpty(description) || description.Length > MaxDescriptionLength)
        {
            errors.Add($"Mô tả (description) là bắt buộc, tối đa {MaxDescriptionLength} ký tự.");
        }

        if (request.OccurredAt is null)
        {
            errors.Add("Thời điểm xảy ra (occurredAt) là bắt buộc.");
        }
        else if (request.OccurredAt > now)
        {
            errors.Add("Thời điểm xảy ra (occurredAt) không được ở tương lai.");
        }

        var severity = EnumApiFormat.ParseEnum<RiskEventSeverity>(request.Severity, "severity");
        if (severity is null)
        {
            errors.Add("Mức độ (severity) là bắt buộc: LOW, MEDIUM, HIGH hoặc CRITICAL.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (request.EmployeeId!.Value, eventType!, description!, severity!.Value, request.OccurredAt!.Value);
    }

    private async Task<Employee> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
        if (caller is null || caller.Status != EmployeeStatus.Active || caller.Account is { IsLocked: true })
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return caller;
    }

    /// <summary>Loads the tracked event for a state change; a reviewer may not act on an event about themselves.</summary>
    private async Task<RiskEvent> LoadForActionAsync(Guid riskEventId, Employee caller, CancellationToken cancellationToken)
    {
        var riskEvent = await db.RiskEvents.SingleOrDefaultAsync(r => r.Id == riskEventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện rủi ro", riskEventId);
        if (riskEvent.EmployeeId == caller.Id)
        {
            throw new ForbiddenException("Bạn không thể xử lý sự kiện rủi ro của chính mình.");
        }

        return riskEvent;
    }

    /// <summary>Saves a status change; the unique contribution index turns a concurrent duplicate into a conflict.</summary>
    private async Task SaveTransitionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            throw InvalidState();
        }
    }

    private Task<Guid?> ContributionIdAsync(Guid riskEventId, CancellationToken cancellationToken) =>
        db.PerformanceContributions.AsNoTracking()
            .Where(c => c.RiskEventId == riskEventId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The period containing the event date (UTC+7 calendar day); the latest start wins on overlap.</summary>
    private async Task<EvaluationPeriod> FindPeriodAsync(DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(occurredAt.ToOffset(TimeProviderExtensions.BusinessUtcOffset).DateTime);
        var period = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.StartDate <= date && p.EndDate >= date)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.EndDate).ThenBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (period is null || period.Status is PeriodStatus.Locked or PeriodStatus.Closed)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.RiskEventPeriodUnavailable),
                ErrorCodes.RiskEventPeriodUnavailable);
        }

        return period;
    }

    private static RiskEventDto ToDto(RiskEvent e, Guid? contributionId) => new(
        e.Id,
        e.Code,
        e.EmployeeId,
        e.RecordedBy,
        e.EventType,
        e.Description,
        e.Severity.ToEnumString(),
        e.Status.ToEnumString(),
        e.OccurredAt,
        e.ResolvedAt,
        e.UpdatedAt,
        contributionId);

    private static ConflictException InvalidState() =>
        new(ErrorCodes.GetDefaultMessage(ErrorCodes.RiskEventInvalidState), ErrorCodes.RiskEventInvalidState);
}
