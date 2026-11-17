using System.Collections.Concurrent;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Models;
using G54.BLL.Services;
using Microsoft.AspNetCore.Diagnostics;

namespace G54.Api.Middleware;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    ISystemAlertNotifier? systemAlerts = null) : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandledException =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "UnhandledException"), "Unhandled exception for {Path}");

    private static readonly Action<ILogger, string, string, Exception?> LogAppException =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(2, "AppException"), "Application exception [{ErrorCode}] for {Path}");

    private const int MaxTrackedAlerts = 500;
    private static readonly TimeSpan AlertWindow = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastAlertAt = new(StringComparer.Ordinal);

    /// <summary>The same failure on the same endpoint alerts the admins at most once per window, so a failing route cannot flood their inbox.</summary>
    internal static bool ShouldAlert(string key, DateTimeOffset now)
    {
        if (LastAlertAt.Count > MaxTrackedAlerts)
        {
            LastAlertAt.Clear();
        }

        var alert = false;
        LastAlertAt.AddOrUpdate(
            key,
            _ =>
            {
                alert = true;
                return now;
            },
            (_, last) =>
            {
                alert = now - last >= AlertWindow;
                return alert ? now : last;
            });
        return alert;
    }

    // The route pattern, never the raw path: ids in the path would make every request a new key.
    private static string AlertKey(HttpContext httpContext, Exception exception)
        => $"{exception.GetType().Name}|{httpContext.Request.Method}|{(httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched"}";

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
        if (systemAlerts is not null && ShouldAlert(AlertKey(httpContext, exception), DateTimeOffset.UtcNow))
        {
            await systemAlerts.NotifyAdminsAsync(
                "Cảnh báo lỗi hệ thống (HTTP 500)",
                $"Phát sinh ngoại lệ chưa xử lý ({exception.GetType().Name}) tại {httpContext.Request.Method} {httpContext.Request.Path}. Mã truy vết (TraceId): {traceId}.",
                CancellationToken.None);
        }

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
