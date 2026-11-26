using G54.BLL.Dtos.CallRecording;

namespace G54.BLL.Services;

/// <summary>
/// Call recordings of a result submission (docs/api/CALL_RECORDING_AI_TRIAGE_CONTRACT.md §2–§4). Only the submitter uploads;
/// the submitter and holders of <c>call_recording:play</c> (All) read and stream. <paramref name="userId"/> is an account or
/// employee id.
/// </summary>
public interface ICallRecordingService
{
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">No such submission.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The caller is not the submitter.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ConflictException">The submission was withdrawn.</exception>
    Task<CallRecordingDto> UploadAsync(
        Guid submissionId,
        Guid userId,
        UploadCallRecordingRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>Recordings of the submission, newest first.</summary>
    Task<IReadOnlyList<CallRecordingDto>> GetBySubmissionAsync(Guid submissionId, Guid userId, CancellationToken cancellationToken = default);

    Task<CallRecordingDto> GetAsync(Guid recordingId, Guid userId, CancellationToken cancellationToken = default);

    /// <exception cref="G54.BLL.Common.Exceptions.ConflictException">The recording is not <c>READY</c>.</exception>
    Task<CallRecordingStreamDto> OpenStreamAsync(Guid recordingId, Guid userId, CancellationToken cancellationToken = default);
}
