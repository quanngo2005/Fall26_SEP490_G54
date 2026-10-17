namespace G54.BLL.Dtos.Submission;

public sealed record SubmissionContextDto(
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    string WorkStatus,
    Guid AssigneeEmployeeId,
    string AssigneeEmployeeName,
    string AssigneeEmployeeCode,
    Guid? WorkTypeId,
    string? WorkTypeName,
    int NextSubmissionNo,
    int PreviousSubmissionsCount,
    IReadOnlyList<SubmissionKpiCriteriaDto> KpiCriteria,
    IReadOnlyList<string> AllowedFileTypes,
    long MaxFileSizeBytes,
    int MaxFileCount);

public sealed record SubmissionKpiCriteriaDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string? Description,
    string Unit,
    string AggregationType,
    string Direction,
    bool IsRequired,
    string? Formula);
