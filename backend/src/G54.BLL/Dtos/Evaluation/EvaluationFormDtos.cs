using G54.DAL.Entities;

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
/// by the weights of its parent groups, so the leaves of a group add up to 100.
/// </summary>
public sealed record EvaluationKpiItemDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit,
    decimal WeightPercent,
    decimal? TargetValue,
    decimal ActualValue,
    decimal AchievementPercent,
    decimal ComponentScore);

/// <summary>
/// A root group of the evaluation profile tree. EvaluationProfileItemId is the tree node and the key of its
/// qualitative score; GroupCode is the code of the catalog KPI group of its KPIs.
/// </summary>
public sealed record EvaluationCriterionGroupDto(
    Guid EvaluationProfileItemId,
    string GroupCode,
    string GroupName,
    decimal WeightPercent,
    decimal GroupScore,
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
    decimal BusinessScore,
    decimal ServiceQualityScore,
    decimal OperationsScore,
    decimal TotalRiskPenalty,
    decimal TotalScore,
    string ProposedTier,
    decimal WeightedScore);

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
    bool CoachingRequired);

public sealed record PreviousPeriodSummaryDto(
    string PeriodCode,
    decimal TotalScore,
    string Tier,
    decimal ScoreDifference);

public sealed record EvaluationFormDto(
    EvaluationEmployeeDto Employee,
    EvaluationPeriodDto Period,
    EvaluationProfileSummaryDto Profile,
    List<EvaluationCriterionGroupDto> CriteriaGroups,
    List<EvaluationRiskEventDto> RiskEvents,
    EvaluationCalculationSummaryDto Summary,
    PerformanceEvaluationStateDto CurrentEvaluation,
    bool CanEdit,
    int PendingSubmissionCount,
    int OpenAppealCount,
    bool HasOpenDispute,
    PreviousPeriodSummaryDto? PreviousPeriod);

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
    bool IsDraft = false);

public sealed record EvaluationResultDto(
    Guid EvaluationId,
    Guid EmployeeId,
    Guid PeriodId,
    decimal TotalScore,
    string Status,
    string ProposedTier,
    DateTimeOffset CalculatedAt,
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
    DateTimeOffset? UpdatedAt);
