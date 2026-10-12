using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class WorkTypeKpi
{
    public Guid Id { get; set; }

    public Guid WorkTypeId { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public bool IsRequired { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual WorkType WorkType { get; set; } = null!;
}
