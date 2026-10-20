using G54.BLL.Dtos.Evaluation;

namespace G54.BLL.Services;

public interface IEvaluationService
{
    Task<EvaluationFormDto> GetEvaluationFormAsync(
        Guid employeeId,
        Guid periodId,
        Guid currentUserId,
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

    Task<CalibrationOverviewDto> GetDepartmentCalibrationAsync(
        Guid periodId,
        Guid? departmentId,
        Guid? positionId,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CalibrationSubmitResultDto> SubmitDepartmentCalibrationAsync(
        SubmitCalibrationRequestDto request,
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

    Task<FinalizeResultDto> FinalizeAndLockPeriodAsync(
        FinalizeEvaluationPeriodRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
