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

    /// <summary>
    /// Expected result of this work in the unit of the work type's KPI (accounts, VND billion, ...); null when no
    /// target was set. Set when work is generated in batch from a campaign.
    /// </summary>
    public decimal? TargetValue { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the work was assigned (created); rows that predate the column were backfilled from <see cref="UpdatedAt"/>.</summary>
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>The customer the work is for (SCR-WRK-03); free text and optional.</summary>
    public string? CustomerName { get; set; }

    /// <summary>The customer's CIF code, as typed by whoever assigned the work.</summary>
    public string? CustomerCif { get; set; }

    public WorkPriority Priority { get; set; }
    public WorkStatus Status { get; set; }

    public virtual Employee AssigneeEmployee { get; set; } = null!;

    public virtual BusinessCampaign? BusinessCampaign { get; set; }

    public virtual Employee CreatedByNavigation { get; set; } = null!;

    public virtual ICollection<WorkChecklistItem> ChecklistItems { get; set; } = new List<WorkChecklistItem>();

    public virtual ICollection<WorkActivity> Activities { get; set; } = new List<WorkActivity>();

    public virtual ICollection<Work> InverseParentWork { get; set; } = new List<Work>();

    public virtual Work? ParentWork { get; set; }

    public virtual ICollection<ResultSubmission> ResultSubmissions { get; set; } = new List<ResultSubmission>();

    public virtual ICollection<TaskHandover> TaskHandovers { get; set; } = new List<TaskHandover>();

    public virtual WorkType? WorkType { get; set; }
}
