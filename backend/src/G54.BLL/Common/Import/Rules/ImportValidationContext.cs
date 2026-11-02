using G54.BLL.Common.Import.Pipeline;

namespace G54.BLL.Common.Import.Rules;

/// <summary>
/// Everything a rule needs: the whole batch of rows (so cross-row rules such as "duplicate in file" work)
/// and the definition-specific context object loaded once for the batch (cast by the rule that needs it).
/// </summary>
public sealed class ImportValidationContext<TRow>
{
    public required IReadOnlyList<ImportRow<TRow>> Rows { get; init; }

    public object? Context { get; init; }
}
