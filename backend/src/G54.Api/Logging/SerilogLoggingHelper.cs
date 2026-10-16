using System.Security.Claims;
using Serilog;
using Serilog.Events;

namespace G54.Api.Logging;

public static class SerilogLoggingHelper
{
    public const string DefaultMessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    public static void EnrichDiagnosticContext(IDiagnosticContext diagnosticContext, HttpContext httpContext)
    {
        diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);

        var userId = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            diagnosticContext.Set("UserId", userId);
        }

        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? "unknown";
        diagnosticContext.Set("ClientIp", clientIp);

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            diagnosticContext.Set("UserAgent", userAgent);
        }

        if (httpContext.Request.Host.HasValue)
        {
            diagnosticContext.Set("Host", httpContext.Request.Host.Value);
        }

        if (!string.IsNullOrWhiteSpace(httpContext.Request.Scheme))
        {
            diagnosticContext.Set("Scheme", httpContext.Request.Scheme);
        }
    }

    public static LogEventLevel GetRequestLogLevel(HttpContext httpContext, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return LogEventLevel.Warning;
        }

        if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }
}
