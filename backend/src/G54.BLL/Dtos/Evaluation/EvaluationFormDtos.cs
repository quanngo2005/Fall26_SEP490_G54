namespace G54.BLL.Dtos.Evaluation;

public sealed record EvaluationEmployeeDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string DepartmentName,
    Guid DepartmentId,
    string JobPositionTitle,
    Guid JobPositionId);

public sealed record EvaluationPeriodDto(
    Guid Id,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string PeriodType,
    string Status);

public sealed record EvaluationProfileSummaryDto(
    Guid Id,
    string Code,
    string Name,
    int Version);

/// <summary>
/// One KPI leaf of a group. WeightPercent is its share of the root group: its weight among its siblings multiplied
/// by the weights of its parent groups, so the active leaves of a group add up to 100. A KPI without a target or without
/// progress has no actual, achievement or score to show (null, shown as N/A); in the group score it counts as 0 and keeps its share.
/// </summary>
public sealed record EvaluationKpiItemDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit,
    decimal WeightPercent,
    decimal? TargetValue,
    decimal? ActualValue,
    decimal? AchievementPercent,
    decimal? ComponentScore);

/// <summary>
/// A root group of the evaluation profile tree. EvaluationProfileItemId is the tree node and the key of its
/// qualitative score; GroupCode is the code of the catalog KPI group of its KPIs. GroupScore is null only when the
/// calculator has no score for the group (it scores every root group, so not in practice).
/// </summary>
public sealed record EvaluationCriterionGroupDto(
    Guid EvaluationProfileItemId,
    string GroupCode,
    string GroupName,
    decimal WeightPercent,
    decimal? GroupScore,
    List<EvaluationKpiItemDto> KpiItems,
    decimal? QualitativeScore = null,
    string? QualitativeNotes = null,
    bool IsQualitative = false);

public sealed record EvaluationRiskEventDto(
    Guid Id,
    string Code,
    string EventType,
    string Description,
    string Severity,
    decimal PenaltyPoints,
    DateTimeOffset OccurredAt);

public sealed record EvaluationCalculationSummaryDto(
    decimal? BusinessScore,
    decimal? ServiceQualityScore,
    decimal? OperationsScore,
    decimal TotalRiskPenalty,
    decimal? TotalScore,
    decimal? WeightedScore);

public sealed record PerformanceEvaluationStateDto(
    Guid? Id,
    decimal? TotalScore,
    string Status,
    Guid? ReviewerEmployeeId,
    string? ReviewerName,
    DateTimeOffset? CalculatedAt,
    DateTimeOffset? ApprovedAt,
    string? Comment,
    string? CoachingPlan,
    bool CoachingRequired,
    decimal? GeneralQualitativeScore = null,
    string? FinalTierCode = null);

public sealed record PreviousPeriodSummaryDto(
    string PeriodCode,
    decimal TotalScore,
    string Tier,
    decimal? ScoreDifference);

public sealed record EvaluationCriteriaSummaryDto(
    int AchievedCount,
    int NearCount,
    int BelowCount,
    int TotalCount,
    decimal? AchievedRatePercent);

public sealed record EvaluationSlaSummaryDto(
    int OnTimeCompletedCount,
    int OverdueCount,
    int InProgressCount,
    decimal? OnTimeRatePercent,
    int CompletedWithDeadlineCount = 0);

public sealed record EvaluationFormSettingsDto(
    int MaxCommentLength,
    decimal KpiMetThresholdPercent,
    decimal KpiNearThresholdPercent);

public sealed record EvaluationFormDto(
    EvaluationEmployeeDto Employee,
    EvaluationPeriodDto Period,
    EvaluationProfileSummaryDto Profile,
    List<EvaluationCriterionGroupDto> CriteriaGroups,
    List<EvaluationRiskEventDto> RiskEvents,
    EvaluationCalculationSummaryDto Summary,
    PerformanceEvaluationStateDto CurrentEvaluation,
    bool CanEdit,
    bool CanComplete,
    int PendingSubmissionCount,
    int OpenAppealCount,
    bool HasOpenDispute,
    PreviousPeriodSummaryDto? PreviousPeriod,
    EvaluationCriteriaSummaryDto? CriteriaSummary = null,
    EvaluationSlaSummaryDto? SlaSummary = null,
    EvaluationFormSettingsDto? FormSettings = null,
    EvaluationPeriodOptionDto? ComparePeriod = null,
    IReadOnlyList<EvaluationPeriodOptionDto>? CompareOptions = null);

public sealed record QualitativeScoreInputDto(
    Guid EvaluationProfileItemId,
    decimal Score,
    string? Notes = null);

public sealed record SubmitEvaluationRequestDto(
    Guid EmployeeId,
    Guid PeriodId,
    string? Comment = null,
    string? CoachingPlan = null,
    bool CoachingRequired = false,
    List<QualitativeScoreInputDto>? QualitativeScores = null,
    bool IsDraft = false,
    decimal? GeneralQualitativeScore = null);

public sealed record EvaluationResultDto(
    Guid EvaluationId,
    Guid EmployeeId,
    Guid PeriodId,
    decimal? TotalScore,
    string Status,
    string? ProposedTier,
    DateTimeOffset? CalculatedAt,
    string Message);

public sealed record EvaluationPeriodOptionDto(
    Guid Id,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string PeriodType,
    string Status,
    bool IsActive);

public sealed record DepartmentEmployeeEvaluationSummaryDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobPositionTitle,
    Guid JobPositionId,
    Guid DepartmentId,
    string DepartmentName,
    Guid? EvaluationId,
    decimal? TotalScore,
    string EvaluationStatus,
    string? ProposedTier,
    DateTimeOffset? CalculatedAt,
    decimal? QuantitativeScore,
    decimal? QualitativeScore,
    bool HasOpenDispute,
    int PendingSubmissionCount,
    bool CanEvaluate,
    DateTimeOffset? UpdatedAt,
    decimal? QuantitativeScoreChange,
    IReadOnlyList<string>? ProposedTierExplanation = null);

public sealed record EvaluationFormExportDto(
    byte[] Content,
    string FileName);
