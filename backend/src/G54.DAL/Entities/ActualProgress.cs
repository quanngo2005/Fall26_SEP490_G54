using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class ActualProgress
{
    public Guid Id { get; set; }

    public Guid TargetAssignmentId { get; set; }

    public DateOnly ProgressDate { get; set; }

    public decimal Value { get; set; }

    public decimal? Denominator { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ActualProgressStatus Status { get; set; }

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();

    public virtual TargetAssignment TargetAssignment { get; set; } = null!;

    public virtual ICollection<WorkResultItem> WorkResultItems { get; set; } = new List<WorkResultItem>();
}
