using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.SystemParameter;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>System parameters &amp; Ranking Policy Studio (SCR-ADM-04 / GB-910). Contract: docs/api/SYSTEM_PARAMETER_CONTRACT.md.</summary>
[ApiController]
[Route("system-parameters")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class SystemParametersController(
    ISystemParameterService service,
    IEvaluationSettingsService evaluationSettings,
    IRankingPolicyService rankingPolicies) : ControllerBase
{
    [HttpGet]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SystemParameterDto>>>> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.GetAllAsync(cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut]
    [HasPermission("system_parameter", "update", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SystemParameterDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SystemParameterDto>>>> Update(
        UpdateSystemParametersDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await service.UpdateAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpGet("evaluation-comparison")]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<EvaluationComparisonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<EvaluationComparisonDto>>> GetEvaluationComparison(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await evaluationSettings.GetComparisonAsync(cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut("evaluation-comparison")]
    [HasPermission("system_parameter", "update", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<EvaluationComparisonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<EvaluationComparisonDto>>> UpdateEvaluationComparison(
        EvaluationComparisonDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await evaluationSettings.UpdateComparisonAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpGet("tier-criteria")]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<TierCriteriaMatrixDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<TierCriteriaMatrixDto>>> GetTierCriteria(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await evaluationSettings.GetTierCriteriaAsync(cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut("tier-criteria")]
    [HasPermission("system_parameter", "update", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<TierCriteriaMatrixDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TierCriteriaMatrixDto>>> UpdateTierCriteria(
        UpdateTierCriteriaDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await evaluationSettings.UpdateTierCriteriaAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpGet("ranking-policy")]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RankingPolicyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RankingPolicyDto>>> GetRankingPolicy(
        [FromQuery] Guid? periodId,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await rankingPolicies.GetPolicyAsync(periodId, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPut("ranking-policy")]
    [HasPermission("system_parameter", "update", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<RankingPolicyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RankingPolicyDto>>> UpdateRankingPolicy(
        UpdateRankingPolicyDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await rankingPolicies.UpdatePolicyAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));

    [HttpPost("ranking-policy/simulate")]
    [HasPermission("system_parameter", "view", PermissionScopes.All)]
    [ProducesResponseType<ApiResponse<SimulateRankingPolicyResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SimulateRankingPolicyResultDto>>> SimulateRankingPolicy(
        SimulateRankingPolicyRequestDto request,
        CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await rankingPolicies.SimulateAsync(request, cancellationToken), traceId: HttpContext.TraceIdentifier));
}
