namespace G54.BLL.Dtos.BusinessObjective;

public sealed record BusinessObjectiveFilterDto
{
    /// <summary>Matches code or name.</summary>
    public string? Keyword { get; init; }

    /// <summary>UPPER_SNAKE status filter (DRAFT, ACTIVE, CLOSED, CANCELLED); unknown value is 400. Without it CANCELLED objectives are left out.</summary>
    public string? Status { get; init; }
}
