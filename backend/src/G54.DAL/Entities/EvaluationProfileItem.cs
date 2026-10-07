using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class EvaluationProfileItem
{
    public Guid Id { get; set; }

    public Guid EvaluationProfileId { get; set; }

    public Guid KpiGroupId { get; set; }

    public decimal WeightPercent { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual EvaluationProfile EvaluationProfile { get; set; } = null!;

    public virtual KpiGroup KpiGroup { get; set; } = null!;

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();
}
