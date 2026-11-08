using G54.BLL.Dtos.BusinessObjective;

namespace G54.BLL.Services;

public interface IBusinessObjectiveService
{
    /// <summary>The objectives the caller may read: all of them for Branch scope or wider, only those with a target in the caller's department subtree for Dept scope.</summary>
    Task<IReadOnlyList<BusinessObjectiveDto>> GetObjectivesAsync(
        Guid accountId,
        BusinessObjectiveFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<BusinessObjectiveDetailDto> GetObjectiveAsync(
        Guid accountId,
        Guid id,
        CancellationToken cancellationToken = default);

    Task<BusinessObjectiveDto> CreateObjectiveAsync(
        BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken = default);

    Task<BusinessObjectiveDto> UpdateObjectiveAsync(
        Guid id,
        BusinessObjectiveSaveDto dto,
        CancellationToken cancellationToken = default);

    Task DeleteObjectiveAsync(Guid id, CancellationToken cancellationToken = default);
}
