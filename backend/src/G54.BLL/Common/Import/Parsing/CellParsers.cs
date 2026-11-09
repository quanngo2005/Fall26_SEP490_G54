using G54.BLL.Common.Extensions;

namespace G54.BLL.Common.Import.Parsing;

/// <summary>Reusable cell-level parsers. Parsing never throws: the caller reports a failed parse as a row error.</summary>
public static class CellParsers
{
    /// <summary>Matches the UPPER_SNAKE API vocabulary of <typeparamref name="TEnum"/> (see <see cref="EnumApiFormat"/>).</summary>
    public static bool TryParseEnum<TEnum>(string? raw, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(candidate.ToEnumString(), text, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }
}
