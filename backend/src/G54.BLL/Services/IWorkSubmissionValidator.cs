using G54.BLL.Dtos.Submission;
using G54.DAL.Entities;

namespace G54.BLL.Services;

public interface IWorkSubmissionValidator
{
    Task<WorkSubmissionValidationResult> ValidateAsync(
        Work work,
        Guid employeeId,
        CreateWorkSubmissionRequestDto request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkSubmissionValidationResult(bool IsValid, IReadOnlyList<string> Errors);
