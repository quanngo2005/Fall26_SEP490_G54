using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Appeal
{
    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid ResultSubmissionId { get; set; }

    public string Reason { get; set; } = null!;

    public DateTimeOffset FiledAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public AppealStatus Status { get; set; }

    public virtual Employee Employee { get; set; } = null!;

    public virtual ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();

    public virtual ResultSubmission ResultSubmission { get; set; } = null!;
}
