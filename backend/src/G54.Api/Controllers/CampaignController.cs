using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Campaign;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

/// <summary>
/// Controller for Business Campaigns & Batch Work Generator (SCR-CMP-01 & SCR-CMP-02).
/// Enforces Maker-Checker approval (BR-02), idempotent task code generation (BR-08),
/// server UTC time bounds (BR-11, BR-12), 7-day exclusivity (BR-25), and active employee filtering (BR-41).
/// </summary>
[ApiController]
[Route("campaigns")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
public sealed class CampaignController(ICampaignService campaignService) : ControllerBase
{
    /// <summary>
    /// Gets a paginated list of business campaigns with overview metrics and filtering.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<CampaignListOverviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<CampaignListOverviewDto>>> GetCampaigns(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Guid? objectiveId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.GetCampaignsAsync(search, status, objectiveId, page, pageSize, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Lấy danh sách chiến dịch thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Gets campaign details by ID including work types, participants, and execution metrics.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<CampaignDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<CampaignDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.GetCampaignByIdAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Lấy thông tin chi tiết chiến dịch thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Creates a new campaign plan.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<CampaignDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CampaignDetailDto>>> Create(
        [FromBody] CreateCampaignRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.CreateCampaignAsync(request, User.GetRequiredUserId(), cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo mới kế hoạch chiến dịch thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            response);
    }

    /// <summary>
    /// Updates an existing campaign plan.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiResponse<CampaignDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CampaignDetailDto>>> Update(
        Guid id,
        [FromBody] UpdateCampaignRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.UpdateCampaignAsync(id, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật kế hoạch chiến dịch thành công.", HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Deletes a planned campaign that has not yet generated any tasks.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await campaignService.DeleteCampaignAsync(id, User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Reviews and approves or rejects a campaign plan (SCR-CMP-02 / UC23 - Maker-Checker BR-02).
    /// </summary>
    [HttpPost("{id:guid}/decision")]
    [ProducesResponseType<ApiResponse<CampaignDecisionResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CampaignDecisionResultDto>>> Decide(
        Guid id,
        [FromBody] CampaignDecisionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.DecideCampaignAsync(id, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, result.Message, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Generates batch work items allocated across active participants (SCR-CMP-01 / UC24).
    /// </summary>
    [HttpPost("{id:guid}/batch-generate-work")]
    [ProducesResponseType<ApiResponse<BatchWorkGenerateResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BatchWorkGenerateResultDto>>> BatchGenerateWork(
        Guid id,
        [FromBody] BatchWorkGeneratorRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var result = await campaignService.BatchGenerateWorkAsync(id, request, User.GetRequiredUserId(), cancellationToken);
        return Ok(ApiResponse.Ok(result, result.Message, HttpContext.TraceIdentifier));
    }
}
