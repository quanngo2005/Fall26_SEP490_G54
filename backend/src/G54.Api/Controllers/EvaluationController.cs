using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Controller for Employee Performance Evaluation Form (SCR-EVL-01 - UC103, UC105).
/// Enforces Maker != Checker (BR-02), quantitative ledger auto-sync (BR-05), and role weight PE score (BR-06, BR-44a).
/// </summary>
[ApiController]
[Route("evaluations")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class EvaluationController(IEvaluationService service) : ControllerBase
{
    /// <summary>
    /// Gets the evaluation form pre-populated with employee details, criteria structure, and quantitative ledger actuals.
    /// </summary>
    [HttpGet("form")]
    [ProducesResponseType<ApiResponse<EvaluationFormDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EvaluationFormDto>>> GetEvaluationForm(
        [FromQuery] Guid employeeId,
        [FromQuery] Guid periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetEvaluationFormAsync(employeeId, periodId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Submits qualitative scores, comments, and coaching plans, calculating and saving the employee's total performance score.
    /// </summary>
    [HttpPost("submit")]
    [ProducesResponseType<ApiResponse<EvaluationResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EvaluationResultDto>>> SubmitEvaluation(
        [FromBody] SubmitEvaluationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitEvaluationAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets available evaluation periods for selection in the evaluation workspace.
    /// </summary>
    [HttpGet("periods")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EvaluationPeriodOptionDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EvaluationPeriodOptionDto>>>> GetPeriods(
        CancellationToken cancellationToken)
    {
        var result = await service.GetEvaluationPeriodsAsync(cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Lists all employees in the department along with their evaluation status for a given period.
    /// </summary>
    [HttpGet("department-employees")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>>>> GetDepartmentEmployees(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentEmployeesForEvaluationAsync(
            periodId,
            departmentId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets the department ranking calibration workspace overview including ranked roster, Gaussian Bell curve, and quota tracking (SCR-EVL-02 - UC104).
    /// </summary>
    [HttpGet("calibration")]
    [ProducesResponseType<ApiResponse<CalibrationOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CalibrationOverviewDto>>> GetCalibration(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? positionId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentCalibrationAsync(
            periodId,
            departmentId,
            positionId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Saves draft adjustments or submits calibrated employee rankings to Executive with Tier A quota enforcement (SCR-EVL-02 - UC104, BR-37).
    /// </summary>
    [HttpPost("calibration/submit")]
    [ProducesResponseType<ApiResponse<CalibrationSubmitResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CalibrationSubmitResultDto>>> SubmitCalibration(
        [FromBody] SubmitCalibrationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitDepartmentCalibrationAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets the executive evaluation and ranking approval workspace overview for the entire branch (SCR-EVL-03 - UC106).
    /// </summary>
    [HttpGet("approvals")]
    [ProducesResponseType<ApiResponse<BranchApprovalOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BranchApprovalOverviewDto>>> GetApprovals(
        [FromQuery] Guid periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetBranchApprovalOverviewAsync(periodId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Requests re-calibration by rejecting a department's calibration proposal with mandatory feedback (SCR-EVL-03 - UC106 Alt).
    /// </summary>
    [HttpPost("approvals/{departmentId:guid}/recalibrate")]
    [ProducesResponseType<ApiResponse<CalibrationDecisionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CalibrationDecisionResultDto>>> RequestRecalibration(
        [FromRoute] Guid departmentId,
        [FromBody] RejectCalibrationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.RejectDepartmentCalibrationAsync(
            departmentId,
            request,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Executive sign-off and hard-lock of evaluation period (SCR-EVL-03 - UC106, UC107, BR-29).
    /// </summary>
    [HttpPost("approvals/finalize")]
    [ProducesResponseType<ApiResponse<FinalizeResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<FinalizeResultDto>>> FinalizePeriod(
        [FromBody] FinalizeEvaluationPeriodRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.FinalizeAndLockPeriodAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
