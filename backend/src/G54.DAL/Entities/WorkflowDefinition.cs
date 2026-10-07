using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class WorkflowDefinition
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public WorkflowDefinitionStatus Status { get; set; }

    public virtual ICollection<ResultSubmission> ResultSubmissions { get; set; } = new List<ResultSubmission>();

    public virtual ICollection<WorkType> WorkTypes { get; set; } = new List<WorkType>();
}
