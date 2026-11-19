namespace G54.BLL.Dtos.Evaluation;

/// <summary>One tier of the board: the configured headcount (null when not configured) and the employees placed in it.</summary>
public sealed record RankingBoardTierDto(
    string TierCode,
    int? Headcount,
    int ActualCount,
    bool IsFilled);

/// <summary>A metric in the selected period and in the compare period, with the change between them (null when either is missing).</summary>
public sealed record RankingBoardDeltaDto(
    decimal? Value,
    decimal? PreviousValue,
    decimal? Change);

public sealed record RankingBoardStaffRowDto(
    int Rank,
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    RankingBoardDeltaDto OnTimeSla,
    int OnTimeCompletedCount,
    int CompletedWithDeadlineCount,
    int? AchievedCriteria,
    int? TotalCriteria,
    IReadOnlyList<RankingBoardCriterionDto> Criteria,
    RankingBoardDeltaDto RiskEventCount,
    IReadOnlyList<RankingBoardRiskEventDto> RiskEvents,
    RankingBoardDeltaDto RiskPenalty,
    RankingBoardDeltaDto QuantitativeScore,
    string? SystemTier,
    string? FinalTier,
    string? AdjustmentReason,
    bool IsConfirmed);

/// <summary>One KPI of the criteria count: its target, its actual value and whether it reached the threshold.</summary>
public sealed record RankingBoardCriterionDto(
    string KpiName,
    decimal? ActualValue,
    decimal TargetValue,
    string Unit,
    bool IsAchieved);

/// <summary>A confirmed risk event of the employee in the period.</summary>
public sealed record RankingBoardRiskEventDto(
    string Code,
    string Description,
    string Severity);

public sealed record RankingBoardOverviewDto(
    Guid DepartmentId,
    string DepartmentName,
    Guid PeriodId,
    string PeriodCode,
    string PeriodName,
    Guid? ComparePeriodId,
    string? ComparePeriodCode,
    int RankingLockDay,
    DateOnly RankingLockDate,
    bool IsLocked,
    bool CanSendToDirector,
    bool IsSentToDirector,
    IReadOnlyList<string> SendBlockers,
    IReadOnlyList<RankingBoardTierDto> Tiers,
    IReadOnlyList<RankingBoardStaffRowDto> StaffRows,
    DateTimeOffset GeneratedAt);

public sealed record RankingBoardAdjustmentItemDto(
    Guid EmployeeId,
    string FinalTier,
    string? Reason);

public sealed record SaveRankingBoardRequestDto(
    Guid DepartmentId,
    Guid PeriodId,
    IReadOnlyList<RankingBoardAdjustmentItemDto> Adjustments);

public sealed record AutoAssignRankingBoardRequestDto(
    Guid DepartmentId,
    Guid PeriodId);

public sealed record RankingBoardSendResultDto(
    Guid DepartmentId,
    Guid PeriodId,
    string Status,
    DateTimeOffset SentAt,
    string Message);

public sealed record RankingBoardExportDto(
    byte[] Content,
    string FileName);

public sealed record RankingCompareMetricDto(
    string Key,
    string Label,
    decimal? Value1,
    decimal? Value2,
    string? Winner,
    decimal? Difference,
    IReadOnlyList<RankingCompareMetricDto> Children);

public sealed record RankingCompareEmployeeDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName);

public sealed record RankingCompareDto(
    RankingCompareEmployeeDto Employee1,
    RankingCompareEmployeeDto Employee2,
    IReadOnlyList<RankingCompareMetricDto> Metrics);
