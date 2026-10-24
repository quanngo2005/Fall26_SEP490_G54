using G54.Api.Authorization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationProfile;
using G54.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Route("evaluation-profiles")]
public sealed class EvaluationProfilesController(IEvaluationProfileService service) : ControllerBase
{
    [HttpGet]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<PagedResult<EvaluationProfileListItemDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<EvaluationProfileListItemDto>>>> GetProfiles(
        [FromQuery] EvaluationProfileFilterDto filter,
        CancellationToken cancellationToken)
    {
        var result = await service.GetProfilesAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:guid}")]
    [HasPermission("kpi", "read", "All")]
    [ProducesResponseType<ApiResponse<EvaluationProfileDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EvaluationProfileDto>>> GetProfileById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.GetProfileByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                ErrorCodes.NotFound,
                "Không tìm thấy hồ sơ đánh giá yêu cầu.",
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [HasPermission("kpi", "manage", "All")]
    [ProducesResponseType<ApiResponse<EvaluationProfileDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EvaluationProfileDto>>> CreateProfile(
        [FromBody] CreateEvaluationProfileDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateProfileAsync(dto, cancellationToken);
        var response = ApiResponse.Ok(result, "Tạo hồ sơ đánh giá thành công.", HttpContext.TraceIdentifier);
        return CreatedAtAction(nameof(GetProfileById), new { id = result.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("kpi", "manage", "All")]
    [ProducesResponseType<ApiResponse<EvaluationProfileDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EvaluationProfileDto>>> UpdateProfile(
        Guid id,
        [FromBody] UpdateEvaluationProfileDto dto,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateProfileAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok(result, "Cập nhật hồ sơ đánh giá thành công.", HttpContext.TraceIdentifier));
    }

    [HttpPatch("{id:guid}/status")]
    [HasPermission("kpi", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> ChangeProfileStatus(
        Guid id,
        [FromBody] EvaluationProfileStatusDto dto,
        CancellationToken cancellationToken)
    {
        await service.ChangeStatusAsync(id, dto, cancellationToken);
        return Ok(ApiResponse.Ok("Cập nhật trạng thái hồ sơ đánh giá thành công.", HttpContext.TraceIdentifier));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("kpi", "manage", "All")]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> DeleteProfile(
        Guid id,
        CancellationToken cancellationToken)
    {
        await service.DeleteProfileAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok("Lưu trữ hồ sơ đánh giá thành công.", HttpContext.TraceIdentifier));
    }
}
