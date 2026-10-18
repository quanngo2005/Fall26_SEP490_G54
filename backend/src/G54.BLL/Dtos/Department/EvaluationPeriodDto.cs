namespace G54.BLL.Dtos.Department;

public sealed record EvaluationPeriodDto(
    Guid Id,
    string Code,
    string Name,
    string PeriodType,
    DateOnly StartDate,
    DateOnly EndDate);
