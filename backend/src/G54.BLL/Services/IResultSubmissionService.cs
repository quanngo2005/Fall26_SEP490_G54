using G54.BLL.Dtos.Submission;

namespace G54.BLL.Services;

public interface IResultSubmissionService
{
    Task<SubmissionContextDto> GetSubmissionContextAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionResultDto> CreateSubmissionAsync(
        Guid workId,
        Guid userId,
        CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubmissionDetailDto>> GetSubmissionsByWorkIdAsync(
        Guid workId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionDetailDto> GetSubmissionByIdAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubmissionResultDto> WithdrawSubmissionAsync(
        Guid submissionId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
