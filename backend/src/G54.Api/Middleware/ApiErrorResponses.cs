using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Middleware;

/// <summary>
/// Builds the standard <see cref="ApiResponse"/> envelope for errors raised outside <see cref="GlobalExceptionHandler"/>:
/// model-state validation (400) and JwtBearer challenge/forbid (401/403).
/// </summary>
public static class ApiErrorResponses
{
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => FormatError(entry.Key, error)))
            .ToList();

        var response = ApiResponse.Fail(
            errorCode: ErrorCodes.ValidationError,
            message: errors.Count > 0 ? errors[0] : ErrorCodes.GetDefaultMessage(ErrorCodes.ValidationError),
            errors: errors,
            traceId: context.HttpContext.TraceIdentifier,
            status: StatusCodes.Status400BadRequest);

        return new BadRequestObjectResult(response);
    }

    public static Task WriteAsync(HttpContext httpContext, int statusCode, string errorCode)
    {
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.Headers[TraceIdMiddleware.TraceIdHeader] = httpContext.TraceIdentifier;

        var response = ApiResponse.Fail(
            errorCode: errorCode,
            message: ErrorCodes.GetDefaultMessage(errorCode),
            traceId: httpContext.TraceIdentifier,
            status: statusCode);

        return httpContext.Response.WriteAsJsonAsync(response, httpContext.RequestAborted);
    }

    private static string FormatError(string key, Microsoft.AspNetCore.Mvc.ModelBinding.ModelError error)
    {
        var message = string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? ErrorCodes.GetDefaultMessage(ErrorCodes.ValidationError)
            : error.ErrorMessage;
        var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;

        return string.IsNullOrEmpty(field) || field == "$" ? message : $"{field}: {message}";
    }
}
