using Microsoft.AspNetCore.Http;

namespace G54.BLL.Services;

public interface IStorageService
{
    Task<IReadOnlyList<StoredFileMetadata>> UploadEvidenceFilesAsync(
        IEnumerable<IFormFile> files,
        CancellationToken cancellationToken = default);
}

public sealed record StoredFileMetadata(
    string FileName,
    string FilePath,
    string? MimeType,
    long FileSize);
