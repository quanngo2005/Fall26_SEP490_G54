using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class AuthAuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public string Action { get; set; } = null!;

    public DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public virtual Account? Actor { get; set; }
}
