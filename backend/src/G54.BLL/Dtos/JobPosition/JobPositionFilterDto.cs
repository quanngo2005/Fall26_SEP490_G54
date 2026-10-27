namespace G54.BLL.Dtos.JobPosition;

public sealed record JobPositionFilterDto(
    string? Keyword = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 50);

