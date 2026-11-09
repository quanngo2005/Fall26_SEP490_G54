using G54.DAL.Auditing;

namespace G54.DAL.Entities;

/// <summary>
/// Immutable record of a Create/Update/Delete/SoftDelete operation on an auditable entity. Not a
/// <see cref="BaseEntity"/>: it is never soft-deleted and never audits itself.
/// </summary>
[AuditIgnore]
public sealed class AuditLog
{
    public Guid Id { get; set; }

    public string EntityName { get; set; } = null!;

    public string EntityId { get; set; } = null!;

    /// <summary>Create | Update | Delete | SoftDelete.</summary>
    public string Action { get; set; } = null!;

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    /// <summary>JSON array of changed column names (Update only).</summary>
    public string? ChangedColumns { get; set; }

    public Guid? ActorId { get; set; }

    public string? ActorName { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public string? HttpMethod { get; set; }

    public string? RequestPath { get; set; }

    public string? TraceId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
