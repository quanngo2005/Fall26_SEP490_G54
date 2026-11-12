namespace G54.BLL.Dtos.Quality;

public sealed record QaDashboardFilterDto(
    Guid? PeriodId = null,
    Guid? DepartmentId = null,
    Guid? JobPositionId = null,
    string? JobPositionCode = null);

public sealed record QaDashboardPeriodDto(
    Guid Id,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset LockDeadline,
    bool IsLocked);

public sealed record QaDashboardBannerDto(
    int InReviewCount,
    string Message,
    string Warning,
    bool HasUnmatchedInReview);

public sealed record QaDashboardNotMatchedCardDto(
    int Count,
    int? OldestDays,
    string Subtext);

public sealed record QaDashboardInReviewCardDto(
    int Count,
    string Subtext);

public sealed record QaDashboardQaQueueCardDto(
    int Total,
    int SampleCount,
    int FlagCount,
    string Subtext);

public sealed record QaDashboardRiskEventsCardDto(
    int Count,
    decimal TotalPenaltyPoints,
    string Subtext);

public sealed record QaDashboardCardsDto(
    QaDashboardNotMatchedCardDto NotMatched,
    QaDashboardInReviewCardDto InReview,
    QaDashboardQaQueueCardDto QaQueue,
    QaDashboardRiskEventsCardDto RiskEvents);

public sealed record QaDashboardWeeklyRateDto(
    string WeekLabel,
    int WeekNumber,
    int Year,
    DateOnly StartDate,
    DateOnly EndDate,
    int MatchedCount,
    int TotalCount,
    decimal? MatchRate);

public sealed record QaDashboardMatchedRateChartDto(
    string Title,
    string Subtitle,
    decimal? CurrentRate,
    string CurrentWeekLabel,
    IReadOnlyList<QaDashboardWeeklyRateDto> Weeks);

public sealed record QaDashboardTopEmployeeDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobTitle,
    string? DepartmentName,
    int DiscrepancyCount,
    int FlagCount);

public sealed record QaDashboardReasonCountDto(
    string ReasonCode,
    string Reason,
    int Count);

public sealed record QaDashboardDto(
    DateTimeOffset AsOf,
    QaDashboardPeriodDto Period,
    QaDashboardBannerDto Banner,
    QaDashboardCardsDto KpiCards,
    QaDashboardMatchedRateChartDto MatchedRateChart,
    IReadOnlyList<QaDashboardTopEmployeeDto> TopDiscrepancyEmployees,
    IReadOnlyList<QaDashboardReasonCountDto> CommonDiscrepancyReasons);

public sealed record QaReconciliationFilterDto(
    Guid? PeriodId = null,
    Guid? DepartmentId = null,
    Guid? JobPositionId = null,
    string? Status = null,
    string? Reason = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);

public sealed record QaReconciliationItemDto(
    Guid Id,
    string TransactionCode,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? JobTitle,
    string? DepartmentName,
    string Status,
    string? Reason,
    string? ReasonDescription,
    string? ProductCode,
    decimal? Amount,
    DateOnly? TransactionDate,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record QaDashboardPeriodOptionDto(
    Guid Id,
    string Code,
    string Name,
    bool IsCurrent);

public sealed record QaDashboardDepartmentOptionDto(
    Guid Id,
    string Code,
    string Name);

public sealed record QaDashboardJobPositionOptionDto(
    Guid Id,
    string Code,
    string Title);

public sealed record QaDashboardFilterOptionsDto(
    IReadOnlyList<QaDashboardPeriodOptionDto> Periods,
    IReadOnlyList<QaDashboardDepartmentOptionDto> Departments,
    IReadOnlyList<QaDashboardJobPositionOptionDto> JobPositions);

