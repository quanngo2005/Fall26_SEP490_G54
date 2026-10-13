using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class ResultSubmission
{
    public Guid Id { get; set; }

    public Guid WorkId { get; set; }

    public Guid SubmitterEmployeeId { get; set; }

    public Guid? WorkflowDefinitionId { get; set; }

    public int SubmissionNo { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public SubmissionStatus Status { get; set; }

    public virtual ICollection<Appeal> Appeals { get; set; } = new List<Appeal>();

    public virtual ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();

    public virtual Employee SubmitterEmployee { get; set; } = null!;

    public virtual ICollection<Verification> Verifications { get; set; } = new List<Verification>();

    public virtual Work Work { get; set; } = null!;

    public virtual WorkflowDefinition? WorkflowDefinition { get; set; }
}
