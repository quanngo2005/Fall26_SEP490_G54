using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;

namespace G54.BLL.Services;

public interface IRankingRecordService
{
    /// <exception cref="G54.BLL.Common.Exceptions.ValidationException">periodId is missing or the tier is not configured.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">The period or the department does not exist.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ConflictException">The period is not locked.</exception>
    Task<PagedResult<RankingRecordDto>> GetRecordsAsync(RankingRecordFilterDto filter, CancellationToken cancellationToken = default);

    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">The id is not a final evaluation of a locked period.</exception>
    Task<RankingRecordDetailDto> GetRecordAsync(Guid evaluationId, CancellationToken cancellationToken = default);
}
