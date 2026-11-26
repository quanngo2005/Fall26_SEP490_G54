namespace G54.BLL.Services;

/// <summary>
/// Provides downloadable .xlsx template for customer service rating imports.
/// Contract: docs/api/SERVICE_RATING_IMPORT_CONTRACT.md (SRS UC29).
/// </summary>
public interface IServiceRatingImportTemplateProvider
{
    byte[] GetTemplate();
}
