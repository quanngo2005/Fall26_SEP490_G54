using System.IO;
using System.Text.Json;
using G54.Api.Middleware;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace G54.UnitTests;

public sealed class ApiResponseAndExceptionTests
{
    [Fact]
    public void ApiResponse_Ok_SetsSuccessAndData()
    {
        var response = ApiResponse.Ok("Sample Data", "Success message", "trace-123");

        Assert.True(response.Success);
        Assert.Equal(200, response.Status);
        Assert.Equal("Sample Data", response.Data);
        Assert.Equal("Success message", response.Message);
        Assert.Equal("trace-123", response.TraceId);
    }

    private static readonly string[] SampleItems = ["Item1", "Item2"];
    private static readonly string[] SampleErrors = ["Field is required"];

    [Fact]
    public void ApiResponse_Ok_WithPagination_ComputesMetadata()
    {
        var pagination = PaginationMetadata.Create(pageIndex: 2, pageSize: 10, totalItems: 25);
        var response = ApiResponse.Ok(SampleItems, pagination, "Page 2", "trace-page");

        Assert.True(response.Success);
        Assert.NotNull(response.Pagination);
        Assert.Equal(2, response.Pagination.PageIndex);
        Assert.Equal(10, response.Pagination.PageSize);
        Assert.Equal(25, response.Pagination.TotalItems);
        Assert.Equal(3, response.Pagination.TotalPages);
        Assert.True(response.Pagination.HasPreviousPage);
        Assert.True(response.Pagination.HasNextPage);
    }

    [Fact]
    public void ApiResponse_Fail_SetsFailureFields()
    {
        var response = ApiResponse.Fail(
            errorCode: ErrorCodes.MSG02,
            message: "Validation failed",
            errors: SampleErrors,
            traceId: "trace-err",
            status: 400);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.MSG02, response.ErrorCode);
        Assert.Equal("Validation failed", response.Message);
        Assert.Equal(SampleErrors, response.Errors);
        Assert.Equal("trace-err", response.TraceId);
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public void ErrorCodes_ReturnsExpectedBilingualMessages()
    {
        var msg10Vi = ErrorCodes.GetDefaultMessage(ErrorCodes.MSG10, english: false);
        var msg10En = ErrorCodes.GetDefaultMessage(ErrorCodes.MSG10, english: true);

        Assert.Contains("Người thực hiện", msg10Vi);
        Assert.Contains("Maker cannot assign self", msg10En);

        var fallback = ErrorCodes.GetDefaultMessage("NON_EXISTENT_CODE");
        Assert.Equal("Đã xảy ra lỗi.", fallback);
    }

    [Fact]
    public async Task GlobalExceptionHandler_HandlesBusinessRuleException()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "test-trace-id";

        var exception = new BusinessRuleException(ErrorCodes.MSG10);
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("test-trace-id", context.Response.Headers[TraceIdMiddleware.TraceIdHeader]);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        using var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(ErrorCodes.MSG10, root.GetProperty("errorCode").GetString());
        Assert.Equal("test-trace-id", root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task GlobalExceptionHandler_HandlesUnhandledException_HidesStackTrace()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "internal-trace-id";

        var exception = new InvalidOperationException("Sensitive DB error details with stack trace");
        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.DoesNotContain("Sensitive DB error", body);
        Assert.Contains(ErrorCodes.InternalError, body);
        Assert.Contains("internal-trace-id", body);
    }

    [Fact]
    public async Task TraceIdMiddleware_ReusesIncomingHeaderOrPropagatesTraceIdentifier()
    {
        var middleware = new TraceIdMiddleware(next: innerContext =>
        {
            Assert.Equal("incoming-custom-trace", innerContext.TraceIdentifier);
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers[TraceIdMiddleware.TraceIdHeader] = "incoming-custom-trace";

        await middleware.InvokeAsync(context);
    }

    [Fact]
    public void ApiErrorResponses_InvalidModelState_ReturnsValidationEnvelope()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "model-trace" };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        actionContext.ModelState.AddModelError("$.decision", "Quyết định thẩm định chỉ có thể là APPROVED, REVISION_REQUIRED hoặc REJECTED.");
        actionContext.ModelState.AddModelError("Feedback", "Ý kiến chỉ đạo không được vượt quá 2000 ký tự.");

        var result = Assert.IsType<BadRequestObjectResult>(ApiErrorResponses.InvalidModelState(actionContext));
        var response = Assert.IsType<ApiResponse>(result.Value);

        Assert.False(response.Success);
        Assert.Equal(400, response.Status);
        Assert.Equal(ErrorCodes.ValidationError, response.ErrorCode);
        Assert.Equal("model-trace", response.TraceId);
        Assert.NotNull(response.Errors);
        Assert.Contains("decision: Quyết định thẩm định chỉ có thể là APPROVED, REVISION_REQUIRED hoặc REJECTED.", response.Errors);
        Assert.Contains("Feedback: Ý kiến chỉ đạo không được vượt quá 2000 ký tự.", response.Errors);
    }

    [Theory]
    [InlineData(401, ErrorCodes.Unauthorized)]
    [InlineData(403, ErrorCodes.Forbidden)]
    public async Task ApiErrorResponses_WriteAsync_WritesEnvelopeForAuthFailures(int statusCode, string errorCode)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "auth-trace" };
        context.Response.Body = new MemoryStream();

        await ApiErrorResponses.WriteAsync(context, statusCode, errorCode);

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal("auth-trace", context.Response.Headers[TraceIdMiddleware.TraceIdHeader]);

        context.Response.Body.Position = 0;
        using var jsonDoc = await JsonDocument.ParseAsync(context.Response.Body);
        var root = jsonDoc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(errorCode, root.GetProperty("errorCode").GetString());
        Assert.Equal(statusCode, root.GetProperty("status").GetInt32());
        Assert.Equal("auth-trace", root.GetProperty("traceId").GetString());
    }

    [Theory]
    [InlineData(ErrorCodes.SubmissionInvalidState)]
    [InlineData(ErrorCodes.VerificationInvalidState)]
    [InlineData(ErrorCodes.KpiResultNotFound)]
    public void ErrorCodes_NewLifecycleCodes_HaveBilingualMessages(string code)
    {
        Assert.NotEqual("Đã xảy ra lỗi.", ErrorCodes.GetDefaultMessage(code));
        Assert.NotEqual("An error occurred.", ErrorCodes.GetDefaultMessage(code, english: true));
    }

    [Fact]
    public void EnumWireFormat_UsesPgNameAndApiDecisionVocabulary()
    {
        Assert.Equal("UNDER_REVIEW", SubmissionStatus.UnderReview.ToWire());
        Assert.Equal("IN_PROGRESS", WorkStatus.InProgress.ToWire());
        Assert.Equal("HIGHER_BETTER", KpiDirection.HigherBetter.ToWire());
        Assert.Equal("REVISION_REQUIRED", VerificationDecision.NeedMoreInfo.ToApiDecision());
        Assert.Equal("APPROVED", VerificationDecision.Approved.ToApiDecision());
    }
}
