using System.Globalization;
using System.Text.RegularExpressions;
using G54.BLL.Common.Extensions;

namespace G54.BLL.Common.Import.Parsing;

/// <summary>Reusable cell-level parsers. Parsing never throws: the caller reports a failed parse as a row error.</summary>
public static partial class CellParsers
{
    private static readonly string[] AcceptedDateFormats = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"];

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

    /// <summary>Parses a date cell accepting only <c>dd/MM/yyyy</c>, <c>d/M/yyyy</c>, and <c>yyyy-MM-dd</c>.</summary>
    public static bool TryParseDate(string? raw, out DateOnly value)
    {
        value = default;
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        return DateOnly.TryParseExact(
            text,
            AcceptedDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    /// <summary>
    /// Parses a numeric/amount cell: strips whitespace, accepts plain numbers (<c>15000000</c>, <c>10.5</c>),
    /// and Vietnamese (<c>15.000.000</c>, <c>15.000.000,50</c>) or US (<c>15,000,000</c>, <c>15,000,000.50</c>)
    /// thousands-separated numbers.
    /// </summary>
    public static bool TryParseAmount(string? raw, out decimal value)
    {
        value = default;
        var text = Whitespace().Replace(raw ?? string.Empty, string.Empty);
        if (text.Length == 0)
        {
            return false;
        }

        var normalized = NormalizeNumericText(text);
        return normalized is not null && decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string? NormalizeNumericText(string text)
    {
        if (DotThousandsInteger().IsMatch(text))
        {
            return text.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        if (CommaThousandsInteger().IsMatch(text))
        {
            return text.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        if (DotThousandsCommaDecimal().IsMatch(text))
        {
            return text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }

        if (CommaThousandsDotDecimal().IsMatch(text))
        {
            return text.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        if (PlainCommaDecimal().IsMatch(text))
        {
            return text.Replace(',', '.');
        }

        if (PlainNumber().IsMatch(text))
        {
            return text;
        }

        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[+-]?\d{1,3}(?:\.\d{3})+$")]
    private static partial Regex DotThousandsInteger();

    [GeneratedRegex(@"^[+-]?\d{1,3}(?:,\d{3})+$")]
    private static partial Regex CommaThousandsInteger();

    [GeneratedRegex(@"^[+-]?\d{1,3}(?:\.\d{3})+,\d+$")]
    private static partial Regex DotThousandsCommaDecimal();

    [GeneratedRegex(@"^[+-]?\d{1,3}(?:,\d{3})+\.\d+$")]
    private static partial Regex CommaThousandsDotDecimal();

    [GeneratedRegex(@"^[+-]?\d+,\d{1,2}$")]
    private static partial Regex PlainCommaDecimal();

    [GeneratedRegex(@"^[+-]?\d+(?:\.\d+)?$")]
    private static partial Regex PlainNumber();
}
