using G54.BLL.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class StorageService(
    IConfiguration configuration,
    ILogger<StorageService> logger) : IStorageService
{
    public const long DefaultMaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB

    private const int MaxFileNameLength = 255; // evidence.file_name column

    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".xlsx",
        ".xls",
        ".docx",
        ".doc",
        ".png",
        ".jpg",
        ".jpeg",
    };

    private static readonly Action<ILogger, string, Exception?> LogTraversalAttempt =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "PathTraversalAttempt"), "Potential path traversal attempt detected for filename: {FileName}");

    private static readonly Action<ILogger, string, string, long, Exception?> LogFileStored =
        LoggerMessage.Define<string, string, long>(LogLevel.Information, new EventId(2, "FileStored"), "Stored evidence file {OriginalName} as {StoredPath} ({Bytes} bytes)");

    private static readonly Action<ILogger, string, Exception?> LogFileDeleted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(3, "FileDeleted"), "Removed evidence file {StoredPath} after a failed request");

    private static readonly Action<ILogger, string, Exception?> LogFileDeleteFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4, "FileDeleteFailed"), "Could not remove evidence file {StoredPath}; it needs manual cleanup");

    public async Task<IReadOnlyList<StoredFileMetadata>> UploadEvidenceFilesAsync(
        IEnumerable<IFormFile> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        var pending = files.ToList();
        foreach (var file in pending)
        {
            await ValidateAsync(file, cancellationToken);
        }

        var baseDirectory = ResolveBaseDirectory();
        Directory.CreateDirectory(baseDirectory);
        var canonicalBaseDir = WithTrailingSeparator(Path.GetFullPath(baseDirectory));

        var results = new List<StoredFileMetadata>();
        try
        {
            foreach (var file in pending)
            {
                var sanitizedOriginalName = Path.GetFileName(file.FileName);
                var extension = Path.GetExtension(sanitizedOriginalName).ToLowerInvariant();
                var safeStoredFileName = $"{Guid.NewGuid():N}{extension}";
                var canonicalDestination = Path.GetFullPath(Path.Combine(baseDirectory, safeStoredFileName));

                // SEC-BE-H06: Prevent path traversal attacks
                if (!canonicalDestination.StartsWith(canonicalBaseDir, StringComparison.OrdinalIgnoreCase))
                {
                    LogTraversalAttempt(logger, file.FileName, null);
                    throw new ValidationException("Đường dẫn lưu trữ tệp không hợp lệ.");
                }

                await using (var fileStream = new FileStream(
                    canonicalDestination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await file.CopyToAsync(fileStream, cancellationToken);
                }

                results.Add(new StoredFileMetadata(
                    FileName: sanitizedOriginalName,
                    FilePath: safeStoredFileName,
                    MimeType: EvidenceFileValidator.ContentTypeFor(extension),
                    FileSize: file.Length));

                LogFileStored(logger, sanitizedOriginalName, safeStoredFileName, file.Length, null);
            }
        }
        catch
        {
            await DeleteEvidenceFilesAsync(results, CancellationToken.None);
            throw;
        }

        return results;
    }

    public Task<Stream> OpenEvidenceReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var path = ResolveStoredPath(filePath);
        if (!File.Exists(path))
        {
            throw new NotFoundException("Tệp bằng chứng không còn tồn tại trên máy chủ.");
        }

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteEvidenceFilesAsync(
        IEnumerable<StoredFileMetadata> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var file in files)
        {
            try
            {
                File.Delete(ResolveStoredPath(file.FilePath));
                LogFileDeleted(logger, file.FilePath, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ValidationException)
            {
                LogFileDeleteFailed(logger, file.FilePath, exception);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new ValidationException($"Tệp tin '{file.FileName}' rỗng (0 bytes).");
        }

        if (file.Length > DefaultMaxFileSizeBytes)
        {
            throw new ValidationException(
                $"Tệp tin '{file.FileName}' vượt quá dung lượng tối đa cho phép (25 MB).");
        }

        var sanitizedName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(sanitizedName) || sanitizedName.Length > MaxFileNameLength)
        {
            throw new ValidationException($"Tên tệp không hợp lệ hoặc dài quá {MaxFileNameLength} ký tự.");
        }

        var extension = Path.GetExtension(sanitizedName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new ValidationException(
                $"Định dạng tệp '{extension}' không được hỗ trợ. Chỉ chấp nhận các định dạng: {string.Join(", ", AllowedExtensions)}.");
        }

        var header = new byte[EvidenceFileValidator.HeaderLength];
        int read;
        await using (var stream = file.OpenReadStream())
        {
            read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        if (!EvidenceFileValidator.HasValidSignature(extension, header.AsSpan(0, read)))
        {
            throw new ValidationException(
                $"Nội dung tệp '{sanitizedName}' không đúng định dạng '{extension}'.");
        }
    }

    private string ResolveBaseDirectory()
    {
        var configuredFolder = configuration["Storage:EvidencePath"] ?? "uploads/evidence";
        return Path.IsPathRooted(configuredFolder)
            ? configuredFolder
            : Path.Combine(AppContext.BaseDirectory, configuredFolder);
    }

    /// <summary>
    /// Maps a stored <c>FilePath</c> to a location inside the evidence folder. Only the final segment is used, which keeps
    /// rows written by older versions (<c>uploads/evidence/name</c>) readable and rejects any path that escapes the folder.
    /// </summary>
    private string ResolveStoredPath(string filePath)
    {
        var name = Path.GetFileName(filePath?.Replace('\\', '/') ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            throw new ValidationException("Đường dẫn lưu trữ tệp không hợp lệ.");
        }

        var canonicalBaseDir = WithTrailingSeparator(Path.GetFullPath(ResolveBaseDirectory()));
        var canonicalPath = Path.GetFullPath(Path.Combine(canonicalBaseDir, name));
        if (!canonicalPath.StartsWith(canonicalBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            LogTraversalAttempt(logger, filePath ?? string.Empty, null);
            throw new ValidationException("Đường dẫn lưu trữ tệp không hợp lệ.");
        }

        return canonicalPath;
    }

    private static string WithTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
