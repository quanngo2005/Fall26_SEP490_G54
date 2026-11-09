namespace G54.DAL.Auditing;

/// <summary>Context captured alongside an audit log entry: who performed the operation and from where.</summary>
public sealed record AuditContext(
    Guid? ActorId,
    string? ActorName,
    string? IpAddress,
    string? UserAgent,
    string? HttpMethod,
    string? RequestPath,
    string? TraceId);

/// <summary>
/// Supplies the actor/request context for <see cref="AuditSaveChangesInterceptor"/>. Declared in DAL (not API)
/// to keep the API -&gt; BLL -&gt; DAL dependency direction: the DAL interceptor depends on this interface, and the
/// API layer provides the HTTP-backed implementation.
/// </summary>
public interface IAuditContextProvider
{
    AuditContext GetContext();
}
