using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace G54.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandledException =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "UnhandledException"), "Unhandled exception for {Path}");

    private static readonly Action<ILogger, string, string, Exception?> LogAppException =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(2, "AppException"), "Application exception [{ErrorCode}] for {Path}");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;
        httpContext.Response.Headers[TraceIdMiddleware.TraceIdHeader] = traceId;

        if (exception is AppException appEx)
        {
            LogAppException(logger, appEx.ErrorCode, httpContext.Request.Path, appEx);
            httpContext.Response.StatusCode = appEx.StatusCode;

            var response = ApiResponse.Fail(
                errorCode: appEx.ErrorCode,
                message: appEx.Message,
                errors: appEx.Errors,
                traceId: traceId,
                status: appEx.StatusCode,
                title: appEx.ErrorCode);

            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
            return true;
        }

        LogUnhandledException(logger, httpContext.Request.Path, exception);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var internalResponse = ApiResponse.Fail(
            errorCode: ErrorCodes.InternalError,
            message: "An unexpected error occurred. Contact support with the request trace identifier.",
            errors: null,
            traceId: traceId,
            status: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error");

        await httpContext.Response.WriteAsJsonAsync(internalResponse, cancellationToken);
        return true;
    }
}
