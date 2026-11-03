using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Import.Reading;
using G54.BLL.Common.Import.Rules;
using G54.BLL.Common.Import.Template;

namespace G54.BLL.Common.Import.Pipeline;

/// <summary>
/// Template Method base for every import definition: read → map → load context (one batch query) →
/// validate (rule chain) → classify → (preview) or commit. Subclasses only implement the hooks below;
/// none of them can skip a step or reorder read vs. validate vs. commit.
/// </summary>
public abstract class ImportDefinition<TRow, TCtx> : IImportDefinition
    where TRow : new()
{
    public abstract string Type { get; }

    /// <summary>Declares the columns: header text, required flag, dropdown values, setter.</summary>
    protected abstract void ConfigureTemplate(TemplateBuilder<TRow> builder);

    /// <summary>Rule chain run over the whole batch; every rule adds errors, none of them short-circuit.</summary>
    protected abstract IReadOnlyList<IImportRule<TRow>> CreateRules();

    /// <summary>Loads everything the rules and classification need in one batch (no N+1 per row).</summary>
    protected abstract Task<TCtx> LoadContextAsync(IReadOnlyList<TRow> rows, CancellationToken cancellationToken);

    /// <summary>Called for every row that has no validation errors; sets <c>row.Action</c>.</summary>
    protected abstract void Classify(ImportRow<TRow> row, TCtx context);

    /// <summary>Writes every valid row inside a transaction; called only when no row is invalid.</summary>
    protected abstract Task<ImportCommitResult> CommitRowsAsync(
        IReadOnlyList<ImportRow<TRow>> rows, TCtx context, CancellationToken cancellationToken);

    /// <summary>Shapes a row for the API preview (column name -> display text).</summary>
    protected abstract Dictionary<string, string?> ToDictionary(TRow row);

    /// <summary>Optional records implied by the rows (e.g. branches/departments of employee rows), shown in the preview.</summary>
    protected virtual IReadOnlyList<ImportRelatedEntity> BuildRelatedEntities(
        IReadOnlyList<ImportRow<TRow>> rows, TCtx context) => [];

    private ImportTemplate<TRow> BuildTemplate()
    {
        var builder = new TemplateBuilder<TRow>();
        ConfigureTemplate(builder);
        return builder.Build();
    }

    public byte[] BuildTemplateFile() => TemplateWorkbookWriter.Write(BuildTemplate());

    private async Task<(List<ImportRow<TRow>> Rows, TCtx Context)> RunPipelineAsync(ExcelSheetResult sheet, CancellationToken ct)
    {
        var template = BuildTemplate();
        var headerIndex = template.BuildHeaderIndex(sheet.Headers);

        var rows = sheet.Rows
            .Select(raw => new ImportRow<TRow> { RowNumber = raw.RowNumber, Data = template.MapRow(raw, headerIndex) })
            .ToList();

        var context = await LoadContextAsync(rows.Select(r => r.Data).ToList(), ct);

        var validationContext = new ImportValidationContext<TRow> { Rows = rows, Context = context };
        foreach (var rule in CreateRules().OrderBy(r => r.Order))
        {
            rule.Apply(validationContext);
        }

        foreach (var row in rows)
        {
            if (row.HasErrors)
            {
                row.Action = ImportAction.Invalid;
            }
            else
            {
                Classify(row, context);
            }
        }

        return (rows, context);
    }

    private static ImportSummary Summarize(List<ImportRow<TRow>> rows) => new(
        rows.Count,
        rows.Count(r => r.Action != ImportAction.Invalid),
        rows.Count(r => r.Action == ImportAction.Invalid),
        rows.Count(r => r.Action == ImportAction.Create),
        rows.Count(r => r.Action == ImportAction.Update),
        rows.Count(r => r.Action == ImportAction.Unchanged));

    public async Task<ImportPipelineResult> PreviewAsync(ExcelSheetResult sheet, CancellationToken cancellationToken)
    {
        var (rows, context) = await RunPipelineAsync(sheet, cancellationToken);
        var rowResults = rows
            .Select(r => new ImportRowResult(r.RowNumber, r.Action.ToEnumString(), ToDictionary(r.Data), r.Errors.ToList()))
            .ToList();
        return new ImportPipelineResult(Summarize(rows), rowResults, BuildRelatedEntities(rows, context));
    }

    public async Task<ImportCommitResult> CommitAsync(ExcelSheetResult sheet, CancellationToken cancellationToken)
    {
        var (rows, context) = await RunPipelineAsync(sheet, cancellationToken);
        if (rows.Any(r => r.HasErrors))
        {
            throw new BusinessRuleException(ErrorCodes.DataImportHasErrors, null, 422);
        }

        return await CommitRowsAsync(rows, context, cancellationToken);
    }
}
