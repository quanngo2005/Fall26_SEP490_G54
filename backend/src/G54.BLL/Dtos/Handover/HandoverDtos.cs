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
    DateTimeOffset UpdatedAt);
