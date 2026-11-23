using G54.Api.Authorization;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Admin;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Controllers;

[ApiController]
[Authorize]
[Route("")]
[HasPermission("role", "update", "System")]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status403Forbidden)]
public sealed class AdminAccountsController(IAdminAccountService service) : ControllerBase
{
    [HttpGet("roles")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<RoleSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleSummaryDto>>>> GetRoles(
        CancellationToken cancellationToken)
    {
        var roles = await service.GetRolesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(roles, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("user-accounts")]
    [ProducesResponseType<ApiResponse<PagedResult<UserAccountSummaryDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserAccountSummaryDto>>>> GetAccounts(
        [FromQuery] UserAccountFilterDto filter,
        CancellationToken cancellationToken)
    {
        var accounts = await service.GetAccountsAsync(filter, cancellationToken);
        return Ok(ApiResponse.Ok(accounts, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("user-accounts/summary")]
    [ProducesResponseType<ApiResponse<UserAccountSummaryCountsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<UserAccountSummaryCountsDto>>> GetSummary(
        CancellationToken cancellationToken)
    {
        var counts = await service.GetSummaryAsync(cancellationToken);
        return Ok(ApiResponse.Ok(counts, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("user-accounts/eligible-employees")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EligibleEmployeeDto>>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EligibleEmployeeDto>>>> GetEligibleEmployees(
        CancellationToken cancellationToken)
    {
        var employees = await service.GetEligibleEmployeesAsync(cancellationToken);
        return Ok(ApiResponse.Ok(employees, traceId: HttpContext.TraceIdentifier));
    }

    [HttpGet("user-accounts/{id:guid}")]
    [ProducesResponseType<ApiResponse<UserAccountDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserAccountDetailDto>>> GetAccountDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var detail = await service.GetAccountDetailAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(detail, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("user-accounts")]
    [ProducesResponseType<ApiResponse<CreateUserAccountResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<CreateUserAccountResultDto>>> CreateAccount(
        [FromBody] CreateUserAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAccountAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetAccountDetail),
            new { id = created.Account.Id },
            new ApiResponse<CreateUserAccountResultDto>
            {
                Success = true,
                Status = StatusCodes.Status201Created,
                Data = created,
                TraceId = HttpContext.TraceIdentifier,
            });
    }

    [HttpPut("user-accounts/{id:guid}")]
    [ProducesResponseType<ApiResponse<UserAccountDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserAccountDetailDto>>> UpdateAccount(
        Guid id,
        [FromBody] UpdateUserAccountRequestDto request,
        CancellationToken cancellationToken)
    {
        var updated = await service.UpdateAccountAsync(id, request, cancellationToken);
        return Ok(ApiResponse.Ok(updated, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("user-accounts/{id:guid}/reset-password")]
    [ProducesResponseType<ApiResponse<ResetUserPasswordResultDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ResetUserPasswordResultDto>>> ResetPassword(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await service.ResetPasswordAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("user-accounts/{id:guid}/lock")]
    [ProducesResponseType<ApiResponse<UserAccountDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserAccountDetailDto>>> LockAccount(
        Guid id,
        CancellationToken cancellationToken)
    {
        var locked = await service.LockAccountAsync(User.GetRequiredUserId(), id, cancellationToken);
        return Ok(ApiResponse.Ok(locked, traceId: HttpContext.TraceIdentifier));
    }

    [HttpPost("user-accounts/{id:guid}/unlock")]
    [ProducesResponseType<ApiResponse<UserAccountDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserAccountDetailDto>>> UnlockAccount(
        Guid id,
        CancellationToken cancellationToken)
    {
        var unlocked = await service.UnlockAccountAsync(id, cancellationToken);
        return Ok(ApiResponse.Ok(unlocked, traceId: HttpContext.TraceIdentifier));
    }
}
