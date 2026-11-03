using G54.BLL.Common.Import.Pipeline;

namespace G54.BLL.Common.Import.Rules;

/// <summary>
/// Flags every row that shares a non-blank key with another row in the file (not just the second occurrence),
/// so the preview makes the whole duplicate group visible.
/// </summary>
public sealed class DuplicateInFileRule<TRow, TKey>(
    string column,
    Func<TRow, TKey?> keySelector,
    string code,
    string messageFormat,
    IEqualityComparer<TKey>? comparer = null) : IImportRule<TRow>
    where TKey : notnull
{
    public int Order => -50;

    public void Apply(ImportValidationContext<TRow> context)
    {
        var groups = context.Rows
            .Select(row => (Row: row, Key: keySelector(row.Data)))
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, comparer);

        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            foreach (var (row, key) in group)
            {
                row.AddError(column, code, string.Format(System.Globalization.CultureInfo.InvariantCulture, messageFormat, key));
            }
        }
    }
}
