using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.RiskEvent;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class RiskEventScreenService(
    AppDbContext db,
    TimeProvider time,
    IPermissionService permissions,
    IStorageService storage,
    ILogger<RiskEventScreenService> logger) : IRiskEventScreenService
{
    public const string RecordedState = "RECORDED";
    public const string ReversedState = "REVERSED";

    private const int MaxPageSize = 100;
    private const int MinReasonLength = 10;
    private const int MaxReasonLength = 2000;
    private static readonly TimeSpan BusinessOffset = TimeSpan.FromHours(7);

    private static readonly Action<ILogger, string, Guid, Guid, Exception?> LogAction =
        LoggerMessage.Define<string, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, nameof(LogAction)),
            "Risk event audit: Action={Action}, RiskEventId={RiskEventId}, By={ActorId}");

    public async Task<RiskEventListDto> ListAsync(Guid userId, RiskEventFilterDto filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var severity = EnumApiFormat.ParseEnum<RiskEventSeverity>(filter.Severity, "severity");
        var state = ParseState(filter.State);
        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);
        var (_, scope) = await ResolveReadScopeAsync(userId, cancellationToken);

        var query = EventQuery().AsQueryable();
        if (scope is not null)
        {
            query = query.Where(e => scope.Contains(e.Employee.DepartmentId));
        }

        if (filter.DepartmentId is { } departmentId)
        {
            var subtree = (await DepartmentHierarchy.LoadAsync(db.Departments, cancellationToken)).Subtree(departmentId);
            query = query.Where(e => subtree.Contains(e.Employee.DepartmentId));
        }

        if (filter.PeriodId is { } periodId)
        {
            var period = await db.EvaluationPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, cancellationToken)
                ?? throw new NotFoundException("Kỳ đánh giá", periodId);
            // Npgsql only accepts offset 0 in a query: the Vietnamese day boundaries are converted to UTC.
            var from = new DateTimeOffset(period.StartDate.ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            var to = new DateTimeOffset(period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), BusinessOffset).ToUniversalTime();
            query = query.Where(e => e.OccurredAt >= from && e.OccurredAt < to);
        }

        if (severity is { } level)
        {
            query = query.Where(e => e.Severity == level);
        }

        var departments = await query
            .Select(e => new { e.Employee.Department.Id, e.Employee.Department.Name })
            .Distinct()
            .ToListAsync(cancellationToken);

        var items = (await query.ToListAsync(cancellationToken))
            .Select(ToItem)
            .Where(i => Matches(i, filter.Search))
            .ToList();
        var counts = new RiskEventCountsDto(
            items.Count(i => i.State == RecordedState),
            items.Count(i => i.State == ReversedState));
        var inState = Sort(items.Where(i => state is null || i.State == state), filter.SortBy, filter.SortDir);

        return new RiskEventListDto(
            inState.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            inState.Count,
            page,
            pageSize,
            counts,
            departments.OrderBy(d => d.Name, StringComparer.CurrentCulture)
                .Select(d => new RiskEventDepartmentDto(d.Id, d.Name))
                .ToList());
    }

    public async Task<RiskEventDetailsDto> GetDetailsAsync(Guid userId, Guid riskEventId, CancellationToken cancellationToken = default)
    {
        var (caller, scope) = await ResolveReadScopeAsync(userId, cancellationToken);
        var riskEvent = await EventQuery().FirstOrDefaultAsync(e => e.Id == riskEventId, cancellationToken);
        if (riskEvent is null || (scope is not null && !scope.Contains(riskEvent.Employee.DepartmentId)))
        {
            throw new NotFoundException("Sự kiện rủi ro", riskEventId);
        }

        return await BuildDetailsAsync(riskEvent, caller, cancellationToken);
    }

    public async Task<List<RiskEventAttachmentDto>> AddEvidenceAsync(
        Guid userId,
        Guid riskEventId,
        IReadOnlyCollection<IFormFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ValidationException("Hãy chọn ít nhất một tệp đính kèm.");
        }

        await ResolveCallerAsync(userId, cancellationToken);
        var riskEvent = await db.RiskEvents.FirstOrDefaultAsync(e => e.Id == riskEventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện rủi ro", riskEventId);
        if (riskEvent.Status == RiskEventStatus.Dismissed)
        {
            throw InvalidState();
        }

        var stored = await storage.UploadEvidenceFilesAsync(files, cancellationToken);
        var now = time.GetUtcNow();
        foreach (var file in stored)
        {
            db.Evidences.Add(new Evidence
            {
                Id = Guid.NewGuid(),
                RiskEventId = riskEvent.Id,
                FileName = file.FileName,
                FilePath = file.FilePath,
                MimeType = file.MimeType,
                FileSize = file.FileSize,
                UploadedAt = now,
                UpdatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteEvidenceFilesAsync(stored, CancellationToken.None);
            throw;
        }

        return await AttachmentsAsync(riskEventId, cancellationToken);
    }

    public async Task<RiskEventDetailsDto> ReverseAsync(
        Guid userId,
        Guid riskEventId,
        ReverseRiskEventDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length < MinReasonLength || reason.Length > MaxReasonLength)
        {
            throw new ValidationException($"Lý do gỡ điểm phạt là bắt buộc, từ {MinReasonLength} đến {MaxReasonLength} ký tự.");
        }

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var riskEvent = await db.RiskEvents
            .Include(e => e.Employee).ThenInclude(e => e.Department)
            .Include(e => e.RecordedByNavigation)
            .Include(e => e.Work)
            .FirstOrDefaultAsync(e => e.Id == riskEventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện rủi ro", riskEventId);
        if (riskEvent.EmployeeId == caller.Id)
        {
            throw new ForbiddenException("Bạn không thể xử lý sự kiện rủi ro của chính mình.");
        }

        if (riskEvent.Status is not (RiskEventStatus.Confirmed or RiskEventStatus.Resolved) || riskEvent.ReversedAt is not null)
        {
            throw InvalidState();
        }

        // The penalty was applied to the period of the violation date: a locked period cannot take it back.
        await RiskEventService.FindPeriodAsync(db, riskEvent.OccurredAt, cancellationToken);

        var now = time.GetUtcNow();
        riskEvent.Status = RiskEventStatus.Dismissed;
        riskEvent.ReversedAt = now;
        riskEvent.ReversedBy = caller.Id;
        riskEvent.ReversalReason = reason;
        riskEvent.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        LogAction(logger, "REVERSE", riskEvent.Id, caller.Id, null);
        var reversed = await EventQuery().FirstAsync(e => e.Id == riskEventId, cancellationToken);
        return await BuildDetailsAsync(reversed, caller, cancellationToken);
    }

    private IQueryable<RiskEvent> EventQuery() => db.RiskEvents.AsNoTracking()
        .Include(e => e.Employee).ThenInclude(e => e.Department)
        .Include(e => e.Work)
        .Include(e => e.RecordedByNavigation)
        .Include(e => e.ReversedByEmployee);

    private async Task<RiskEventDetailsDto> BuildDetailsAsync(RiskEvent riskEvent, Employee caller, CancellationToken cancellationToken)
    {
        var date = DateOnly.FromDateTime(riskEvent.OccurredAt.ToOffset(BusinessOffset).DateTime);
        var period = await db.EvaluationPeriods.AsNoTracking()
            .Where(p => p.StartDate <= date && p.EndDate >= date)
            .OrderByDescending(p => p.StartDate).ThenByDescending(p => p.EndDate).ThenBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var jobTitle = await db.Employees.AsNoTracking()
            .Where(e => e.Id == riskEvent.EmployeeId)
            .Select(e => e.JobPosition.Title)
            .FirstOrDefaultAsync(cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        var canReverse = AccessScope.LevelFor(granted, "risk_event", "reverse") >= AccessScope.AllLevel
            && riskEvent.Status is RiskEventStatus.Confirmed or RiskEventStatus.Resolved
            && riskEvent.ReversedAt is null
            && riskEvent.EmployeeId != caller.Id;

        return new RiskEventDetailsDto(
            ToItem(riskEvent),
            jobTitle,
            riskEvent.Description,
            period?.Code,
            period?.Name,
            riskEvent.RecordedByNavigation.FullName,
            riskEvent.RecordedAt,
            riskEvent.ReversedAt,
            riskEvent.ReversedByEmployee?.FullName,
            riskEvent.ReversalReason,
            await AttachmentsAsync(riskEvent.Id, cancellationToken),
            canReverse);
    }

    private async Task<List<RiskEventAttachmentDto>> AttachmentsAsync(Guid riskEventId, CancellationToken cancellationToken) =>
        (await db.Evidences.AsNoTracking()
            .Where(e => e.RiskEventId == riskEventId)
            .OrderBy(e => e.FileName)
            .ToListAsync(cancellationToken))
        .Select(e => new RiskEventAttachmentDto(e.Id, e.FileName, e.FileSize ?? 0, e.MimeType ?? "application/octet-stream"))
        .ToList();

    private static RiskEventListItemDto ToItem(RiskEvent e) => new(
        e.Id,
        Code(e.CodeNo),
        e.OccurredAt,
        e.EmployeeId,
        e.Employee.EmployeeCode,
        e.Employee.FullName,
        e.Employee.DepartmentId,
        e.Employee.Department.Name,
        e.EventType,
        e.Severity.ToEnumString(),
        EvaluationService.RiskPenalty(e),
        e.WorkId,
        e.Work?.Code,
        e.Status.ToEnumString(),
        e.Status == RiskEventStatus.Dismissed ? ReversedState : RecordedState,
        e.RecordedAt);

    private static string Code(long no) => $"RSK-{no.ToString("D4", CultureInfo.InvariantCulture)}";

    private static bool Matches(RiskEventListItemDto item, string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var digits = text.StartsWith("RSK-", StringComparison.OrdinalIgnoreCase) ? text[4..] : text;
        if (long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && string.Equals(item.Code, Code(number), StringComparison.Ordinal))
        {
            return true;
        }

        return new[] { item.Code, item.EmployeeCode, item.EmployeeName, item.WorkCode ?? string.Empty }
            .Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Dictionary<string, Func<RiskEventListItemDto, IComparable>> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = i => i.Code,
        ["occurredAt"] = i => i.OccurredAt,
        ["employee"] = i => i.EmployeeName,
        ["department"] = i => i.DepartmentName,
        ["type"] = i => i.EventType,
        ["severity"] = i => SeverityRank(i.Severity),
        ["penalty"] = i => i.PenaltyPoints,
        ["work"] = i => i.WorkCode ?? string.Empty,
        ["status"] = i => i.State,
    };

    private static int SeverityRank(string severity) => severity switch
    {
        "LOW" => 1,
        "MEDIUM" => 2,
        "HIGH" => 3,
        _ => 4,
    };

    /// <summary>Sorts by the chosen column (violation date, newest first, by default); ties keep a stable order.</summary>
    private static List<RiskEventListItemDto> Sort(IEnumerable<RiskEventListItemDto> items, string? sortBy, string? sortDir)
    {
        var key = string.IsNullOrWhiteSpace(sortBy) ? "occurredAt" : sortBy.Trim();
        if (!SortKeys.TryGetValue(key, out var selector))
        {
            throw new ValidationException("Cột sắp xếp không hợp lệ.");
        }

        var descending = string.IsNullOrWhiteSpace(sortDir)
            ? key.Equals("occurredAt", StringComparison.OrdinalIgnoreCase) || key.Equals("penalty", StringComparison.OrdinalIgnoreCase)
            : sortDir.Trim().ToUpperInvariant() switch
            {
                "DESC" => true,
                "ASC" => false,
                _ => throw new ValidationException("Chiều sắp xếp phải là asc hoặc desc."),
            };
        var ordered = descending ? items.OrderByDescending(selector) : items.OrderBy(selector);
        return ordered.ThenByDescending(i => i.OccurredAt).ThenBy(i => i.Id).ToList();
    }

    private static string? ParseState(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Trim().ToUpperInvariant() is var s && s is RecordedState or ReversedState
            ? s
            : throw new ValidationException("Trạng thái phải là RECORDED hoặc REVERSED.");

    /// <summary>The caller, and the departments their <c>risk_event:read</c> scope covers (null = every department).</summary>
    private async Task<(Employee Caller, HashSet<Guid>? Departments)> ResolveReadScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var granted = await permissions.GetEffectivePermissionsAsync(caller.Account!.Id, cancellationToken);
        var level = AccessScope.LevelFor(granted, "risk_event", "read");
        if (level <= 0)
        {
            throw new ForbiddenException();
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(db.Departments.AsNoTracking(), cancellationToken);
        return (caller, AccessScope.DepartmentsInScope(level, caller.DepartmentId, hierarchy));
    }

    private async Task<Employee> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var caller = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
        if (caller?.Account is null || caller.Status != EmployeeStatus.Active || caller.Account.IsLocked)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return caller;
    }

    private static ConflictException InvalidState() =>
        new(ErrorCodes.GetDefaultMessage(ErrorCodes.RiskEventInvalidState), ErrorCodes.RiskEventInvalidState);
}
