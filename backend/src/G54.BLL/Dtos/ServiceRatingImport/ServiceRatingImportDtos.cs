namespace G54.BLL.Dtos.ServiceRatingImport;

// docs/api/SERVICE_RATING_IMPORT_CONTRACT.md
public sealed record ServiceRatingDto(
    Guid Id,
    string TransactionCode,
    string EmployeeCode,
    DateOnly RatingDate,
    int Score,
    string? CustomerComment);
