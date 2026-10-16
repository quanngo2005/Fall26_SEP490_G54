using G54.DAL.Entities;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record KpiDefinitionDto(
    Guid Id,
    Guid KpiGroupId,
    string KpiGroupName,
    string Code,
    string Name,
    string? Description,
    string? Formula,
    string Unit,
    KpiAggregationType AggregationType,
    KpiDirection Direction,
    decimal? MaxAchievementPercent,
    KpiStatus Status,
    DateTimeOffset UpdatedAt);
