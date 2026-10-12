using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class JobPositionWorkType
{
    public Guid Id { get; set; }

    public Guid JobPositionId { get; set; }

    public Guid WorkTypeId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual JobPosition JobPosition { get; set; } = null!;

    public virtual WorkType WorkType { get; set; } = null!;
}
