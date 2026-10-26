namespace G54.BLL.Services;

public interface IEvidenceService
{
    /// <summary>
    /// Authorises <paramref name="userId"/> (account or employee id) and opens the evidence file.
    /// Allowed: the employee who submitted it (or filed the appeal it belongs to), a verifier whose
    /// <c>verification:decide</c> scope covers that employee's department, and administrators.
    /// </summary>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">No such evidence, or its file is gone.</exception>
    /// <exception cref="G54.BLL.Common.Exceptions.ForbiddenException">The caller may not see this evidence.</exception>
    Task<EvidenceFileDto> OpenAsync(Guid userId, Guid evidenceId, CancellationToken cancellationToken = default);
}

/// <summary>The caller owns <see cref="Content"/> and must dispose it.</summary>
public sealed record EvidenceFileDto(
    Stream Content,
    string FileName,
    string ContentType,
    bool CanPreview);
