namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>
/// Adds one node to a profile. A null `ParentItemId` adds a root group; otherwise the parent must be a group of the
/// profile. Exactly one of `Name` (a group) and `KpiDefinitionId` (a KPI leaf) is set.
/// </summary>
public sealed record AddEvaluationProfileItemDto
{
    public Guid? ParentItemId { get; init; }

    public string? Name { get; init; }

    public Guid? KpiDefinitionId { get; init; }

    /// <summary>Greater than 0 and at most 100, up to 4 decimal places.</summary>
    public decimal WeightPercent { get; init; }

    /// <summary>1 or greater.</summary>
    public int DisplayOrder { get; init; }
}
