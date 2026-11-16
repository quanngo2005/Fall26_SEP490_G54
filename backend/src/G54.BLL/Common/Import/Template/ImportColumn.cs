namespace G54.BLL.Common.Import.Template;

/// <summary>
/// One declared column: the header text to match, optional header aliases, whether it is required,
/// the dropdown values to render in the generated template, and how a raw cell value is copied onto the row model.
/// </summary>
public sealed class ImportColumn<TRow>
{
    public required string Header { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public bool Required { get; init; } = true;

    public IReadOnlyList<string>? DropdownValues { get; init; }

    public required Action<TRow, string?> Apply { get; init; }
}
