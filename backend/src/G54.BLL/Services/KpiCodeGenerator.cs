using System.Globalization;
using System.Text;

namespace G54.BLL.Services;

/// <summary>Builds a KPI code from the KPI name: upper case, no accents, words joined by underscores (<c>Tiền gửi - giới thiệu</c> becomes <c>TIEN_GUI_GIOI_THIEU</c>).</summary>
internal static class KpiCodeGenerator
{
    /// <summary>Leaves room under the 50 characters of the column for a numeric suffix.</summary>
    private const int MaxLength = 40;

    private const string Fallback = "KPI";

    public static string FromName(string? name)
    {
        var code = new StringBuilder();
        foreach (var letter in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (letter is '\u0111' or '\u0110')
            {
                code.Append('D');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                code.Append(char.IsAsciiLetterOrDigit(letter) ? char.ToUpperInvariant(letter) : '_');
            }
        }

        var words = code.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join('_', words);
        if (joined.Length > MaxLength)
        {
            joined = joined[..MaxLength].TrimEnd('_');
        }

        return joined.Length == 0 ? Fallback : joined;
    }
}
