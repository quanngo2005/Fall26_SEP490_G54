namespace G54.DAL.Repositories;

public sealed record AuditLogFilter(
    string? EntityName = null,
    string? EntityId = null,
    string? Action = null,
    Guid? ActorId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Keyword = null);
