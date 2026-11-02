using G54.Api.Authorization;
using G54.DAL.Auditing;
using Microsoft.AspNetCore.Http;

namespace G54.Api.Auditing;

/// <summary>HTTP-backed <see cref="IAuditContextProvider"/>; reads actor/request data from the current request.</summary>
public sealed class HttpAuditContextProvider(IHttpContextAccessor httpContextAccessor) : IAuditContextProvider
{
    public AuditContext GetContext()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return new AuditContext(null, null, null, null, null, null, null);
        }

        Guid? actorId = null;
        try
        {
            actorId = httpContext.User.GetRequiredUserId();
        }
        catch
        {
            // Anonymous request (e.g. login): no actor id, never throw from audit context resolution.
        }

        return new AuditContext(
            actorId,
            actorId is null ? null : httpContext.User.Identity?.Name,
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            httpContext.Request.Method,
            httpContext.Request.Path.ToString(),
            httpContext.TraceIdentifier);
    }
}
