namespace G54.BLL.Dtos.AuditLog;

public sealed record AuditLogListItemDto(
    Guid Id,
    string EntityName,
    string EntityId,
    string Action,
    Guid? ActorId,
    string? ActorName,
    string? IpAddress,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string>? ChangedColumns);

public sealed record AuditLogDetailDto(
    Guid Id,
    string EntityName,
    string EntityId,
    string Action,
    Guid? ActorId,
    string? ActorName,
    string? IpAddress,
    string? UserAgent,
    string? HttpMethod,
    string? RequestPath,
    string? TraceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<string>? ChangedColumns,
    object? OldValues,
    object? NewValues);

public sealed record AuditLogQueryDto
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string? EntityName { get; init; }

    public string? EntityId { get; init; }

    public string? Action { get; init; }

    public Guid? ActorId { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public string? Keyword { get; init; }

    /// <summary>"occurredAt desc" (default) or "occurredAt asc".</summary>
    public string? Sort { get; init; }
}
