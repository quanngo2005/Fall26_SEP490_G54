namespace G54.BLL.Common.Import.Pipeline;

/// <summary>A single parsed row carried through validation, classification and commit.</summary>
public sealed class ImportRow<TRow>
{
    public required int RowNumber { get; init; }

    public required TRow Data { get; init; }

    public List<ImportError> Errors { get; } = [];

    public ImportAction Action { get; set; } = ImportAction.Create;

    public bool HasErrors => Errors.Count > 0;

    public void AddError(string column, string code, string message) => Errors.Add(new ImportError(column, code, message));
}
