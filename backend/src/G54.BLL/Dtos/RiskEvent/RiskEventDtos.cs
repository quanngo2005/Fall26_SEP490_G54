namespace G54.BLL.Dtos.RiskEvent;

/// <summary>Body of <c>POST /risk-events</c>. Contract: docs/api/RISK_EVENT_CONTRACT.md.</summary>
public sealed record CreateRiskEventRequestDto
{
    public Guid? EmployeeId { get; init; }

    public string? EventType { get; init; }

    public string? Description { get; init; }

    /// <summary><c>LOW | MEDIUM | HIGH | CRITICAL</c> (case-insensitive).</summary>
    public string? Severity { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }
}

public sealed record RiskEventDto(
    Guid Id,
    string Code,
    Guid EmployeeId,
    Guid RecordedBy,
    string EventType,
    string Description,
    string Severity,
    string Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset UpdatedAt,
    Guid? ContributionId);

/// <summary>Result of confirming a risk event.</summary>
public sealed record RiskEventConfirmationDto(
    Guid Id,
    string Code,
    Guid EmployeeId,
    string Severity,
    string Status,
    Guid ContributionId,
    Guid EvaluationPeriodId,
    Guid? PerformanceEvaluationId,
    decimal ComponentScore,
    DateTimeOffset UpdatedAt);
