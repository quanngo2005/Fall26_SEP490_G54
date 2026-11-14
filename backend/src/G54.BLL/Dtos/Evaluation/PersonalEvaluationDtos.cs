using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Evaluation;

public sealed record PersonalEvaluationCriteriaItemDto(
    Guid Id,
    string Name,
    decimal WeightPercent,
    string ActualTargetDisplay,
    decimal? Score,
    decimal? WeightedScore,
    bool HasDrilldown,
    Guid? KpiDefinitionId = null,
    string TargetDisplay = "—",
    string ActualDisplay = "—");

public sealed record PersonalEvaluationViewDto(
    Guid PeriodId,
    string PeriodName,
    string PeriodCode,
    string Status,
    string AppealDeadlineText,
    int AppealRemainingDays,
    bool CanAppeal,
    decimal? TotalPeScore,
    decimal? ScoreDelta,
    string? PreviousPeriodName,
    decimal? PreviousPeriodScore,
    string? Tier,
    IReadOnlyList<string> AllTiers,
    string RankingPercentile,
    int RankingPosition,
    int RankingTotalEmployees,
    string RankingRoleTitle,
    IReadOnlyList<PersonalEvaluationCriteriaItemDto> Criteria,
    decimal? QualityRatio,
    decimal RiskPenalty,
    string? ManagerFeedback,
    bool IsAcknowledged,
    DateTimeOffset? AcknowledgedAt);

public sealed record AcknowledgePersonalEvaluationRequestDto(
    [Required] Guid PeriodId);

public sealed record AcknowledgePersonalEvaluationResultDto(
    bool Success,
    DateTimeOffset AcknowledgedAt,
    string Message);

public sealed record PersonalCriteriaLedgerItemDto(
    Guid Id,
    string TxCode,
    string Date,
    string Amount,
    decimal KpiContribution,
    string Status,
    string OccurredOn,
    Guid? WorkId = null,
    string? WorkCode = null,
    string? WorkTitle = null);

