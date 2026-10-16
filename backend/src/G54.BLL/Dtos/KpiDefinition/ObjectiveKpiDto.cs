namespace G54.BLL.Dtos.KpiDefinition;

public sealed record ObjectiveKpiDto(
    Guid Id,
    Guid BusinessObjectiveId,
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    string Unit);
