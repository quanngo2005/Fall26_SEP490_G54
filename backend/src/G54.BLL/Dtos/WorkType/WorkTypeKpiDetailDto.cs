namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeKpiDetailDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    bool IsRequired,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
