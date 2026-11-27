using G54.BLL.Dtos.DataImport;

namespace G54.BLL.Services;

/// <summary>
/// Campaign customer Excel import (docs/api/CAMPAIGN_CONTRACT.md §5). Stateless: preview validates the uploaded
/// file without writing; commit takes the same file again, re-validates and writes in one transaction. An existing
/// CIF in the campaign updates name/phone/note only; its assignee and contact status are kept.
/// </summary>
public interface ICampaignCustomerImportService
{
    byte[] GetTemplate();

    Task<ImportPreviewDto> PreviewAsync(Guid campaignId, string fileName, Stream file, CancellationToken cancellationToken);

    Task<ImportCommitResultDto> CommitAsync(Guid campaignId, Stream file, Guid currentUserId, CancellationToken cancellationToken);
}
