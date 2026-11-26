using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G54.BLL.Dtos.SystemParameter;

#pragma warning disable CA1720 // Parameter type constants match the contract wire identifiers INTEGER/DECIMAL/STRING.
public static class SystemParameterValueTypes
{
    public const string Integer = "INTEGER";
    public const string Decimal = "DECIMAL";
    public const string Percent = "PERCENT";
    public const string Time = "TIME";
    public const string Boolean = "BOOLEAN";
    public const string String = "STRING";
}
#pragma warning restore CA1720

public static class SystemParameterGroups
{
    public const string Deadlines = "DEADLINES";
    public const string Appeals = "APPEALS";
    public const string ScoringQa = "SCORING_QA";
    public const string QuotaRanking = "QUOTA_RANKING";
    public const string ImportHandover = "IMPORT_HANDOVER";
    public const string DashboardSecurity = "DASHBOARD_SECURITY";
    public const string TargetAllocation = "TARGET_ALLOCATION";
}

/// <param name="Value">JSON number, boolean, or string depending on <paramref name="ValueType"/>.</param>
/// <param name="UpdatedBy">Username of the last editor.</param>
public sealed record SystemParameterDto(
    string Key,
    object Value,
    decimal? Min,
    decimal? Max,
    bool Locked,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ValueType = SystemParameterValueTypes.Integer,
    string Group = SystemParameterGroups.Deadlines);

public sealed record SystemParameterChangeDto
{
    [JsonConstructor]
    public SystemParameterChangeDto(string key, JsonElement value)
    {
        Key = key;
        Value = value;
        RawValue = UnwrapJsonElement(value);
    }

    public SystemParameterChangeDto(string key, object? value)
    {
        Key = key;
        RawValue = UnwrapJsonElement(value);
        Value = JsonSerializer.SerializeToElement(RawValue);
    }

    public SystemParameterChangeDto(string key, decimal value)
    {
        Key = key;
        RawValue = value;
        Value = JsonSerializer.SerializeToElement(value);
    }

    public string Key { get; init; }

    public JsonElement Value { get; init; }

    [JsonIgnore]
    public object? RawValue { get; init; }

    private static object? UnwrapJsonElement(object? raw)
    {
        if (raw is not JsonElement element)
        {
            return raw;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var dec) => dec,
            JsonValueKind.Number => (decimal)element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }
}

public sealed record UpdateSystemParametersDto(IReadOnlyList<SystemParameterChangeDto> Changes);
