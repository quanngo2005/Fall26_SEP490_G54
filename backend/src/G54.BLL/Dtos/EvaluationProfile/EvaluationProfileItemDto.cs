namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>
/// A node of the profile's weight tree. A group has `Name` and `Children`; a KPI leaf has `KpiDefinitionId`
/// (with its code and name) and no children. `WeightPercent` is relative to the node's siblings.
/// </summary>
public sealed record EvaluationProfileItemDto(
    Guid Id,
    Guid? ParentItemId,
    string? Name,
    Guid? KpiDefinitionId,
    string? KpiCode,
    string? KpiName,
    decimal WeightPercent,
    int DisplayOrder,
    List<EvaluationProfileItemDto> Children);
