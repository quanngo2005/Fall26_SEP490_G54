namespace G54.BLL.Common.Import.Template;

/// <summary>Fluent builder for <see cref="ImportTemplate{TRow}"/>.</summary>
public sealed class TemplateBuilder<TRow>
    where TRow : new()
{
    private readonly List<ImportColumn<TRow>> _columns = [];

    public TemplateBuilder<TRow> Column(
        string header,
        Action<TRow, string?> apply,
        bool required = true,
        IReadOnlyList<string>? dropdown = null)
    {
        _columns.Add(new ImportColumn<TRow>
        {
            Header = header,
            Apply = apply,
            Required = required,
            DropdownValues = dropdown,
        });
        return this;
    }

    public ImportTemplate<TRow> Build() => new() { Columns = _columns };
}
