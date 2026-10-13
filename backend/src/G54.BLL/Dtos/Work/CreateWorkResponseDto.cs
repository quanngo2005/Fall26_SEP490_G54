namespace G54.BLL.Dtos.Work;

public sealed record CreateWorkResponseDto(
    Guid WorkId,
    string WorkCode,
    string Status,
    DateTimeOffset UpdatedAt,
    string Message);
