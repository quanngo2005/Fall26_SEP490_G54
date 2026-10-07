using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Evidence
{
    public Guid Id { get; set; }

    public Guid? ResultSubmissionId { get; set; }

    public Guid? RiskEventId { get; set; }

    public Guid? AppealId { get; set; }

    public string FileName { get; set; } = null!;

    public string FilePath { get; set; } = null!;

    public string? MimeType { get; set; }

    public long? FileSize { get; set; }

    public string? Description { get; set; }

    public Guid UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public virtual Appeal? Appeal { get; set; }

    public virtual ResultSubmission? ResultSubmission { get; set; }

    public virtual RiskEvent? RiskEvent { get; set; }

    public virtual Employee UploadedByNavigation { get; set; } = null!;
}
