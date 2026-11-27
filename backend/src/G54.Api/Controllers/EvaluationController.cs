using G54.Api.Authorization;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Dtos.SystemParameter;
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
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Gets the evaluation form pre-populated with employee details, criteria structure, and quantitative ledger actuals.
    /// </summary>
    [HttpGet("form")]
    [ProducesResponseType<ApiResponse<EvaluationFormDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Own")]
    public async Task<ActionResult<ApiResponse<EvaluationFormDto>>> GetEvaluationForm(
        [FromQuery] Guid employeeId,
        [FromQuery] Guid periodId,
        [FromQuery] Guid? comparePeriodId,
        CancellationToken cancellationToken)
    {
        RequireIds(("employeeId", employeeId), ("periodId", periodId));
        var result = await service.GetEvaluationFormAsync(employeeId, periodId, User.GetRequiredUserId(), comparePeriodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Exports the employee's performance evaluation report for the selected period (`/evaluations/form`) to an Excel workbook (`.xlsx`).
    /// </summary>
    [HttpGet("form/export")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelContentType)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Own")]
    public async Task<IActionResult> ExportEvaluationForm(
        [FromQuery] Guid employeeId,
        [FromQuery] Guid periodId,
        [FromQuery] Guid? comparePeriodId,
        CancellationToken cancellationToken)
    {
        RequireIds(("employeeId", employeeId), ("periodId", periodId));
        var file = await service.ExportEvaluationFormAsync(
            employeeId,
            periodId,
            User.GetRequiredUserId(),
            comparePeriodId,
            cancellationToken);

        return File(file.Content, ExcelContentType, file.FileName);
    }

    /// <summary>
    /// Submits qualitative scores, comments, and coaching plans, calculating and saving the employee's total performance score.
    /// </summary>
    [HttpPost("submit")]
    [ProducesResponseType<ApiResponse<EvaluationResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "submit", "Dept")]
    public async Task<ActionResult<ApiResponse<EvaluationResultDto>>> SubmitEvaluation(
        [FromBody] SubmitEvaluationRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireIds(("employeeId", request.EmployeeId), ("periodId", request.PeriodId));
        var result = await service.SubmitEvaluationAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets available evaluation periods for selection in the evaluation workspace.
    /// </summary>
    [HttpGet("periods")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EvaluationPeriodOptionDto>>>(StatusCodes.Status200OK)]
    [HasPermission("evaluation", "read", "Own")]
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
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>>>> GetDepartmentEmployees(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var result = await service.GetDepartmentEmployeesForEvaluationAsync(
            periodId,
            departmentId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets the Department Evaluation &amp; Performance Dashboard overview for the caller's department (`/evaluations`).
    /// Only accessible to Department Managers (`evaluation:read` at `Dept` scope).
    /// </summary>
    [HttpGet("department-dashboard")]
    [ProducesResponseType<ApiResponse<DepartmentEvaluationDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<ActionResult<ApiResponse<DepartmentEvaluationDashboardDto>>> GetDepartmentDashboard(
        [FromQuery] Guid? periodId,
        [FromQuery] Guid? comparePeriodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetDepartmentEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Exports the Department Evaluation &amp; Performance Dashboard (`/evaluations`) to an Excel workbook (`.xlsx`).
    /// Only accessible to Department Managers (`evaluation:read` at `Dept` scope).
    /// </summary>
    [HttpGet("department-dashboard/export")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelContentType)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<IActionResult> ExportDepartmentDashboard(
        [FromQuery] Guid? periodId,
        [FromQuery] Guid? comparePeriodId,
        CancellationToken cancellationToken)
    {
        var file = await service.ExportDepartmentEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            User.GetRequiredUserId(),
            cancellationToken);

        return File(file.Content, ExcelContentType, file.FileName);
    }

    /// <summary>
    /// Gets the Branch Manager's Evaluation Dashboard overview across departments in the branch (`/branch-performance/evaluations`).
    /// Only accessible to Branch Managers (`evaluation:read` at `Branch` or `All` scope).
    /// </summary>
    [HttpGet("branch-dashboard")]
    [ProducesResponseType<ApiResponse<DepartmentEvaluationDashboardDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<DepartmentEvaluationDashboardDto>>> GetBranchDashboard(
        [FromQuery] Guid? periodId,
        [FromQuery] Guid? comparePeriodId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetBranchEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            departmentId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Exports the Branch Manager's Evaluation Dashboard to an Excel workbook (`.xlsx`).
    /// Only accessible to Branch Managers (`evaluation:read` at `Branch` or `All` scope).
    /// </summary>
    [HttpGet("branch-dashboard/export")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelContentType)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<IActionResult> ExportBranchDashboard(
        [FromQuery] Guid? periodId,
        [FromQuery] Guid? comparePeriodId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        var file = await service.ExportBranchEvaluationDashboardAsync(
            periodId,
            comparePeriodId,
            departmentId,
            User.GetRequiredUserId(),
            cancellationToken);

        return File(file.Content, ExcelContentType, file.FileName);
    }

    /// <summary>
    /// Gets the department ranking board for a period: tier slots, the staff ranking and the change against the compare period (SCR-EVAL-04).
    /// </summary>
    [HttpGet("ranking-board")]
    [ProducesResponseType<ApiResponse<RankingBoardOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<ActionResult<ApiResponse<RankingBoardOverviewDto>>> GetRankingBoard(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? comparePeriodId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var result = await service.GetRankingBoardAsync(periodId, departmentId, comparePeriodId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Saves the final tiers and adjustment reasons of the ranking board as a draft (SCR-EVAL-04).
    /// </summary>
    [HttpPost("ranking-board/draft")]
    [ProducesResponseType<ApiResponse<RankingBoardOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "calibrate", "Dept")]
    public async Task<ActionResult<ApiResponse<RankingBoardOverviewDto>>> SaveRankingBoardDraft(
        [FromBody] SaveRankingBoardRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveRankingBoardDraftAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Once the lock day is reached, assigns the final tiers A1 to C by quantitative score and tier slots, and saves them as the draft (SCR-EVAL-04).
    /// </summary>
    [HttpPost("ranking-board/auto-assign")]
    [ProducesResponseType<ApiResponse<RankingBoardOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "calibrate", "Dept")]
    public async Task<ActionResult<ApiResponse<RankingBoardOverviewDto>>> AutoAssignRankingBoard(
        [FromBody] AutoAssignRankingBoardRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.AutoAssignRankingBoardAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Sends the ranking board to the Branch Director once the lock day is reached and every rule is satisfied (SCR-EVAL-04).
    /// </summary>
    [HttpPost("ranking-board/send-to-director")]
    [ProducesResponseType<ApiResponse<RankingBoardSendResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<RankingBoardSendResultDto>>> SendRankingBoardToDirector(
        [FromBody] SaveRankingBoardRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await service.SendRankingBoardToDirectorAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Exports the ranking board of a period to an Excel workbook (`.xlsx`) (SCR-EVAL-04).
    /// </summary>
    [HttpGet("ranking-board/export")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, ExcelContentType)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<IActionResult> ExportRankingBoard(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var file = await service.ExportRankingBoardAsync(periodId, departmentId, User.GetRequiredUserId(), cancellationToken);
        return File(file.Content, ExcelContentType, file.FileName);
    }

    /// <summary>
    /// Compares two employees of a period for the ranking board: quantitative score, criteria, SLA, risk events and group scores (reference only).
    /// </summary>
    [HttpGet("ranking-compare")]
    [ProducesResponseType<ApiResponse<RankingCompareDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<ActionResult<ApiResponse<RankingCompareDto>>> GetRankingCompare(
        [FromQuery] Guid periodId,
        [FromQuery] Guid employeeId1,
        [FromQuery] Guid employeeId2,
        CancellationToken cancellationToken)
    {
        var result = await service.GetRankingCompareAsync(periodId, employeeId1, employeeId2, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets the department ranking calibration workspace overview including ranked roster, Gaussian Bell curve, and quota tracking (SCR-EVL-02 - UC104).
    /// </summary>
    [HttpGet("calibration")]
    [ProducesResponseType<ApiResponse<CalibrationOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Dept")]
    public async Task<ActionResult<ApiResponse<CalibrationOverviewDto>>> GetCalibration(
        [FromQuery] Guid periodId,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? positionId,
        [FromQuery] bool includeSubDepartments = false,
        CancellationToken cancellationToken = default)
    {
        RequireIds(("periodId", periodId));
        var result = await service.GetDepartmentCalibrationAsync(
            periodId,
            departmentId,
            positionId,
            User.GetRequiredUserId(),
            includeSubDepartments: includeSubDepartments,
            cancellationToken: cancellationToken);

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
    [HasPermission("evaluation", "calibrate", "Dept")]
    public async Task<ActionResult<ApiResponse<CalibrationSubmitResultDto>>> SubmitCalibration(
        [FromBody] SubmitCalibrationRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireIds(("departmentId", request.DepartmentId), ("periodId", request.PeriodId));
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
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<BranchApprovalOverviewDto>>> GetApprovals(
        [FromQuery] Guid periodId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
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
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<CalibrationDecisionResultDto>>> RequestRecalibration(
        [FromRoute] Guid departmentId,
        [FromBody] RejectCalibrationRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", request.PeriodId));
        var result = await service.RejectDepartmentCalibrationAsync(
            departmentId,
            request,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Executive approves the calibrated rankings of a department (SCR-EVL-03-D1).
    /// </summary>
    [HttpPost("approvals/{departmentId:guid}/approve")]
    [ProducesResponseType<ApiResponse<DepartmentApprovalResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<DepartmentApprovalResultDto>>> ApproveDepartmentRanking(
        [FromRoute] Guid departmentId,
        [FromBody] ApproveDepartmentRankingRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", request.PeriodId));
        var result = await service.ApproveDepartmentCalibrationAsync(
            departmentId,
            request,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets ranking slot quotas for a department (SCR-EVL-03 - UC106).
    /// </summary>
    [HttpGet("approvals/departments/{departmentId:guid}/quotas")]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> GetDepartmentSlotQuotas(
        [FromRoute] Guid departmentId,
        [FromQuery] Guid? periodId,
        [FromServices] IRankingSlotQuotaService quotaService,
        CancellationToken cancellationToken)
    {
        var result = await quotaService.GetAsync(departmentId, periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Updates ranking slot quotas for a department by Branch Manager (SCR-EVL-03 - UC106).
    /// </summary>
    [HttpPut("approvals/departments/{departmentId:guid}/quotas")]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> UpdateDepartmentSlotQuotas(
        [FromRoute] Guid departmentId,
        [FromBody] UpdateRankingSlotQuotasDto request,
        [FromServices] IRankingSlotQuotaService quotaService,
        CancellationToken cancellationToken)
    {
        if (departmentId != request.DepartmentId)
        {
            request = request with { DepartmentId = departmentId };
        }
        var result = await quotaService.UpdateAsync(request, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets ranking slot quotas for the entire branch/period (SCR-EVL-03 - UC106).
    /// </summary>
    [HttpGet("approvals/period-quota")]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> GetBranchPeriodSlotQuotas(
        [FromQuery] Guid periodId,
        [FromServices] IRankingSlotQuotaService quotaService,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var result = await quotaService.GetBranchQuotaAsync(periodId, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Updates ranking slot quotas for the entire branch/period by Branch Manager (SCR-EVL-03 - UC106).
    /// </summary>
    [HttpPut("approvals/period-quota")]
    [ProducesResponseType<ApiResponse<RankingSlotQuotasDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<RankingSlotQuotasDto>>> UpdateBranchPeriodSlotQuotas(
        [FromQuery] Guid periodId,
        [FromBody] UpdateRankingSlotQuotasDto request,
        [FromServices] IRankingSlotQuotaService quotaService,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var result = await quotaService.UpdateBranchQuotaAsync(periodId, request.Slots.ToList(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Pre-close operational checklist before finalizing/locking an evaluation period (SCR-EVL-03-M1).
    /// </summary>
    [HttpGet("pre-close-check")]
    [ProducesResponseType<ApiResponse<PreCloseCheckOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<PreCloseCheckOverviewDto>>> GetPreCloseCheck(
        [FromQuery] Guid periodId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId));
        var result = await service.GetPreCloseCheckAsync(
            periodId,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Executive sign-off and hard-lock of evaluation period (SCR-EVL-03 - UC106, UC107).
    /// </summary>
    [HttpPost("approvals/finalize")]
    [ProducesResponseType<ApiResponse<FinalizeResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    [HasPermission("evaluation", "finalize", "Branch")]
    public async Task<ActionResult<ApiResponse<FinalizeResultDto>>> FinalizePeriod(
        [FromBody] FinalizeEvaluationPeriodRequestDto request,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", request.PeriodId));
        var result = await service.FinalizeAndLockPeriodAsync(request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets the list of evaluation periods for Branch Manager with progress, quality ratio and appeal window status (SCR-EVL-03 List).
    /// </summary>
    [HttpGet("branch-periods")]
    [ProducesResponseType<ApiResponse<BranchEvaluationPeriodsOverviewDto>>(StatusCodes.Status200OK)]
    [HasPermission("evaluation", "read", "Branch")]
    public async Task<ActionResult<ApiResponse<BranchEvaluationPeriodsOverviewDto>>> GetBranchPeriods(
        [FromQuery] int? year,
        [FromQuery] string? periodType,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var result = await service.GetBranchEvaluationPeriodsOverviewAsync(
            year,
            periodType,
            status,
            User.GetRequiredUserId(),
            cancellationToken);

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets personal performance evaluation details for the current employee (SCR-EVL-04 - UC108, UC109).
    /// </summary>
    [HttpGet("personal")]
    [ProducesResponseType<ApiResponse<PersonalEvaluationViewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Own")]
    public async Task<ActionResult<ApiResponse<PersonalEvaluationViewDto>>> GetPersonalEvaluation(
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPersonalEvaluationAsync(periodId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets personal criteria transaction ledger details for drilldown (SCR-EVL-04).
    /// </summary>
    [HttpGet("personal/ledger")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PersonalCriteriaLedgerItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [HasPermission("evaluation", "read", "Own")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PersonalCriteriaLedgerItemDto>>>> GetPersonalCriteriaLedger(
        [FromQuery] Guid periodId,
        [FromQuery] Guid criteriaId,
        CancellationToken cancellationToken)
    {
        RequireIds(("periodId", periodId), ("criteriaId", criteriaId));
        var result = await service.GetPersonalCriteriaLedgerAsync(periodId, criteriaId, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    /// <summary>An omitted GUID binds to Guid.Empty; refuse it as a validation error instead of reporting "not found".</summary>
    private static void RequireIds(params (string Name, Guid Value)[] ids)
    {
        var missing = ids.Where(i => i.Value == Guid.Empty).Select(i => $"'{i.Name}' là bắt buộc.").ToList();
        if (missing.Count > 0)
        {
            throw new ValidationException(missing);
        }
    }
}
