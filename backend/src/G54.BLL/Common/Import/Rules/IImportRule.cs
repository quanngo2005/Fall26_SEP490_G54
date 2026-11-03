namespace G54.BLL.Common.Import.Rules;

/// <summary>
/// One link in the validation chain. Every rule runs over the whole batch and only ever adds errors —
/// it never stops the chain — so a single preview reports every problem at once.
/// </summary>
public interface IImportRule<TRow>
{
    /// <summary>Lower runs first. Field-format rules should run before rules that read the format's output.</summary>
    int Order { get; }

    void Apply(ImportValidationContext<TRow> context);
}
