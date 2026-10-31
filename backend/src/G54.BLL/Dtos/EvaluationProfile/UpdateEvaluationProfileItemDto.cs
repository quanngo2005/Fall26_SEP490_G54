namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>Changes one node of a profile; the node is identified by the route and its place in the tree never changes.</summary>
public sealed record UpdateEvaluationProfileItemDto
{
    /// <summary>New name of a group; null keeps the current one. Not allowed for a KPI leaf.</summary>
    public string? Name { get; init; }

    /// <summary>Greater than 0 and at most 100, up to 4 decimal places.</summary>
    public decimal WeightPercent { get; init; }

    /// <summary>1 or greater.</summary>
    public int DisplayOrder { get; init; }
}
