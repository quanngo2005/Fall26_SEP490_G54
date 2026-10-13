using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class ObjectiveKpi
{
    public Guid Id { get; set; }

    public Guid BusinessObjectiveId { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual BusinessObjective BusinessObjective { get; set; } = null!;

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;
}
