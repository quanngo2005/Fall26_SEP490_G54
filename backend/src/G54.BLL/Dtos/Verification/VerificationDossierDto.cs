namespace G54.BLL.Dtos.Verification;

public sealed record SubmitterInfoDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobPosition,
    string Department);

public sealed record KpiItemDto(
    Guid? KpiDefinitionId,
    string KpiCode,
    string KpiName,
    decimal DeclaredValue,
    string Unit);

public sealed record EvidenceItemDto(
    Guid FileId,
    string FileName,
    long? FileSize,
    string? MimeType,
    string PreviewUrl,
    string? DownloadUrl);

public sealed record ControlStandardDto(
    string Id,
    string Name,
    bool Passed);

public sealed record VerificationDossierDto(
    Guid SubmissionId,
    string SubmissionCode,
    int SubmissionNo,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    SubmitterInfoDto Submitter,
    DateTimeOffset SubmittedAt,
    KpiItemDto? KpiItem,
    string? Narrative,
    List<EvidenceItemDto> EvidenceFiles,
    List<ControlStandardDto> ControlStandards,
    string Status);
