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
    int? AchievedCriteria,
    int? TotalCriteria,
    RankingBoardDeltaDto RiskEventCount,
    RankingBoardDeltaDto RiskPenalty,
    RankingBoardDeltaDto QuantitativeScore,
    string? SystemTier,
    string? FinalTier,
    string? AdjustmentReason,
    bool IsConfirmed);

public sealed record RankingBoardOverviewDto(
    Guid DepartmentId,
    string DepartmentName,
    Guid PeriodId,
    string PeriodCode,
    string PeriodName,
    Guid? ComparePeriodId,
    string? ComparePeriodCode,
    int RankingLockDay,
    bool IsLocked,
    bool CanSendToDirector,
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
