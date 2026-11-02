using System.Globalization;
using System.Text.RegularExpressions;
using G54.BLL.Common.Extensions;

namespace G54.BLL.Common.Import.Parsing;

/// <summary>
/// Reusable cell-level parsers shared by every import definition's rules. Parsing never throws: a failed
/// parse is reported by the rule that called it, as a row error, not as an exception.
/// </summary>
public static partial class CellParsers
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex CodePattern();

    /// <summary>Normalizes to UPPER and checks the shared org/employee code shape (max 50 chars).</summary>
    public static bool TryParseCode(string? raw, out string normalized)
    {
        normalized = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length is > 0 and <= 50 && CodePattern().IsMatch(normalized);
    }

    /// <summary>Normalizes to lower-case and checks a simple email shape (max 255 chars).</summary>
    public static bool TryParseEmail(string? raw, out string normalized)
    {
        normalized = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 255 && EmailPattern().IsMatch(normalized);
    }

    /// <summary>Accepts dd/MM/yyyy; real Excel date cells are already rendered that way by the sheet reader.</summary>
    public static bool TryParseDate(string? raw, out DateOnly value) =>
        DateOnly.TryParseExact((raw ?? string.Empty).Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

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
