using G54.BLL.Dtos.Campaign;

namespace G54.BLL.Services;

public interface ICampaignService
{
    Task<CampaignListOverviewDto> GetCampaignsAsync(
        string? search,
        string? status,
        Guid? objectiveId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CampaignDetailDto> GetCampaignByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CampaignDetailDto> CreateCampaignAsync(
        CreateCampaignRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CampaignDetailDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task DeleteCampaignAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<CampaignDecisionResultDto> DecideCampaignAsync(
        Guid id,
        CampaignDecisionRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);

    Task<BatchWorkGenerateResultDto> BatchGenerateWorkAsync(
        Guid id,
        BatchWorkGeneratorRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default);
}
