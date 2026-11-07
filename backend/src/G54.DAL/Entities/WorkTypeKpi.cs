using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class WorkTypeKpi
{
    public Guid Id { get; set; }

    public Guid WorkTypeId { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public bool IsRequired { get; set; }

    public decimal Multiplier { get; set; } = 1m;

    /// <summary>COUNT credits 1 per imported transaction; AMOUNT credits the file's amount.</summary>
    public string ValueSource { get; set; } = KpiValueSource.Count;

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual WorkType WorkType { get; set; } = null!;
}
