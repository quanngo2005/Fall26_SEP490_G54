namespace G54.BLL.Services;

/// <summary>
/// Provides downloadable .xlsx template for Core Banking transaction imports.
/// Contract: docs/api/TRANSACTION_IMPORT_CONTRACT.md (SRS UC29).
/// </summary>
public interface ITransactionImportTemplateProvider
{
    byte[] GetTemplate();
}
