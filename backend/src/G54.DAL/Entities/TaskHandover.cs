using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class TaskHandover
{
    public Guid Id { get; set; }

    public Guid WorkId { get; set; }

    public Guid FromEmployeeId { get; set; }

    public Guid ToEmployeeId { get; set; }

    public string Reason { get; set; } = null!;

    public DateTimeOffset HandoverAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public HandoverStatus Status { get; set; }

    public virtual Employee FromEmployee { get; set; } = null!;

    public virtual Employee ToEmployee { get; set; } = null!;

    public virtual Work Work { get; set; } = null!;
}
