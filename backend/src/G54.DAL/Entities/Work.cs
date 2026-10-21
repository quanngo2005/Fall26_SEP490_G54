using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Work : IBaseEntity
{
    public Guid Id { get; set; }

    public Guid? WorkTypeId { get; set; }

    public Guid? BusinessCampaignId { get; set; }

    public Guid AssigneeEmployeeId { get; set; }

    public Guid? ParentWorkId { get; set; }

    public Guid CreatedBy { get; set; }

    public string Code { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public decimal? EstimatedHours { get; set; }

    public DateOnly? PlannedStartDate { get; set; }

    public DateOnly? PlannedEndDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public WorkPriority Priority { get; set; }
    public WorkStatus Status { get; set; }

    public virtual Employee AssigneeEmployee { get; set; } = null!;

    public virtual BusinessCampaign? BusinessCampaign { get; set; }

    public virtual Employee CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<Work> InverseParentWork { get; set; } = new List<Work>();

    public virtual Work? ParentWork { get; set; }

    public virtual ICollection<ResultSubmission> ResultSubmissions { get; set; } = new List<ResultSubmission>();

    public virtual ICollection<TaskHandover> TaskHandovers { get; set; } = new List<TaskHandover>();

    public virtual WorkType? WorkType { get; set; }
}
