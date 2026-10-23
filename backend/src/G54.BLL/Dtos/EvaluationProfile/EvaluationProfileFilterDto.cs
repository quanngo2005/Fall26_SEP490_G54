namespace G54.BLL.Dtos.EvaluationProfile;

public sealed record EvaluationProfileFilterDto
{
    /// <summary>Matches profile code or name.</summary>
    public string? Keyword { get; init; }

    /// <summary>UPPER_SNAKE status filter (DRAFT, ACTIVE, INACTIVE, ARCHIVED); unknown value is 400.</summary>
    public string? Status { get; init; }

    public Guid? JobPositionId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
