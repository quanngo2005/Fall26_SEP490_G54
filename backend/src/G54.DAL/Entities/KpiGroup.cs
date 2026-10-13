using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class KpiGroup
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public KpiStatus Status { get; set; }

    public virtual ICollection<EvaluationProfileItem> EvaluationProfileItems { get; set; } = new List<EvaluationProfileItem>();

    public virtual ICollection<KpiDefinition> KpiDefinitions { get; set; } = new List<KpiDefinition>();
}
