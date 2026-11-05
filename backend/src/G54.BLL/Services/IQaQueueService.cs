using G54.BLL.Dtos.Quality;

namespace G54.BLL.Services;

/// <summary>The QA queue and detail screens (SCR-REV-06). Contract: docs/api/QUALITY_ASSURANCE_CONTRACT.md.</summary>
public interface IQaQueueService
{
    /// <summary>Quality flags and unflagged 5% samples in one list, one tab at a time, with the count of every tab.</summary>
    Task<QaQueueDto> GetQueueAsync(QaQueueFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>One queue item with the submission, its evidence and the matched transactions. <paramref name="kind"/> is FLAG or SAMPLE.</summary>
    Task<QaQueueDetailDto> GetItemAsync(string kind, Guid id, CancellationToken cancellationToken = default);
}
