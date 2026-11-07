using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Common.Import.Template;

/// <summary>
/// A declared set of columns, used both to map an uploaded file's header row (order-independent, by name)
/// and to generate the downloadable template (same declaration, so the two can never drift apart).
/// </summary>
public sealed class ImportTemplate<TRow>
    where TRow : new()
{
    public required IReadOnlyList<ImportColumn<TRow>> Columns { get; init; }

    private static string Normalize(string header) => header.Trim().TrimEnd('*').Trim();

    /// <summary>
    /// Matches the file's header row to declared columns by normalized name. Throws
    /// <see cref="BusinessRuleException"/> (<see cref="ErrorCodes.ImportTemplateInvalid"/>) when a required
    /// column is missing.
    /// </summary>
    public Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> fileHeaders)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fileHeaders.Count; i++)
        {
            var normalized = Normalize(fileHeaders[i] ?? string.Empty);
            if (normalized.Length > 0)
            {
                index.TryAdd(normalized, i);
            }
        }

        var missing = Columns
            .Where(c => c.Required && !index.ContainsKey(Normalize(c.Header)))
            .Select(c => c.Header)
            .ToList();

        if (missing.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.ImportTemplateInvalid,
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
            if (headerIndex.TryGetValue(Normalize(column.Header), out var columnIndex) && columnIndex < raw.Cells.Count)
            {
                value = raw.Cells[columnIndex]?.Trim();
            }

            column.Apply(row, value);
        }

        return row;
    }
}
