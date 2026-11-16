using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Common.Import.Template;

/// <summary>Non-generic header normalizer and index helpers shared by <see cref="ImportTemplate{TRow}"/> and sheet readers.</summary>
public static partial class ImportHeaderNormalizer
{
    /// <summary>Lower-case, accent-free, single-spaced text without "(...)" notes, "*" or ":".</summary>
    public static string Normalize(string text)
    {
        var stripped = NoteOrMarker().Replace(text, " ").Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(stripped.Length);
        foreach (var ch in stripped)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return Whitespace().Replace(builder.ToString(), " ").Trim();
    }

    /// <summary>Returns whether the declared column was present in the uploaded file's header row.</summary>
    public static bool HasColumn(string declaredHeader, IReadOnlyDictionary<string, int> headerIndex) =>
        headerIndex.ContainsKey(Normalize(declaredHeader));

    /// <summary>
    /// Returns the header text as written in the uploaded file for <paramref name="declaredHeader"/>,
    /// falling back to <paramref name="declaredHeader"/> when the column was absent.
    /// </summary>
    public static string GetMatchedHeader(
        string declaredHeader,
        IReadOnlyList<string> fileHeaders,
        IReadOnlyDictionary<string, int> headerIndex)
    {
        if (headerIndex.TryGetValue(Normalize(declaredHeader), out var colIndex)
            && colIndex >= 0 && colIndex < fileHeaders.Count)
        {
            var raw = fileHeaders[colIndex]?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                return raw;
            }
        }

        return declaredHeader;
    }

    [GeneratedRegex(@"\([^)]*\)|[*:]")]
    private static partial Regex NoteOrMarker();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// A declared set of columns, used both to map an uploaded file's header row (order-independent, by name or alias)
/// and to generate the downloadable template (same declaration, so the two can never drift apart).
/// </summary>
public sealed class ImportTemplate<TRow>
    where TRow : new()
{
    public required IReadOnlyList<ImportColumn<TRow>> Columns { get; init; }

    /// <summary>
    /// Matches the file's header row to declared columns by normalized name or alias. Throws
    /// <see cref="BusinessRuleException"/> (<paramref name="missingErrorCode"/>) when a required column is missing.
    /// Keys of the returned dictionary are <c>ImportHeaderNormalizer.Normalize(column.Header)</c>.
    /// </summary>
    public Dictionary<string, int> BuildHeaderIndex(
        IReadOnlyList<string> fileHeaders,
        string missingErrorCode = ErrorCodes.ImportTemplateInvalid)
    {
        var fileIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fileHeaders.Count; i++)
        {
            var normalized = ImportHeaderNormalizer.Normalize(fileHeaders[i] ?? string.Empty);
            if (normalized.Length > 0)
            {
                fileIndex.TryAdd(normalized, i);
            }
        }

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var column in Columns)
        {
            var canonical = ImportHeaderNormalizer.Normalize(column.Header);
            var matched = CandidateNames(column)
                .Select(ImportHeaderNormalizer.Normalize)
                .Where(n => n.Length > 0)
                .Select(n => fileIndex.TryGetValue(n, out var colIdx) ? (int?)colIdx : null)
                .FirstOrDefault(colIdx => colIdx is not null);

            if (matched is { } columnIndex)
            {
                index[canonical] = columnIndex;
            }
            else if (column.Required)
            {
                missing.Add(column.Header);
            }
        }

        if (missing.Count > 0)
        {
            throw new BusinessRuleException(
                missingErrorCode,
                $"File thiếu cột bắt buộc: {string.Join(", ", missing)}.",
                400);
        }

        return index;
    }

    public TRow MapRow(RawRow raw, Dictionary<string, int> headerIndex)
    {
        var row = new TRow();
        foreach (var column in Columns)
        {
            string? value = null;
            if (headerIndex.TryGetValue(ImportHeaderNormalizer.Normalize(column.Header), out var columnIndex)
                && columnIndex < raw.Cells.Count)
            {
                value = raw.Cells[columnIndex]?.Trim();
            }

            column.Apply(row, value);
        }

        return row;
    }

    private static IEnumerable<string> CandidateNames(ImportColumn<TRow> column)
    {
        yield return column.Header;
        foreach (var alias in column.Aliases)
        {
            yield return alias;
        }
    }
}
