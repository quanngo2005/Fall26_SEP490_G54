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

public sealed record CalibrationEmployeeRowDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    Guid JobPositionId,
    string JobPositionTitle,
    Guid DepartmentId,
    string DepartmentName,
    decimal BusinessScore,
    decimal ServiceQualityScore,
    decimal OperationsScore,
    decimal QualitativeScore,
    decimal TotalScore,
    int Rank,
    string ProposedTier,
    string CalibratedTier,
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
