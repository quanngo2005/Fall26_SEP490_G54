using G54.DAL.Entities;

namespace G54.BLL.Dtos.Evaluation;

// =========================================================================
// Phase 2: Department Ranking Calibration Workspace (SCR-EVL-02 - UC104)
// =========================================================================

public sealed record CalibrationDepartmentDto(
    Guid Id,
    string Code,
    string Name,
    string? ManagerName);

public sealed record CalibrationQuotaDto(
    int TotalEmployees,
    int MaxTierAQuota,
    int ProposedTierACount,
    int CalibratedTierACount,
    decimal CalibratedTierAPercent,
    int CalibratedTierBCount,
    decimal CalibratedTierBPercent,
    int CalibratedTierCCount,
    decimal CalibratedTierCPercent,
    int CalibratedTierDCount,
    decimal CalibratedTierDPercent,
    bool IsCompliant,
    string ComplianceNote);

public sealed record GaussianBellCurvePointDto(
    int X,
    decimal Y);

public sealed record GaussianDistributionDto(
    decimal MeanScore,
    decimal StandardDeviation,
    List<GaussianBellCurvePointDto> Points,
    decimal TargetTierAPercent,
    decimal TargetTierBPercent,
    decimal TargetTierCPercent,
    decimal TargetTierDPercent);

/// <summary>
/// One employee of the calibration roster. The scores, the rank and the tiers are null when the score cannot be
/// determined (no evaluation profile, or a profile without a root group): such an employee is not ranked and has no tier.
/// </summary>
public sealed record CalibrationEmployeeRowDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    Guid JobPositionId,
    string JobPositionTitle,
    Guid DepartmentId,
    string DepartmentName,
    decimal? BusinessScore,
    decimal? ServiceQualityScore,
    decimal? OperationsScore,
    decimal? QualitativeScore,
    decimal? TotalScore,
    int? Rank,
    string? ProposedTier,
    string? CalibratedTier,
    bool IsAdjusted,
    string? AdjustmentReason,
    bool CoachingFlag,
    string? DisciplinaryFlag,
    string EvaluationStatus,
    Guid? EvaluationId,
    DateTimeOffset? CalculatedAt);

public sealed record CalibrationOverviewDto(
    CalibrationDepartmentDto Department,
    EvaluationPeriodDto Period,
    CalibrationQuotaDto Quota,
    GaussianDistributionDto BellCurve,
    List<CalibrationEmployeeRowDto> RankedRoster,
    bool CanSubmit,
    string? OverallStatus,
    int DisputedPendingCount = 0);

public sealed record CalibrationAdjustmentItemDto(
    Guid EmployeeId,
    string CalibratedTier,
    string? Reason = null,
    bool CoachingFlag = false);

public sealed record SubmitCalibrationRequestDto(
    Guid DepartmentId,
    Guid PeriodId,
    List<CalibrationAdjustmentItemDto> Adjustments,
    string? Notes = null,
    bool IsDraft = false);

public sealed record CalibrationSubmitResultDto(
    Guid DepartmentId,
    Guid PeriodId,
    string Status,
    int TotalAdjusted,
    int TierACount,
    bool IsQuotaCompliant,
    DateTimeOffset SubmittedAt,
    string Message);

// =========================================================================
// Phase 3: Executive Approval & Hard-Lock (SCR-EVL-03 - UC106, UC107)
// =========================================================================

public sealed record DepartmentApprovalSummaryDto(
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    string? ManagerName,
    int TotalEmployees,
    int EvaluatedEmployees,
    string Status,
    decimal AverageScore,
    int TierACount,
    decimal TierAPercent,
    int TierBCount,
    int TierCCount,
    int TierDCount,
    bool IsQuotaCompliant,
    string? ReviewerComment,
    DateTimeOffset? SubmittedAt);

public sealed record BranchApprovalOverviewDto(
    EvaluationPeriodDto Period,
    int TotalEmployees,
    int TotalEvaluated,
    int TotalTierACount,
    decimal BranchTierAPercent,
    decimal BranchCapPercent,
    bool IsBranchCapCompliant,
    bool CanFinalize,
    List<DepartmentApprovalSummaryDto> Departments,
    int DisputedPendingCount = 0);

public sealed record RejectCalibrationRequestDto(
    Guid PeriodId,
    string Reason);

public sealed record CalibrationDecisionResultDto(
    Guid DepartmentId,
    Guid PeriodId,
    string Status,
    string Message);

public sealed record FinalizeEvaluationPeriodRequestDto(
    Guid PeriodId,
    string? SignatureToken = null,
    string? ApprovalComments = null);

public sealed record FinalizeResultDto(
    Guid PeriodId,
    string PeriodStatus,
    DateTimeOffset FinalizedAt,
    int TotalEvaluationsFinalized,
    string Message);

public sealed record AppealWindowDto(
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool IsExpired,
    bool IsClosed);

public sealed record BranchEvaluationPeriodSummaryDto(
    Guid Id,
    string Code,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    string PeriodType,
    string Status,
    string DisplayStatus,
    decimal? QualityRatio,
    int SubmittedDepartmentCount,
    int ApprovedDepartmentCount,
    int TotalDepartmentCount,
    AppealWindowDto? AppealWindow,
    bool CanOpen,
    bool CanLock,
    bool CanViewHistory);

public sealed record BranchEvaluationPeriodsOverviewDto(
    string BranchName,
    string BranchCode,
    List<int> AvailableYears,
    List<BranchEvaluationPeriodSummaryDto> Periods);

// =========================================================================
// Department Ranking Approval & Pre-close Check (SCR-EVL-03-D1, SCR-EVL-03-M1)
// =========================================================================

public sealed record ApproveDepartmentRankingRequestDto(
    Guid PeriodId,
    string? Note = null);

public sealed record DepartmentApprovalResultDto(
    Guid DepartmentId,
    Guid PeriodId,
    string Status,
    int ApprovedEvaluationsCount,
    string Message);

public sealed record PreCloseCheckItemDto(
    string Code,
    string Title,
    int RemainingCount,
    bool IsPassed,
    string? TargetScreenCode = null,
    string? TargetRoute = null);

public sealed record PreCloseCheckOverviewDto(
    Guid PeriodId,
    string PeriodCode,
    string PeriodName,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset? PreCloseDeadline,
    bool CanPublish,
    int TotalUnresolvedCount,
    List<PreCloseCheckItemDto> Items);
