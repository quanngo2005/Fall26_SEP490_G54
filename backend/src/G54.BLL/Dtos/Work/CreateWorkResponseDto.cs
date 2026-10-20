namespace G54.BLL.Dtos.Work;

/// <summary>
/// Hiện chưa được sử dụng bởi endpoint nào. Endpoint POST /works trả WorkDetailDto.
/// </summary>
public sealed record CreateWorkResponseDto(
    Guid WorkId,
    string WorkCode,
    string Status,
    DateTimeOffset UpdatedAt,
    string Message);
