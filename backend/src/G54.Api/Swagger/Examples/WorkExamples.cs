using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using Microsoft.AspNetCore.Http;
using Swashbuckle.AspNetCore.Filters;

namespace G54.Api.Swagger.Examples;

/// <summary>
/// Dữ liệu mẫu cho request body tạo mới công việc (POST /works).
/// </summary>
public sealed class CreateWorkRequestExample : IExamplesProvider<CreateWorkRequestDto>
{
    private static readonly string[] SampleChecklistItems =
    [
        "Lấy danh sách khách hàng",
        "Gọi điện tư vấn"
    ];

    public CreateWorkRequestDto GetExamples()
    {
        return new CreateWorkRequestDto(
            Title: "Liên hệ khách hàng tiềm năng đợt 1",
            WorkTypeId: Guid.Parse("b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d"),
            AssigneeEmployeeId: Guid.Parse("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"),
            Priority: "High",
            PlannedStartDate: new DateOnly(2026, 10, 20),
            PlannedEndDate: new DateOnly(2026, 10, 31),
            EstimatedHours: 16.5m,
            BusinessCampaignId: Guid.Parse("d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70"),
            Description: "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
            ChecklistItems: SampleChecklistItems);
    }
}

/// <summary>
/// Dữ liệu mẫu cho response tạo mới công việc thành công (POST /works - 201 Created).
/// Lưu ý: Thuộc tính Status trong body là 200 do ApiResponse.Ok khởi tạo, trong khi HTTP response status là 201.
/// </summary>
public sealed class CreateWorkSuccessExample : IExamplesProvider<ApiResponse<WorkDetailDto>>
{
    public ApiResponse<WorkDetailDto> GetExamples()
    {
        var data = new WorkDetailDto(
            Id: Guid.Parse("3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10"),
            Code: "WRK-20261019-0001",
            Title: "Liên hệ khách hàng tiềm năng đợt 1",
            Description: "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
            WorkTypeId: Guid.Parse("b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d"),
            WorkTypeName: "Chăm sóc khách hàng",
            BusinessCampaignId: Guid.Parse("d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70"),
            BusinessCampaignName: "Chiến dịch Q4 2026",
            AssigneeEmployeeId: Guid.Parse("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"),
            AssigneeEmployeeName: "Nguyễn Văn An",
            AssigneeEmployeeCode: "EMP001",
            Priority: "High",
            Status: "New",
            PlannedStartDate: new DateOnly(2026, 10, 20),
            PlannedEndDate: new DateOnly(2026, 10, 31),
            EstimatedHours: 16.5m,
            UpdatedAt: DateTimeOffset.Parse("2026-10-19T08:30:00+00:00", CultureInfo.InvariantCulture));

        return ApiResponse.Ok(
            data,
            "Khởi tạo và phân công công việc thành công.",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lấy chi tiết công việc thành công (GET /works/{id} - 200 OK).
/// </summary>
public sealed class GetWorkSuccessExample : IExamplesProvider<ApiResponse<WorkDetailDto>>
{
    public ApiResponse<WorkDetailDto> GetExamples()
    {
        var data = new WorkDetailDto(
            Id: Guid.Parse("3f2b8c1e-5a4d-4e7f-9b21-8d6c0a1e7f10"),
            Code: "WRK-20261019-0001",
            Title: "Liên hệ khách hàng tiềm năng đợt 1",
            Description: "Gọi điện tư vấn gói dịch vụ cho danh sách khách hàng được giao.",
            WorkTypeId: Guid.Parse("b7a1c2d3-4e5f-4a6b-8c7d-9e0f1a2b3c4d"),
            WorkTypeName: "Chăm sóc khách hàng",
            BusinessCampaignId: Guid.Parse("d4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70"),
            BusinessCampaignName: "Chiến dịch Q4 2026",
            AssigneeEmployeeId: Guid.Parse("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"),
            AssigneeEmployeeName: "Nguyễn Văn An",
            AssigneeEmployeeCode: "EMP001",
            Priority: "High",
            Status: "New",
            PlannedStartDate: new DateOnly(2026, 10, 20),
            PlannedEndDate: new DateOnly(2026, 10, 31),
            EstimatedHours: 16.5m,
            UpdatedAt: DateTimeOffset.Parse("2026-10-19T08:30:00+00:00", CultureInfo.InvariantCulture));

        return ApiResponse.Ok(
            data,
            message: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00");
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lỗi xác thực dữ liệu đầu vào (400 Bad Request - VALIDATION_ERROR).
/// </summary>
public sealed class WorkValidationErrorExample : IExamplesProvider<ApiResponse>
{
    private static readonly string[] SampleErrors =
    [
        "Title: The field Title must be a string with a minimum length of 5 and a maximum length of 255."
    ];

    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.ValidationError,
            message: SampleErrors[0],
            errors: SampleErrors,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status400BadRequest);
    }
}

/// <summary>
/// Dữ liệu mẫu cho response vi phạm quy tắc chiến dịch (400 Bad Request - MSG18).
/// </summary>
public sealed class WorkCampaignRuleErrorExample : IExamplesProvider<ApiResponse>
{
    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.MSG18,
            message: "Chiến dịch được gắn không trong trạng thái hoạt động.",
            errors: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status400BadRequest);
    }
}

/// <summary>
/// Nhiều ví dụ cho response 400 của POST /works: lỗi dữ liệu đầu vào (VALIDATION_ERROR) và vi phạm quy tắc chiến dịch (MSG18).
/// </summary>
public sealed class WorkCreateBadRequestExamples : IMultipleExamplesProvider<ApiResponse>
{
    public IEnumerable<SwaggerExample<ApiResponse>> GetExamples()
    {
        yield return SwaggerExample.Create(
            "VALIDATION_ERROR",
            "Dữ liệu đầu vào không hợp lệ",
            new WorkValidationErrorExample().GetExamples());

        yield return SwaggerExample.Create(
            "MSG18",
            "Vi phạm quy tắc chiến dịch",
            new WorkCampaignRuleErrorExample().GetExamples());
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lỗi chưa xác thực hoặc token không hợp lệ (401 Unauthorized - UNAUTHORIZED).
/// </summary>
public sealed class WorkUnauthorizedExample : IExamplesProvider<ApiResponse>
{
    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.Unauthorized,
            message: ErrorCodes.GetDefaultMessage(ErrorCodes.Unauthorized),
            errors: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status401Unauthorized);
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lỗi không tìm thấy tài nguyên khi tạo công việc (POST /works - 404 Not Found - NOT_FOUND).
/// Xảy ra khi BusinessCampaignId truyền lên không tồn tại trong hệ thống.
/// </summary>
public sealed class WorkCreateNotFoundExample : IExamplesProvider<ApiResponse>
{
    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.NotFound,
            message: "Chiến dịch với khóa 'd4e5f6a7-b8c9-4d0e-9f1a-2b3c4d5e6f70' không tồn tại.",
            errors: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status404NotFound);
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lỗi không tìm thấy công việc (GET /works/{id} - 404 Not Found - NOT_FOUND).
/// </summary>
public sealed class WorkGetNotFoundExample : IExamplesProvider<ApiResponse>
{
    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.NotFound,
            message: "Không tìm thấy công việc yêu cầu.",
            errors: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status404NotFound);
    }
}

/// <summary>
/// Dữ liệu mẫu cho response lỗi hệ thống không mong muốn (500 Internal Server Error - INTERNAL_ERROR).
/// </summary>
public sealed class WorkInternalErrorExample : IExamplesProvider<ApiResponse>
{
    public ApiResponse GetExamples()
    {
        return ApiResponse.Fail(
            errorCode: ErrorCodes.InternalError,
            message: "An unexpected error occurred. Contact support with the request trace identifier.",
            errors: null,
            traceId: "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00",
            status: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error");
    }
}
