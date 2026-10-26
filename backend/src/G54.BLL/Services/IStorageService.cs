using Microsoft.AspNetCore.Http;

namespace G54.BLL.Services;

public interface IStorageService
{
    /// <summary>
    /// Validates every file first (size, extension, signature) and only then stores them, so a bad file never leaves
    /// earlier files of the same request behind.
    /// </summary>
    Task<IReadOnlyList<StoredFileMetadata>> UploadEvidenceFilesAsync(
        IEnumerable<IFormFile> files,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored evidence file for reading. <paramref name="filePath"/> is the value saved in <c>Evidence.FilePath</c>.</summary>
    /// <exception cref="G54.BLL.Common.Exceptions.NotFoundException">The file is not on disk.</exception>
    Task<Stream> OpenEvidenceReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Best-effort removal of files stored by a request whose database work then failed.</summary>
    Task DeleteEvidenceFilesAsync(
        IEnumerable<StoredFileMetadata> files,
        CancellationToken cancellationToken = default);
}

/// <param name="FilePath">Name of the stored file inside the evidence folder (no directory), as saved in the database.</param>
public sealed record StoredFileMetadata(
    string FileName,
    string FilePath,
    string? MimeType,
    long FileSize);
