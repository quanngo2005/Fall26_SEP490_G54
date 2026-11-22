namespace G54.BLL.Dtos.Handover;

/// <summary>Body of <c>POST /works/{workId}/handovers</c>.</summary>
public sealed record CreateHandoverRequestDto
{
    public Guid? ToEmployeeId { get; init; }

    public string? Reason { get; init; }

    /// <summary>Monetary value of the work being handed over; checked against the BR-01 threshold, not stored.</summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Optional 0-100 share of the KPI points kept by the referrer. Only callers with <c>handover:set_share</c>
    /// (Manager/Admin) may send it; when omitted the recipient receives 100%.
    /// </summary>
    public decimal? ReferrerSharePercent { get; init; }
}

/// <summary>Body of <c>POST /handovers/{id}/manager/reassign</c>.</summary>
public sealed record ReassignHandoverRequestDto
{
    public Guid? ToEmployeeId { get; init; }
}

public sealed record HandoverDetailDto(
    Guid Id,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    Guid FromEmployeeId,
    string FromEmployeeName,
    Guid ToEmployeeId,
    string ToEmployeeName,
    string Reason,
    decimal ReferrerSharePercent,
    string Status,
    DateTimeOffset HandoverAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A colleague a work can be handed over to (<c>GET /handovers/recipients</c>).</summary>
public sealed record HandoverRecipientDto(
    Guid Id,
    string EmployeeCode,
    string FullName,
    string JobPositionTitle);

/// <summary>Query parameters for <c>GET /handovers</c> (SCR-WRK-05).</summary>
public sealed record HandoverListQueryDto
{
    public string? Direction { get; init; }
    public string? Status { get; init; }
    public Guid? FromEmployeeId { get; init; }
    public string? KpiStatus { get; init; }
    public string? Keyword { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Handover row &amp; detail payload for SCR-WRK-05 and SCR-WRK-05-D1.</summary>
public sealed record HandoverListItemDto(
    Guid Id,
    string HandoverCode,
    Guid WorkId,
    string WorkCode,
    string WorkTitle,
    string WorkStatus,
    string? WorkTypeName,
    string? CustomerName,
    string? CustomerCif,
    decimal? TargetValue,
    DateOnly? PlannedEndDate,
    Guid FromEmployeeId,
    string FromEmployeeCode,
    string FromEmployeeName,
    string FromJobPositionTitle,
    Guid ToEmployeeId,
    string ToEmployeeCode,
    string ToEmployeeName,
    string ToJobPositionTitle,
    string Reason,
    decimal ReferrerSharePercent,
    string Status,
    string KpiStatus,
    DateTimeOffset HandoverAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset UpdatedAt,
    bool IsReadOnly);
