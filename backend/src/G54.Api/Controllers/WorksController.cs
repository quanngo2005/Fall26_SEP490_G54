using G54.Api.Swagger.Examples;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using G54.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace G54.Api.Controllers;

/// <summary>
/// Quản lý công việc (Work): tạo và phân công công việc, xem chi tiết công việc.
/// Contract: docs/api/WORK_CONTRACT.md.
/// Lỗi được ném dưới dạng AppException ở service và được GlobalExceptionHandler map sang ApiResponse.
/// </summary>
[ApiController]
[Route("works")]
[Authorize]
[ProducesResponseType<ApiResponse>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse>(StatusCodes.Status500InternalServerError)]
public sealed class WorksController(IWorkService workService) : ControllerBase
{
    /// <summary>
    /// Tạo và phân công công việc mới.
    /// </summary>
    /// <remarks>
    /// <para><b>Hành vi nghiệp vụ:</b></para>
    /// <list type="bullet">
    /// <item>Trạng thái ban đầu luôn là <c>New</c>.</item>
    /// <item>Mã công việc (<c>Code</c>) sinh tự động theo định dạng <c>WRK-yyyyMMdd-####</c>. Nếu trùng lặp 5 lần sẽ dùng hậu tố 6 ký tự hex của Guid.</item>
    /// <item>Nếu <c>PlannedStartDate</c> để trống, hệ thống mặc định là ngày hiện tại (UTC).</item>
    /// <item>Mức ưu tiên <c>Priority</c> mặc định là <c>Medium</c>. Giá trị nhận: <c>Low</c>, <c>Medium</c>, <c>High</c>, <c>Critical</c>, và bí danh <c>P1</c> (Critical), <c>P2</c> (High) (không phân biệt hoa/thường). Mọi giá trị khác được âm thầm chuyển về <c>Medium</c> mà không báo lỗi.</item>
    /// <item><c>ChecklistItems</c> hiện tại server chưa xử lý và bỏ qua.</item>
    /// <item><c>Status</c> và <c>Priority</c> trong response trả về định dạng PascalCase (<c>New</c>, <c>High</c>...).</item>
    /// </list>
    /// <para><b>Quy tắc kiểm tra (Validation / Business Rules):</b></para>
    /// <list type="bullet">
    /// <item><c>Title</c> bắt buộc, từ 5 đến 255 ký tự.</item>
    /// <item><c>WorkTypeId</c> và <c>AssigneeEmployeeId</c> bắt buộc, phải tồn tại và đang Active (nếu không tồn tại hoặc inactive trả lỗi 400 <c>VALIDATION_ERROR</c>).</item>
    /// <item><c>PlannedEndDate</c> không được trước <c>PlannedStartDate</c>.</item>
    /// <item><c>EstimatedHours</c> không được âm (từ 0 đến 9999.99).</item>
    /// <item>Nếu có <c>BusinessCampaignId</c>: chiến dịch phải tồn tại (nếu không trả 404 <c>NOT_FOUND</c>), phải ở trạng thái Running hoặc Planned (nếu không trả 400 <c>MSG18</c>), <c>PlannedEndDate</c> không được vượt quá <c>EndDate</c> của chiến dịch (nếu không trả 400 <c>MSG18</c>), và nhân viên phải là Participant của chiến dịch (nếu không trả 400 <c>VALIDATION_ERROR</c>).</item>
    /// </list>
    /// <para><b>Ví dụ request body:</b></para>
    /// <code>
    /// {
    ///   "title": "Liên hệ khách hàng tiềm năng đợt 1",
    ///   "workTypeId": "b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d",
    ///   "assigneeEmployeeId": "c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f",
    ///   "priority": "High",
    ///   "plannedStartDate": "2026-10-20",
    ///   "plannedEndDate": "2026-10-31",
    ///   "estimatedHours": 16.5,
    ///   "businessCampaignId": "d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70",
    ///   "description": "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
    ///   "checklistItems": [
    ///     "Lấy danh sách khách hàng",
    ///     "Gọi điện tư vấn"
    ///   ]
    /// }
    /// </code>
    /// <para><b>Lưu ý về Status Code:</b> HTTP status trả về là <b>201 Created</b> kèm header <c>Location</c> trỏ tới <c>GET /works/{id}</c>. Tuy nhiên trường <c>status</c> bên trong JSON envelope có giá trị là 200 do hành vi mặc định hiện tại của <c>ApiResponse.Ok</c>.</para>
    /// </remarks>
    /// <param name="request">Dữ liệu yêu cầu khởi tạo công việc.</param>
    /// <param name="cancellationToken">Token hủy yêu cầu bất đồng bộ.</param>
    /// <returns>Thông tin chi tiết công việc vừa được khởi tạo.</returns>
    /// <response code="201">Khởi tạo và phân công công việc thành công. Trả về thông tin chi tiết kèm header Location.</response>
    /// <response code="400">Dữ liệu không hợp lệ (errorCode: VALIDATION_ERROR) hoặc vi phạm quy tắc chiến dịch (errorCode: MSG18). Gồm các trường hợp: Title rỗng hoặc ngoài 5-255 ký tự; PlannedEndDate &lt; PlannedStartDate; EstimatedHours &lt; 0; WorkTypeId/AssigneeEmployeeId không tồn tại hoặc không Active; Assignee chưa là Participant của campaign; Campaign không ở trạng thái Running/Planned; PlannedEndDate vượt quá EndDate của campaign.</response>
    /// <response code="401">Chưa xác thực hoặc token không hợp lệ (errorCode: UNAUTHORIZED).</response>
    /// <response code="404">Không tìm thấy tài nguyên (errorCode: NOT_FOUND). Chỉ xảy ra khi BusinessCampaignId được truyền nhưng không tồn tại trong hệ thống. Lưu ý: WorkTypeId hoặc AssigneeEmployeeId không tồn tại trả về mã lỗi 400 (VALIDATION_ERROR), không phải 404.</response>
    /// <response code="500">Lỗi hệ thống không mong muốn (errorCode: INTERNAL_ERROR).</response>
    [HttpPost]
    [ProducesResponseType<ApiResponse<WorkDetailDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [SwaggerRequestExample(typeof(CreateWorkRequestDto), typeof(CreateWorkRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(CreateWorkSuccessExample))]
    [SwaggerResponseExample(StatusCodes.Status400BadRequest, typeof(WorkCreateBadRequestExamples))]
    [SwaggerResponseExample(StatusCodes.Status401Unauthorized, typeof(WorkUnauthorizedExample))]
    [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(WorkCreateNotFoundExample))]
    [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(WorkInternalErrorExample))]
    public async Task<ActionResult<ApiResponse<WorkDetailDto>>> Create(
        [FromBody] CreateWorkRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await workService.CreateWorkAsync(request, cancellationToken);
        var response = ApiResponse.Ok(
            result,
            "Khởi tạo và phân công công việc thành công.",
            HttpContext.TraceIdentifier);

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            response);
    }

    /// <summary>
    /// Lấy chi tiết công việc theo Id.
    /// </summary>
    /// <param name="id">Id định danh duy nhất của công việc (Guid, ví dụ: 3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10).</param>
    /// <param name="cancellationToken">Token hủy yêu cầu bất đồng bộ.</param>
    /// <returns>Thông tin chi tiết công việc theo Id yêu cầu.</returns>
    /// <response code="200">Lấy thông tin chi tiết công việc thành công.</response>
    /// <response code="401">Chưa xác thực hoặc token không hợp lệ (errorCode: UNAUTHORIZED).</response>
    /// <response code="404">Không tìm thấy công việc yêu cầu với Id đã cung cấp (errorCode: NOT_FOUND).</response>
    /// <response code="500">Lỗi hệ thống không mong muốn (errorCode: INTERNAL_ERROR).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApiResponse<WorkDetailDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse>(StatusCodes.Status404NotFound)]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(GetWorkSuccessExample))]
    [SwaggerResponseExample(StatusCodes.Status401Unauthorized, typeof(WorkUnauthorizedExample))]
    [SwaggerResponseExample(StatusCodes.Status404NotFound, typeof(WorkGetNotFoundExample))]
    [SwaggerResponseExample(StatusCodes.Status500InternalServerError, typeof(WorkInternalErrorExample))]
    public async Task<ActionResult<ApiResponse<WorkDetailDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await workService.GetByIdAsync(id, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiResponse.Fail(
                errorCode: ErrorCodes.NotFound,
                message: "Không tìm thấy công việc yêu cầu.",
                errors: null,
                traceId: HttpContext.TraceIdentifier,
                status: StatusCodes.Status404NotFound));
        }

        return Ok(ApiResponse.Ok(result, traceId: HttpContext.TraceIdentifier));
    }
}
