using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class EvaluationProfile
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int Version { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public EvaluationProfileStatus Status { get; set; }

    public virtual ICollection<EvaluationProfileItem> EvaluationProfileItems { get; set; } = new List<EvaluationProfileItem>();

    public virtual ICollection<JobPosition> JobPositions { get; set; } = new List<JobPosition>();

    public virtual ICollection<PerformanceEvaluation> PerformanceEvaluations { get; set; } = new List<PerformanceEvaluation>();
}
