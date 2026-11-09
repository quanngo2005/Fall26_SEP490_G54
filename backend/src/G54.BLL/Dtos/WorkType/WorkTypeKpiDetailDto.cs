namespace G54.BLL.Dtos.WorkType;

public sealed record WorkTypeKpiDetailDto(
    Guid KpiDefinitionId,
    string KpiCode,
    string KpiName,
    bool IsRequired,
    decimal Multiplier,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);
