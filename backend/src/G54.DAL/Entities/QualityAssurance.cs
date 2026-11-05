using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>Stored as the <see cref="PgNameAttribute"/> text in a varchar column (not a PostgreSQL enum type).</summary>
public enum QualityFlagSeverity
{
    [PgName("LOW")] Low,
    [PgName("MEDIUM")] Medium,
    [PgName("HIGH")] High,
}

public enum QualityFlagStatus
{
    /// <summary>Raised by QA, waiting for a decision (request rework or dismiss).</summary>
    [PgName("OPEN")] Open,

    /// <summary>QA sent the work back to the employee; the verified result was invalidated.</summary>
    [PgName("REWORK_REQUESTED")] ReworkRequested,

    [PgName("DISMISSED")] Dismissed,
}

public enum QaSampleStatus
{
    [PgName("PENDING")] Pending,
    [PgName("PASSED")] Passed,
    [PgName("FAILED")] Failed,
}

/// <summary>A quality concern QA raised about an approved result submission (and therefore about its submitter).</summary>
public sealed class QualityFlag
{
    public Guid Id { get; set; }

    public Guid ResultSubmissionId { get; set; }

    /// <summary>The employee whose work is flagged (the submitter).</summary>
    public Guid EmployeeId { get; set; }

    public Guid FlaggedByEmployeeId { get; set; }

    /// <summary>The QA re-verification this flag came from; null when QA flagged the result by hand.</summary>
    public Guid? QaSampleId { get; set; }

    public QualityFlagSeverity Severity { get; set; }

    public QualityFlagStatus Status { get; set; }

    public string Reason { get; set; } = null!;

    public string? ReworkComment { get; set; }

    public DateTimeOffset? ReworkRequestedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ResultSubmission ResultSubmission { get; set; } = null!;

    public Employee Employee { get; set; } = null!;

    public Employee FlaggedByEmployee { get; set; } = null!;

    public QaSample? QaSample { get; set; }
}

/// <summary>
/// An approved result picked at random (BR-32) for QA to re-verify. At most one sample per submission.
/// </summary>
public sealed class QaSample
{
    public Guid Id { get; set; }

    public Guid ResultSubmissionId { get; set; }

    public Guid SubmitterEmployeeId { get; set; }

    public QaSampleStatus Status { get; set; }

    public DateTimeOffset SampledAt { get; set; }

    public Guid? ReviewedByEmployeeId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? Comment { get; set; }

    public ResultSubmission ResultSubmission { get; set; } = null!;

    public Employee SubmitterEmployee { get; set; } = null!;

    public Employee? ReviewedByEmployee { get; set; }

    public QualityFlag? QualityFlag { get; set; }
}
