namespace G54.BLL.Dtos.Department;

/// <summary>Escalated has no data source yet and is always null; CompletedInPeriod is null without a period.</summary>
public sealed record DepartmentOperationalDto(
    int Waiting,
    int InProgress,
    int Overdue,
    int? Escalated,
    int? CompletedInPeriod);
