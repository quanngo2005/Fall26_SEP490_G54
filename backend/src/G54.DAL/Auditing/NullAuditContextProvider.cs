namespace G54.DAL.Auditing;

/// <summary>Used outside an HTTP request (DbMigrator, seeders): actor is null, actor name is "system".</summary>
public sealed class NullAuditContextProvider : IAuditContextProvider
{
    public AuditContext GetContext() => new(null, "system", null, null, null, null, null);
}
