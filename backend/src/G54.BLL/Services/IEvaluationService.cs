using G54.BLL.Dtos.Evaluation;

namespace G54.BLL.Services;

public interface IEvaluationService
{
    Task<EvaluationFormDto> GetEvaluationFormAsync(
        Guid employeeId,
        Guid periodId,
        Guid currentUserId,
        Guid? comparePeriodId = null,
        CancellationToken cancellationToken = default);

    Task<EvaluationFormExportDto> ExportEvaluationFormAsync(
        Guid employeeId,
        Guid periodId,
        Guid currentUserId,
        Guid? comparePeriodId = null,
        CancellationToken cancellationToken = default);

    Task<EvaluationResultDto> SubmitEvaluationAsync(
        SubmitEvaluationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvaluationPeriodOptionDto>> GetEvaluationPeriodsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>> GetDepartmentEmployeesForEvaluationAsync(
        Guid periodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<DepartmentEvaluationDashboardDto> GetDepartmentEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<DepartmentEvaluationDashboardExportDto> ExportDepartmentEvaluationDashboardAsync(
        Guid? periodId,
        Guid? comparePeriodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CalibrationOverviewDto> GetDepartmentCalibrationAsync(
        Guid periodId,
        Guid? departmentId,
        Guid? positionId,
        Guid currentUserId,
        bool includeSubDepartments = false,
        CancellationToken cancellationToken = default);

    Task<CalibrationSubmitResultDto> SubmitDepartmentCalibrationAsync(
        SubmitCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingBoardOverviewDto> GetRankingBoardAsync(
        Guid periodId,
        Guid? departmentId,
        Guid? comparePeriodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingBoardOverviewDto> SaveRankingBoardDraftAsync(
        SaveRankingBoardRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingBoardOverviewDto> AutoAssignRankingBoardAsync(
        AutoAssignRankingBoardRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingBoardSendResultDto> SendRankingBoardToDirectorAsync(
        SaveRankingBoardRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingBoardExportDto> ExportRankingBoardAsync(
        Guid periodId,
        Guid? departmentId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<RankingCompareDto> GetRankingCompareAsync(
        Guid periodId,
        Guid employeeId1,
        Guid employeeId2,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<BranchApprovalOverviewDto> GetBranchApprovalOverviewAsync(
        Guid periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CalibrationDecisionResultDto> RejectDepartmentCalibrationAsync(
        Guid departmentId,
        RejectCalibrationRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<DepartmentApprovalResultDto> ApproveDepartmentCalibrationAsync(
        Guid departmentId,
        ApproveDepartmentRankingRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<PreCloseCheckOverviewDto> GetPreCloseCheckAsync(
        Guid periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<FinalizeResultDto> FinalizeAndLockPeriodAsync(
        FinalizeEvaluationPeriodRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<BranchEvaluationPeriodsOverviewDto> GetBranchEvaluationPeriodsOverviewAsync(
        int? year,
        string? periodType,
        string? status,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<PersonalEvaluationViewDto> GetPersonalEvaluationAsync(
        Guid? periodId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersonalCriteriaLedgerItemDto>> GetPersonalCriteriaLedgerAsync(
        Guid periodId,
        Guid criteriaId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
