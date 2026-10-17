using System.Security.Claims;
using Serilog.Context;

namespace G54.Api.Middleware;

public sealed class UserLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            using (LogContext.PushProperty("UserId", userId))
            {
                await next(context);
                return;
            }
        }

        await next(context);
    }
}
