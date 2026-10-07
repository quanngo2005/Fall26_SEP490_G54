using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class WorkType
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public Guid WorkflowDefinitionId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public RecordStatus Status { get; set; }

    public virtual ICollection<CampaignWorkType> CampaignWorkTypes { get; set; } = new List<CampaignWorkType>();

    public virtual ICollection<JobPositionWorkType> JobPositionWorkTypes { get; set; } = new List<JobPositionWorkType>();

    public virtual ICollection<WorkTypeKpi> WorkTypeKpis { get; set; } = new List<WorkTypeKpi>();

    public virtual WorkflowDefinition WorkflowDefinition { get; set; } = null!;

    public virtual ICollection<Work> Works { get; set; } = new List<Work>();
}
