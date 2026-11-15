namespace G54.BLL.Dtos.SystemParameter;

/// <param name="Value">A JSON number for editable parameters, text for locked ones.</param>
/// <param name="UpdatedBy">Username of the last editor.</param>
public sealed record SystemParameterDto(
    string Key,
    object Value,
    decimal? Min,
    decimal? Max,
    bool Locked,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed record SystemParameterChangeDto(string Key, decimal Value);

public sealed record UpdateSystemParametersDto(IReadOnlyList<SystemParameterChangeDto> Changes);
