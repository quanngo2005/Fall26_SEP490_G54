using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace G54.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandledException =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "UnhandledException"), "Unhandled exception for {Path}");

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        LogUnhandledException(logger, httpContext.Request.Path, exception);
        var details = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "Contact support with the request trace identifier.",
            Instance = httpContext.Request.Path,
        };
        details.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = details.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(details, cancellationToken);
        return true;
    }
}
