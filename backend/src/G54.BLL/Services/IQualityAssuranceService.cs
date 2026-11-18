using G54.BLL.Common.Models;
using G54.BLL.Dtos.Quality;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>Decides whether an approved result is picked for QA re-verification (BR-32).</summary>
public interface IQaSamplingPolicy
{
    bool ShouldSample(Work work);
}

/// <summary>
/// QA quality control of approved results: quality flags, rework requests and the random 5% re-verification.
/// Contract: docs/api/QUALITY_ASSURANCE_CONTRACT.md.
/// </summary>
public interface IQualityAssuranceService
{
    Task<QualityFlagDto> CreateFlagAsync(Guid submissionId, Guid userId, CreateQualityFlagDto dto, CancellationToken cancellationToken = default);

    Task<PagedResult<QualityFlagDto>> GetFlagsAsync(QualityFlagFilterDto filter, CancellationToken cancellationToken = default);

    Task<QualityFlagDto> GetFlagAsync(Guid flagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the flagged work back: the verified result becomes INVALID (its KPI progress is taken back out) and the work
    /// returns to IN_PROGRESS so the employee can submit it again.
    /// </summary>
    Task<QualityFlagDto> RequestReworkAsync(Guid flagId, Guid userId, RequestReworkDto dto, CancellationToken cancellationToken = default);

    Task<QualityFlagDto> DismissFlagAsync(Guid flagId, Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<QaSampleDto>> GetSamplesAsync(QaSampleFilterDto filter, CancellationToken cancellationToken = default);

    Task<QaSampleDecisionDto> DecideSampleAsync(Guid sampleId, Guid userId, DecideQaSampleDto dto, CancellationToken cancellationToken = default);
}
