namespace G54.BLL.Dtos.SystemParameters;

public sealed record SystemParameterDto(
    string Key,
    object Value,
    long? Min,
    long? Max,
    bool Locked,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed class SystemParameterChangeDto
{
    public string Key { get; set; } = string.Empty;
    public long Value { get; set; }
}

public sealed class SaveSystemParametersDto
{
    public List<SystemParameterChangeDto>? Changes { get; set; }
}
