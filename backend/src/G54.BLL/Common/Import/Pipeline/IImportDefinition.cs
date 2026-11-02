using G54.BLL.Common.Import.Reading;

namespace G54.BLL.Common.Import.Pipeline;

/// <summary>
/// Non-generic contract the façade (<c>IDataImportService</c>) resolves by <see cref="Type"/>. Every
/// concrete import (Employee/Organization today, Transaction/Service-rating later) implements this via
/// <see cref="ImportDefinition{TRow,TCtx}"/> rather than directly.
/// </summary>
public interface IImportDefinition
{
    /// <summary>Stable route segment, e.g. <c>"employee-organization"</c>.</summary>
    string Type { get; }

    byte[] BuildTemplateFile();

    Task<ImportPipelineResult> PreviewAsync(ExcelSheetResult sheet, CancellationToken cancellationToken);

    /// <summary>Re-validates against current DB state (TOCTOU) then commits; throws when any row is still invalid.</summary>
    Task<ImportCommitResult> CommitAsync(ExcelSheetResult sheet, CancellationToken cancellationToken);
}
