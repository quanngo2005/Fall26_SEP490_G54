namespace G54.BLL.Dtos.Evaluation;

public sealed record DepartmentEvaluationDepartmentDto(
    Guid Id,
    string Code,
    string Name,
    string? ManagerName);

public sealed record DepartmentEvaluationGroupKpiDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    string Direction,
    decimal TargetValue,
    decimal? ActualValue,
    decimal? AchievementPercent,
    decimal? PreviousActualValue,
    decimal? PreviousAchievementPercent,
    decimal? ChangeFromPreviousPercent,
    decimal? Weight = null,
    decimal? Score = null,
    decimal? PreviousScore = null);

public sealed record DepartmentEvaluationKpiGroupDto(
    string? Name,
    int DisplayOrder,
    decimal WeightPercent,
    decimal? AverageScore,
    IReadOnlyList<DepartmentEvaluationGroupKpiDto> Kpis);

public sealed record DepartmentEvaluationTrendPointDto(
    Guid PeriodId,
    string PeriodCode,
    string PeriodName,
    decimal? Average);

public sealed record DepartmentEvaluationPerformanceDto(
    decimal? Average,
    decimal? PreviousAverage,
    decimal? ChangeFromPrevious,
    int EvaluatedEmployeeCount,
    int TotalEmployeeCount,
    int UnscoredEmployeeCount,
    int PipCount,
    int AppealCount,
    IReadOnlyList<DepartmentEvaluationKpiGroupDto> KpiGroups,
    IReadOnlyList<DepartmentEvaluationTrendPointDto> Trend);

public sealed record DepartmentEvaluationOperationsDto(
    int OnTimeCompletedCount,
    int OverdueCount,
    int InProgressCount,
    decimal? OnTimeRatePercent,
    decimal? LoadPerStaff);

public sealed record DepartmentEvaluationTierItemDto(
    string TierCode,
    int RankOrder,
    decimal MinScore,
    decimal? MaxScore,
    int Count,
    decimal Percent);

public sealed record DepartmentEvaluationActionAlertDto(
    string Kind,
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string? TierCode,
    decimal? TotalScore,
    string? MetaText);

public sealed record DepartmentEvaluationTierDistributionDto(
    decimal TierACapPercent,
    IReadOnlyList<DepartmentEvaluationTierItemDto> Tiers,
    IReadOnlyList<DepartmentEvaluationActionAlertDto> ActionAlerts);

public sealed record DepartmentEvaluationProfileGroupColumnDto(
    string Name,
    int DisplayOrder,
    decimal WeightPercent);

public sealed record DepartmentEvaluationStaffGroupScoreDto(
    string GroupName,
    int DisplayOrder,
    decimal WeightPercent,
    decimal? Score,
    decimal? PreviousScore = null);

public sealed record DepartmentEvaluationStaffRowDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    Guid JobPositionId,
    string JobPositionTitle,
    IReadOnlyList<DepartmentEvaluationStaffGroupScoreDto> GroupScores,
    decimal? TotalScore,
    decimal? PreviousTotalScore,
    decimal? ScoreDelta,
    string? TierCode,
    decimal? OnTimeSlaPercent,
    string Status,
    string EvaluationStatus,
    bool CanOpenForm,
    int OnTimeCompletedCount = 0,
    int InProgressCount = 0,
    int OverdueCount = 0,
    decimal? PreviousOnTimeSlaPercent = null);

public sealed record DepartmentEvaluationDashboardDto(
    DepartmentEvaluationDepartmentDto Department,
    EvaluationPeriodOptionDto? Period,
    EvaluationPeriodOptionDto? ComparePeriod,
    IReadOnlyList<EvaluationPeriodOptionDto> AvailablePeriods,
    IReadOnlyList<EvaluationPeriodOptionDto> CompareOptions,
    DepartmentEvaluationPerformanceDto Performance,
    DepartmentEvaluationOperationsDto Operations,
    DepartmentEvaluationTierDistributionDto TierDistribution,
    IReadOnlyList<DepartmentEvaluationProfileGroupColumnDto> ProfileGroups,
    IReadOnlyList<DepartmentEvaluationStaffRowDto> StaffRows,
    DateTimeOffset GeneratedAt);

public sealed record DepartmentEvaluationDashboardExportDto(
    byte[] Content,
    string FileName);
