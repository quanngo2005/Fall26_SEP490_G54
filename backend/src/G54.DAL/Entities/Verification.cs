using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Verification
{
    public Guid Id { get; set; }

    public Guid ResultSubmissionId { get; set; }

    public Guid VerifierEmployeeId { get; set; }

    public int RoundNo { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public VerificationDecision Decision { get; set; }

    public virtual ResultSubmission ResultSubmission { get; set; } = null!;

    public virtual VerifiedWorkResult? VerifiedWorkResult { get; set; }

    public virtual Employee VerifierEmployee { get; set; } = null!;
}
