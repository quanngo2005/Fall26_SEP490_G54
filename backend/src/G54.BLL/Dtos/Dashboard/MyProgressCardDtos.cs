using System;

namespace G54.BLL.Dtos.Dashboard;

/// <summary>
/// Response payload for GET /my-dashboard/progress-card (SCR-HOME-01).
/// Supports Teller &amp; Sales Officer roles with monthly &amp; quarterly calculations.
/// Contract: docs/api/MY_DASHBOARD_PROGRESS_CARD_CONTRACT.md.
/// </summary>
public sealed record MyProgressCardDto(
    string Role,
    string CardTitle,
    DashboardPeriodInfoDto Period,
    decimal CurrentRate,
    string PacingStatus,
    string PacingStatusLabel,
    decimal? ExpectedRateToday,
    string? ExpectedRateTodayText,
    string? PolicyNote,
    PeriodComparisonDto? PreviousPeriodComparison,
    ProgressWorkingDaysDto? WorkingDays,
    string ActionUrl);

public sealed record DashboardPeriodInfoDto(
    Guid Id,
    string Code,
    string Name,
    string PeriodType,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record PeriodComparisonDto(
    string ComparisonType,
    string Direction,
    decimal Delta,
    string ComparedPeriodName,
    string Text);

public sealed record ProgressWorkingDaysDto(
    int TotalWorkingDays,
    int ElapsedWorkingDays);

public sealed record MyProgressCardQueryDto(
    string? PeriodType = null,
    Guid? PeriodId = null);

