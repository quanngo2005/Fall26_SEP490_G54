using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class JobPosition
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public Guid EvaluationProfileId { get; set; }

    public DateTimeOffset LastSyncAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public RecordStatus Status { get; set; }

    public virtual ICollection<Employee> Employees { get; set; } = new List<Employee>();

    public virtual EvaluationProfile EvaluationProfile { get; set; } = null!;

    public virtual ICollection<JobPositionWorkType> JobPositionWorkTypes { get; set; } = new List<JobPositionWorkType>();
}
