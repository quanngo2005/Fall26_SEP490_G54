using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class WorkResultItem
{
    public Guid Id { get; set; }

    public Guid VerifiedWorkResultId { get; set; }

    public Guid KpiDefinitionId { get; set; }

    public decimal Value { get; set; }

    public decimal? Denominator { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual ActualProgress? ActualProgress { get; set; }

    public virtual KpiDefinition KpiDefinition { get; set; } = null!;

    public virtual VerifiedWorkResult VerifiedWorkResult { get; set; } = null!;
}
