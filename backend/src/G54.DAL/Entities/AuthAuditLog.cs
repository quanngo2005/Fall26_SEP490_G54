using System;
using System.Collections.Generic;
using G54.DAL.Auditing;

namespace G54.DAL.Entities;

/// <summary>Audit log for login/logout; excluded from the generic AuditLog interceptor to avoid double-logging.</summary>
[AuditIgnore]
public partial class AuthAuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string Action { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public virtual Account? Actor { get; set; }
}
