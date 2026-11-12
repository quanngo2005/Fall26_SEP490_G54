using G54.BLL.Common.Models;
using G54.BLL.Dtos.Submission;

namespace G54.BLL.Services;

/// <summary>The caller's own submission history and how each submission was reconciled.</summary>
public interface IMySubmissionService
{
    Task<PagedResult<MySubmissionListItemDto>> GetMineAsync(
        Guid userId,
        MySubmissionFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<MySubmissionDetailDto> GetDetailAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
