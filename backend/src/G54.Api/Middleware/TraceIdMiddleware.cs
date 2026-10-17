using Serilog.Context;

namespace G54.Api.Middleware;

public sealed class TraceIdMiddleware(RequestDelegate next)
{
    public const string TraceIdHeader = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(TraceIdHeader, out var incomingTraceId)
            && !string.IsNullOrWhiteSpace(incomingTraceId))
        {
            context.TraceIdentifier = incomingTraceId.ToString();
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceIdHeader] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
        {
            await next(context);
        }
    }
}

