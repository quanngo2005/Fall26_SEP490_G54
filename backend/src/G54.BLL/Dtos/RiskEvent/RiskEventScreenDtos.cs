namespace G54.BLL.Dtos.RiskEvent;

public sealed class RiskEventFilterDto
{
    /// <summary>Only events whose violation date is inside this evaluation period.</summary>
    public Guid? PeriodId { get; set; }

    /// <summary>The department of the employee, including its sub-departments.</summary>
    public Guid? DepartmentId { get; set; }

    /// <summary>LOW, MEDIUM, HIGH or CRITICAL.</summary>
    public string? Severity { get; set; }

    /// <summary>RECORDED or REVERSED.</summary>
    public string? State { get; set; }

    /// <summary>Code (<c>RSK-0045</c> or <c>45</c>), employee code or name, work code.</summary>
    public string? Search { get; set; }

    /// <summary>Column to sort by: code, occurredAt (default), employee, department, type, severity, penalty, work or status.</summary>
    public string? SortBy { get; set; }

    /// <summary>asc or desc (default: desc for occurredAt and penalty, asc otherwise).</summary>
    public string? SortDir { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed record RiskEventListItemDto(
    Guid Id,
    string Code,
    DateTimeOffset OccurredAt,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    Guid DepartmentId,
    string DepartmentName,
    string EventType,
    string Severity,
    decimal PenaltyPoints,
    Guid? WorkId,
    string? WorkCode,
    string Status,
    string State,
    DateTimeOffset CreatedAt);

public sealed record RiskEventCountsDto(int Recorded, int Reversed);

public sealed record RiskEventDepartmentDto(Guid Id, string Name);

public sealed record RiskEventListDto(
    List<RiskEventListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    RiskEventCountsDto Counts,
    List<RiskEventDepartmentDto> Departments);

public sealed record RiskEventAttachmentDto(Guid Id, string FileName, long FileSize, string MimeType);

public sealed record RiskEventDetailsDto(
    RiskEventListItemDto Item,
    string? JobPositionTitle,
    string Description,
    string? PeriodCode,
    string? PeriodName,
    string RecordedByName,
    DateTimeOffset RecordedAt,
    DateTimeOffset? ReversedAt,
    string? ReversedByName,
    string? ReversalReason,
    List<RiskEventAttachmentDto> Attachments,
    bool CanReverse);

public sealed record ReverseRiskEventDto(string? Reason);
