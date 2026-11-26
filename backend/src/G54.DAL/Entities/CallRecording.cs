using NpgsqlTypes;

namespace G54.DAL.Entities;

/// <summary>Stored as the <see cref="PgNameAttribute"/> text in a varchar column (not a PostgreSQL enum type).</summary>
public enum CallRecordingStatus
{
    /// <summary>Uploaded, waiting for the audio pipeline.</summary>
    [PgName("PENDING")] Pending,

    [PgName("PROCESSING")] Processing,

    /// <summary>Processed: the stored file is the PII-muted copy and the transcript and flags exist.</summary>
    [PgName("READY")] Ready,

    /// <summary>Final for this row; the submitter uploads again.</summary>
    [PgName("FAILED")] Failed,
}

public enum AiReviewFlagType
{
    [PgName("RECORDING_INTEGRITY")] RecordingIntegrity,
    [PgName("FACT_MISMATCH")] FactMismatch,
    [PgName("MISSING_DISCLOSURE")] MissingDisclosure,
    [PgName("ABSOLUTE_CLAIM")] AbsoluteClaim,
}

public enum AiReviewFlagStatus
{
    [PgName("PENDING")] Pending,
    [PgName("CONFIRMED")] Confirmed,
    [PgName("DISMISSED")] Dismissed,
}

/// <summary>A call recording a submitter attached to a result submission (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md).</summary>
public sealed class CallRecording
{
    public Guid Id { get; set; }

    public Guid ResultSubmissionId { get; set; }

    public Guid WorkId { get; set; }

    /// <summary>The work's matched transaction code at upload time (snapshot); null when none was matched.</summary>
    public string? TransactionRef { get; set; }

    public string? ProductCode { get; set; }

    /// <summary>Path relative to the evidence storage root: <c>call-recordings/{yyyy}/{MM}/{id}{ext}</c>.</summary>
    public string FilePath { get; set; } = null!;

    public string FileName { get; set; } = null!;

    public string MimeType { get; set; } = null!;

    public long SizeBytes { get; set; }

    /// <summary>Hex SHA-256 of the uploaded file.</summary>
    public string Sha256 { get; set; } = null!;

    /// <summary>Set by the audio pipeline.</summary>
    public int? DurationMs { get; set; }

    public CallRecordingStatus ProcessingStatus { get; set; }

    public string? ErrorCode { get; set; }

    public int Attempts { get; set; }

    public string? PipelineVersion { get; set; }

    public string? KbVersion { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Upload time + 1 year (BR-19).</summary>
    public DateTimeOffset RetainUntil { get; set; }

    public ResultSubmission ResultSubmission { get; set; } = null!;

    public Work Work { get; set; } = null!;

    public Employee UploadedByEmployee { get; set; } = null!;

    public CallTranscript? Transcript { get; set; }

    public ICollection<AiReviewFlag> Flags { get; set; } = new List<AiReviewFlag>();
}

/// <summary>The PII-masked transcript of a processed recording; one per recording.</summary>
public sealed class CallTranscript
{
    public Guid Id { get; set; }

    public Guid CallRecordingId { get; set; }

    public string AsrModel { get; set; } = null!;

    public string Language { get; set; } = null!;

    /// <summary>JSON array of masked segments (<c>startMs</c>, <c>endMs</c>, <c>speaker</c>, <c>textMasked</c>, ...).</summary>
    public string Segments { get; set; } = null!;

    public bool PiiMasked { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public CallRecording CallRecording { get; set; } = null!;
}

/// <summary>
/// A segment the AI suggests QA listen to again. It carries no severity: only a QA user turns it into a Risk Event.
/// </summary>
public sealed class AiReviewFlag
{
    public Guid Id { get; set; }

    /// <summary>Number shown as <c>AI-000123</c> (sequence <c>ai_review_flag_code_seq</c>).</summary>
    public long CodeNo { get; set; }

    public Guid CallRecordingId { get; set; }

    /// <summary>The employee the recording belongs to (the submitter).</summary>
    public Guid EmployeeId { get; set; }

    public AiReviewFlagType FlagType { get; set; }

    public string RuleCode { get; set; } = null!;

    public string Detector { get; set; } = null!;

    /// <summary>Sort key between 0 and 1; never shown as a score.</summary>
    public decimal RankScore { get; set; }

    public int StartMs { get; set; }

    public int EndMs { get; set; }

    /// <summary>Masked text, a substring of the masked segments it points to.</summary>
    public string EvidenceText { get; set; } = null!;

    /// <summary>JSON array of knowledge-base references.</summary>
    public string KbRefs { get; set; } = null!;

    /// <summary>JSON object with detector-specific details.</summary>
    public string Details { get; set; } = null!;

    public AiReviewFlagStatus Status { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? DismissReason { get; set; }

    public Guid? RiskEventId { get; set; }

    public Guid? QualityFlagId { get; set; }

    public string PipelineVersion { get; set; } = null!;

    public string KbVersion { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public CallRecording CallRecording { get; set; } = null!;

    public Employee Employee { get; set; } = null!;

    public Employee? ReviewedByEmployee { get; set; }
}
