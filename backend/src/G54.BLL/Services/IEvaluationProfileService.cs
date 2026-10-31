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

    /// <summary>The root nodes of the profile's tree, each with its children. Returns null when the profile does not exist.</summary>
    Task<List<EvaluationProfileItemDto>?> GetItemsAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<EvaluationProfileItemDto> AddItemAsync(
        Guid profileId,
        AddEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default);

    Task<EvaluationProfileItemDto> UpdateItemAsync(
        Guid profileId,
        Guid itemId,
        UpdateEvaluationProfileItemDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the node and every node beneath it.</summary>
    Task RemoveItemAsync(Guid profileId, Guid itemId, CancellationToken cancellationToken = default);
}
