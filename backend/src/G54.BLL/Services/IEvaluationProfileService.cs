using G54.BLL.Common.Models;
using G54.BLL.Dtos.EvaluationProfile;

namespace G54.BLL.Services;

public interface IEvaluationProfileService
{
    Task<PagedResult<EvaluationProfileListItemDto>> GetProfilesAsync(
        EvaluationProfileFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<EvaluationProfileDto?> GetProfileByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EvaluationProfileDto> CreateProfileAsync(
        CreateEvaluationProfileDto dto,
        CancellationToken cancellationToken = default);

    Task<EvaluationProfileDto> UpdateProfileAsync(
        Guid id,
        UpdateEvaluationProfileDto dto,
        CancellationToken cancellationToken = default);

    Task ChangeStatusAsync(Guid id, EvaluationProfileStatusDto dto, CancellationToken cancellationToken = default);

    Task DeleteProfileAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the profile does not exist.</summary>
    Task<List<EvaluationProfileItemDto>?> GetItemsAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<EvaluationProfileItemDto> AddItemAsync(
        Guid profileId,
        EvaluationProfileItemInputDto dto,
        CancellationToken cancellationToken = default);

    Task<EvaluationProfileItemDto> UpdateItemAsync(
        Guid profileId,
        Guid kpiGroupId,
        UpdateEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default);

    Task RemoveItemAsync(Guid profileId, Guid kpiGroupId, CancellationToken cancellationToken = default);
}
