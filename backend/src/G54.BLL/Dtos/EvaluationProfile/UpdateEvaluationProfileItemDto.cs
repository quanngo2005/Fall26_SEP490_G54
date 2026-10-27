namespace G54.BLL.Dtos.EvaluationProfile;

/// <summary>Changes one item of a profile; the KPI group is identified by the route and never changes.</summary>
public sealed record UpdateEvaluationProfileItemDto
{
    /// <summary>Greater than 0 and at most 100, up to 4 decimal places.</summary>
    public decimal WeightPercent { get; init; }

    /// <summary>1 or greater.</summary>
    public int DisplayOrder { get; init; }
}
