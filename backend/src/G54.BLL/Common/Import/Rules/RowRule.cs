using G54.BLL.Common.Import.Pipeline;

namespace G54.BLL.Common.Import.Rules;

/// <summary>Convenience base for a rule that only looks at one row at a time.</summary>
public abstract class RowRule<TRow> : IImportRule<TRow>
{
    public virtual int Order => 0;

    public void Apply(ImportValidationContext<TRow> context)
    {
        foreach (var row in context.Rows)
        {
            ValidateRow(row, context);
        }
    }

    protected abstract void ValidateRow(ImportRow<TRow> row, ImportValidationContext<TRow> context);
}
